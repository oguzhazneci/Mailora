using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models;
using MYAcademy_IdentityEmailProject.Models.Messages;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    public class MessageController : Controller
    {
        private const int InboxPageSize = 10;
        private const int BulkLimit = 100;

        private readonly EmailContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly IHtmlSanitizer _sanitizer;
        private readonly IMailboxCounter _counter;
        private readonly IMailService _mailService;
        private readonly ILogger<MessageController> _logger;

        public MessageController(EmailContext context, UserManager<AppUser> userManager,
                                 IHtmlSanitizer sanitizer, IMailboxCounter counter,
                                 IMailService mailService, ILogger<MessageController> logger)
        {
            _context = context;
            _userManager = userManager;
            _sanitizer = sanitizer;
            _counter = counter;
            _mailService = mailService;
            _logger = logger;
        }
        /* =====================================================================
           GELEN KUTUSU (7.2 – 7.4)
           ===================================================================== */

        public async Task<IActionResult> Inbox(InboxQuery query)
        {
            string userId = _userManager.GetUserId(User)!;

            // 1–2) Temel + kapsam (kategori, arama). Çip sayıları bu kapsamdan.
            IQueryable<MailboxItem> scoped = BuildInboxScope(userId, query);
            var counts = await CountScopeAsync(scoped);

            // 3) Durum filtresi
            IQueryable<MailboxItem> filtered = query.Filter switch
            {
                InboxFilter.Unread => scoped.Where(i => !i.IsRead),
                InboxFilter.Starred => scoped.Where(i => i.IsStarred),
                _ => scoped
            };

            int totalItems = query.Filter switch
            {
                InboxFilter.Unread => counts.Unread,
                InboxFilter.Starred => counts.Starred,
                _ => counts.All
            };

            // 4) Sayfa: geçersiz numara en yakın geçerli sayfaya sınırlanır
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)InboxPageSize));
            int page = Math.Clamp(query.Page, 1, totalPages);

            // Sıralama + eşitlik bozucu (aynı tarihli mesajlar sayfalar arasında kaymasın)
            IOrderedQueryable<MailboxItem> ordered = query.Sort == InboxSort.Oldest
                ? filtered.OrderBy(i => i.Message.SentDate).ThenBy(i => i.MailboxItemId)
                : filtered.OrderByDescending(i => i.Message.SentDate).ThenByDescending(i => i.MailboxItemId);

            var pageItems = await ordered
                .Skip((page - 1) * InboxPageSize)
                .Take(InboxPageSize)
                .Include(i => i.Message)
                    .ThenInclude(m => m.Sender)
                .Include(i => i.Message)
                    .ThenInclude(m => m.Category)
                .ToListAsync();

            var model = new InboxViewModel
            {
                Query = query,
                Items = pageItems,
                Categories = await _context.Categories.OrderBy(c => c.CategoryId).ToListAsync(),
                AllCount = counts.All,
                UnreadCount = counts.Unread,
                StarredCount = counts.Starred,
                Page = page,
                TotalPages = totalPages,
                TotalItems = totalItems,
                PageSize = InboxPageSize
            };

            return View(model);
        }

        // Inbox sayfasındaki çip sayıları: aynı filtrelerle, sayfa yenilenmeden (7.8).
        [HttpGet]
        public async Task<IActionResult> InboxCounts(InboxQuery query)
        {
            string userId = _userManager.GetUserId(User)!;
            var counts = await CountScopeAsync(BuildInboxScope(userId, query));
            return Json(new { all = counts.All, unread = counts.Unread, starred = counts.Starred });
        }

        // Temel koşul + kategori + arama. Inbox ve InboxCounts aynı kapsamı kullanır (7.8).
        private IQueryable<MailboxItem> BuildInboxScope(string userId, InboxQuery query)
        {
            IQueryable<MailboxItem> scoped = _context.MailboxItems
                .Where(i => i.UserId == userId && i.Folder == MailFolder.Inbox && i.DeletedDate == null);

            if (query.CategoryId.HasValue)
            {
                scoped = scoped.Where(i => i.Message.CategoryId == query.CategoryId);
            }

            if (!string.IsNullOrWhiteSpace(query.Q))
            {
                string term = query.Q.Trim();
                scoped = scoped.Where(i =>
                    i.Message.Subject.Contains(term) ||
                    i.Message.Body.Contains(term) ||
                    i.Message.Sender.Email!.Contains(term) ||
                    i.Message.Sender.Name!.Contains(term) ||
                    i.Message.Sender.Surname!.Contains(term));
            }

            return scoped;
        }

        // Çip sayıları: TEK sorgu (6.4'teki sidebar tekniği)
        private static async Task<(int All, int Unread, int Starred)> CountScopeAsync(IQueryable<MailboxItem> scoped)
        {
            var counts = await scoped
                .GroupBy(i => i.UserId)
                .Select(g => new
                {
                    All = g.Count(),
                    Unread = g.Count(i => !i.IsRead),
                    Starred = g.Count(i => i.IsStarred)
                })
                .FirstOrDefaultAsync();

            return (counts?.All ?? 0, counts?.Unread ?? 0, counts?.Starred ?? 0);
        }

        /* =====================================================================
           MESAJ DETAYI (7.5)
           ===================================================================== */

        public async Task<IActionResult> Detail(int id)
        {
            string userId = _userManager.GetUserId(User)!;

            // Mesajın BENİM kutumdaki kopyası. Sahiplik kontrolü sorgunun kendisinde.
            // Kendine gönderilmiş mesajda iki kopya olabilir: Gelen Kutusu tercih edilir.
            var item = await _context.MailboxItems
                .Where(i => i.MessageId == id && i.UserId == userId)
                .OrderBy(i => i.Folder == MailFolder.Inbox ? 0 : 1)
                .Include(i => i.Message).ThenInclude(m => m.Sender)
                .Include(i => i.Message).ThenInclude(m => m.Category)
                .Include(i => i.Message).ThenInclude(m => m.Recipients)
                .FirstOrDefaultAsync();

            if (item == null)
            {
                // Kopyam yok: mesaj ya hiç yok (404) ya başkasına ait (403).
                // Her durumda 404 için bu iki satırı "return NotFound();" ile değiştir.
                bool exists = await _context.Messages.AnyAsync(m => m.MessageId == id);
                return exists ? Forbid() : NotFound();
            }

            // Açmak = okumak. Idempotent bir değişiklik.
            if (!item.IsRead)
            {
                item.IsRead = true;
                await _context.SaveChangesAsync();   // change tracking: sadece IsRead güncellenir
            }

            return View(new MessageDetailViewModel
            {
                Item = item,
                SafeBodyHtml = _sanitizer.Sanitize(item.Message.Body)
            });
        }

        /* =====================================================================
           TEK KOPYA İŞLEMLERİ (7.6) — her yanıt güncel sayaçları da taşır (7.8)
           ===================================================================== */

        // Yıldız: "tersine çevir" değil "şuna ayarla" (idempotent).
        // id = MailboxItemId (benim kopyam). Sahiplik kontrolü sorguda.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStar(int id, bool isStarred)
        {
            string userId = _userManager.GetUserId(User)!;

            var item = await _context.MailboxItems
                .FirstOrDefaultAsync(i => i.MailboxItemId == id && i.UserId == userId);

            if (item == null)
            {
                return NotFound();
            }

            item.IsStarred = isStarred;
            await _context.SaveChangesAsync();   // değer aynıysa UPDATE gönderilmez

            return Json(new { isStarred = item.IsStarred, counts = await _counter.GetAsync(userId) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetRead(int id, bool isRead)
        {
            string userId = _userManager.GetUserId(User)!;

            var item = await _context.MailboxItems
                .FirstOrDefaultAsync(i => i.MailboxItemId == id && i.UserId == userId);

            if (item == null)
            {
                return NotFound();
            }

            item.IsRead = isRead;
            await _context.SaveChangesAsync();

            return Json(new { isRead = item.IsRead, counts = await _counter.GetAsync(userId) });
        }

        /* =====================================================================
           TOPLU İŞLEMLER (7.7) — satırları okumadan tek UPDATE
           Başkasına ait Id'ler WHERE'deki UserId koşuluna uymadığı için sessizce atlanır.
           ===================================================================== */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkSetRead(int[] ids, bool isRead)
        {
            if (ids.Length == 0 || ids.Length > BulkLimit)
            {
                return BadRequest();
            }

            string userId = _userManager.GetUserId(User)!;

            int count = await _context.MailboxItems
                .Where(i => ids.Contains(i.MailboxItemId) && i.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.IsRead, isRead));

            return Json(new { count, counts = await _counter.GetAsync(userId) });
        }

        // Çöp kutusuna taşı = silinme tarihini doldur (soft delete).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkMoveToTrash(int[] ids)
        {
            if (ids.Length == 0 || ids.Length > BulkLimit)
            {
                return BadRequest();
            }

            string userId = _userManager.GetUserId(User)!;
            var now = DateTime.Now;   // uygulamanın saati; SQL'de GETDATE() olmasın

            int count = await _context.MailboxItems
                .Where(i => ids.Contains(i.MailboxItemId) && i.UserId == userId && i.DeletedDate == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DeletedDate, now));

            return Json(new { count, counts = await _counter.GetAsync(userId) });
        }

        // Geri al / geri yükle = silinme tarihini temizle. 9. aşamada Çöp Kutusu da kullanacak.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkRestore(int[] ids)
        {
            if (ids.Length == 0 || ids.Length > BulkLimit)
            {
                return BadRequest();
            }

            string userId = _userManager.GetUserId(User)!;

            int count = await _context.MailboxItems
                .Where(i => ids.Contains(i.MailboxItemId) && i.UserId == userId && i.DeletedDate != null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.DeletedDate, (DateTime?)null));

            return Json(new { count, counts = await _counter.GetAsync(userId) });
        }

        /* =====================================================================
           YENİ MESAJ (8.2 – 8.4)
           ===================================================================== */

        [HttpGet]
        public async Task<IActionResult> CreateMessage()
        {
            await LoadCategoriesAsync();
            return View(new CreateMessageViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMessage(CreateMessageViewModel model)
        {
            // 1) Biçim kuralları (attribute'lar) bu noktada ModelState'e zaten işlendi.

            // 2) İçerik önce temizlenir; "boş mu" kontrolü temizlenmiş içerik üzerinden yapılır (Kavram 3).
            string safeBody = _sanitizer.Sanitize(model.Body ?? string.Empty);

            // 3) Veritabanı ve servis gerektiren iş kuralları (Kavram 1)
            if (model.CategoryId.HasValue &&
                !await _context.Categories.AnyAsync(c => c.CategoryId == model.CategoryId))
            {
                ModelState.AddModelError(nameof(model.CategoryId), "Seçilen kategori bulunamadı.");
            }

            if (!string.IsNullOrEmpty(model.Body) && !HasVisibleText(safeBody))
            {
                ModelState.AddModelError(nameof(model.Body), "Mesaj içeriği boş olamaz.");
            }

            if (!ModelState.IsValid)
            {
                // Forma geri gösterilen içerik de temizlenmiş olmalı (Kavram 4).
                model.Body = safeBody;
                await LoadCategoriesAsync();
                return View(model);
            }

            // 4) Alıcıyı çözümle: Mailora'da kayıtlı mı? (Kavram 1)
            string senderId = _userManager.GetUserId(User)!;
            string receiverEmail = model.ReceiverEmail.Trim();
            var receiver = await _userManager.FindByEmailAsync(receiverEmail);
            var now = DateTime.Now;

            // 5) Nesne grafiği: her değerin kaynağı belli (Kavram 2)
            var message = new Message
            {
                SenderId = senderId,                 // oturumdan, formdan değil
                Subject = model.Subject.Trim(),
                Body = safeBody,                     // temizlenmiş içerik
                CategoryId = model.CategoryId,
                CreatedDate = now,
                UpdatedDate = now,
                SentDate = now                       // dolu = gönderilmiş (taslak değil)
            };

            message.Recipients.Add(new MessageRecipient
            {
                Email = receiver?.Email ?? receiverEmail,
                UserId = receiver?.Id,               // dış adreste null
                RecipientType = RecipientType.To
            });

            // Gönderenin kopyası: Gönderilenler
            message.MailboxItems.Add(new MailboxItem
            {
                UserId = senderId,
                Folder = MailFolder.Sent,
                IsRead = true
            });

            // Alıcının kopyası: sadece kayıtlı kullanıcıysa Gelen Kutusu
            if (receiver != null)
            {
                message.MailboxItems.Add(new MailboxItem
                {
                    UserId = receiver.Id,
                    Folder = MailFolder.Inbox,
                    IsRead = false
                });
            }

            // 6) TEK SaveChanges = tek transaction: ya hepsi yazılır ya hiçbiri (8.1, Kavram 2)
            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // 7) Kayıt tamam. Geri alınamayan işlem (dış mail) EN SONDA (8.1, Kavram 3).
            //    Toast, mesajın nereye gittiğini açıkça söyler (Kavram 4).
            if (receiver != null)
            {
                string receiverName = $"{receiver.Name} {receiver.Surname}".Trim();
                TempData["Toast"] = $"Mesajınız gönderildi · Alıcı: {(receiverName.Length > 0 ? receiverName : receiver.Email)} (Mailora)";
            }
            else if (await TrySendExternalAsync(message, receiverEmail))
            {
                TempData["Toast"] = $"Mesajınız e-posta olarak gönderildi · Alıcı: {receiverEmail} (Mailora dışı)";
                TempData["ToastIcon"] = "bi-envelope-arrow-up";
            }
            else
            {
                TempData["Toast"] = $"Mesajınız kaydedildi ancak {receiverEmail} adresine iletilemedi.";
                TempData["ToastIcon"] = "bi-exclamation-triangle";
            }

            // 8) Post-Redirect-Get: az önce gönderilen mesajın detayı
            return RedirectToAction("Detail", new { id = message.MessageId });
        }
        private async Task LoadCategoriesAsync()
        {
            var categories = await _context.Categories.OrderBy(c => c.CategoryId).ToListAsync();
            ViewBag.Categories = new SelectList(categories, "CategoryId", "CategoryName");
        }

        // Etiketler çıkarılınca görünür bir metin kalıyor mu? (Güvenlik değil, boşluk kontrolü.)
        private static bool HasVisibleText(string html)
        {
            string text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
            return !string.IsNullOrWhiteSpace(text);
        }
        // Dış adrese gerçek e-posta. Başarısızlık kaydı geri almaz; sadece false döner (8.1, 3. karar).
        private async Task<bool> TrySendExternalAsync(Message message, string toEmail)
        {
            string senderName = SenderDisplayName();
            string? senderEmail = User.FindFirstValue(ClaimTypes.Email);

            // Mesajın kendisi zaten temizlenmiş HTML; altına kaynağı belirten bir not eklenir.
            string html = message.Body +
                "<hr style=\"border:none;border-top:1px solid #ddd;margin:24px 0 12px\">" +
                "<p style=\"color:#888;font-size:12px\">Bu e-posta <b>" + WebUtility.HtmlEncode(senderName) +
                "</b> tarafından Mailora üzerinden gönderildi. Yanıtınız doğrudan göndericiye iletilir.</p>";

            try
            {
                await _mailService.SendAsync(toEmail, message.Subject, html, isHtml: true,
                    fromName: $"{senderName} (Mailora)",
                    replyToEmail: senderEmail);
                return true;
            }
            catch (Exception ex)
            {
                // Loga kişisel veri (alıcı adresi) değil, sadece Id yazılır (Kavram 3).
                _logger.LogError(ex, "Dış adrese gönderim başarısız. MessageId: {MessageId}", message.MessageId);
                return false;
            }
        }

        // Gönderenin adı claim'lerden (6.3): veritabanı sorgusu yok.
        private string SenderDisplayName()
        {
            string fullName = $"{User.FindFirstValue(ClaimTypes.GivenName)} {User.FindFirstValue(ClaimTypes.Surname)}".Trim();
            return fullName.Length > 0 ? fullName : (User.Identity?.Name ?? "Bir Mailora kullanıcısı");
        }
        public async Task<IActionResult> Sendbox()
        {
            string userId = _userManager.GetUserId(User)!;

            var items = await _context.MailboxItems
                .Where(i => i.UserId == userId && i.Folder == MailFolder.Sent && i.DeletedDate == null)
                .Include(i => i.Message)
                    .ThenInclude(m => m.Recipients)
                .OrderByDescending(i => i.Message.SentDate)
                .ToListAsync();

            return View(items);
        }
    }
}