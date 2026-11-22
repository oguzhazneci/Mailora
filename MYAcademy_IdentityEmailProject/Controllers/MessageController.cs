using Ganss.Xss;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Helpers;
using MYAcademy_IdentityEmailProject.Models;
using MYAcademy_IdentityEmailProject.Models.Messages;
using MYAcademy_IdentityEmailProject.Services;
using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    public class MessageController : Controller
    {
        private const int PageSize = 10;
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
        MESAJ LİSTELERİ (7.2 – 7.4, ortak motor 9.2)
        Her klasör = MailboxItems üzerinde farklı bir koşul (9.1, Kavram 1).
        Action'lar sadece hangi görünümü istediklerini söyler.
        ===================================================================== */

        public Task<IActionResult> Inbox(MailboxQuery query) => ListAsync(MailboxView.Inbox, query);

        public Task<IActionResult> Sent(MailboxQuery query) => ListAsync(MailboxView.Sent, query);

        public Task<IActionResult> Starred(MailboxQuery query) => ListAsync(MailboxView.Starred, query);

        public Task<IActionResult> Trash(MailboxQuery query) => ListAsync(MailboxView.Trash, query);

        public Task<IActionResult> Drafts(MailboxQuery query) => ListAsync(MailboxView.Drafts, query);

        // Liste sayfasındaki çip sayıları: aynı kapsamla, sayfa yenilenmeden (7.8).
        // view: hangi listenin sayıları (kullanıcının kendi satırlarıyla sınırlı, BuildScope'a bakın).
        [HttpGet]
        public async Task<IActionResult> ListCounts(MailboxView view, MailboxQuery query)
        {
            string userId = _userManager.GetUserId(User)!;
            var counts = await CountScopeAsync(BuildScope(userId, view, query));
            return Json(new { all = counts.All, unread = counts.Unread, starred = counts.Starred });
        }

        // Ortak liste motoru: kapsam → sayılar → durum filtresi → sayfa → sıralama → o sayfanın satırları
        private async Task<IActionResult> ListAsync(MailboxView view, MailboxQuery query)
        {
            string userId = _userManager.GetUserId(User)!;

            // 1–2) Görünüm koşulu + kapsam (kategori, arama). Çip sayıları bu kapsamdan.
            IQueryable<MailboxItem> scoped = BuildScope(userId, view, query);
            var counts = await CountScopeAsync(scoped);

            // 3) Durum filtresi
            IQueryable<MailboxItem> filtered = query.Filter switch
            {
                MailboxFilter.Unread => scoped.Where(i => !i.IsRead),
                MailboxFilter.Starred => scoped.Where(i => i.IsStarred),
                _ => scoped
            };

            int totalItems = query.Filter switch
            {
                MailboxFilter.Unread => counts.Unread,
                MailboxFilter.Starred => counts.Starred,
                _ => counts.All
            };

            // 4) Sayfa: geçersiz numara en yakın geçerli sayfaya sınırlanır
            int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)PageSize));
            int page = Math.Clamp(query.Page, 1, totalPages);

            // Sıralama tarihi görünüme göre (Kavram 1). Expression: EF bunu SQL'e çevirir.
            // Func olsaydı derlenir ama tüm satırları belleğe çekip C#'ta sıralardı.
            Expression<Func<MailboxItem, DateTime?>> sortKey = view switch
            {
                MailboxView.Trash => i => i.DeletedDate,
                // Taslağın gönderim tarihi yok: son düzenleme anı (9.1, Kavram 1)
                MailboxView.Drafts => i => (DateTime?)i.Message.UpdatedDate,
                _ => i => i.Message.SentDate
            };

            // Eşitlik bozucu: aynı tarihli mesajlar sayfalar arasında kaymasın
            IOrderedQueryable<MailboxItem> ordered = query.Sort == MailboxSort.Oldest
                ? filtered.OrderBy(sortKey).ThenBy(i => i.MailboxItemId)
                : filtered.OrderByDescending(sortKey).ThenByDescending(i => i.MailboxItemId);

            var pageItems = await ordered
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .Include(i => i.Message)
                    .ThenInclude(m => m.Sender)
                .Include(i => i.Message)
                    .ThenInclude(m => m.Category)
                .Include(i => i.Message)
                    .ThenInclude(m => m.Recipients)
                .ToListAsync();

            // Giden satırlardaki kayıtlı alıcıların adları: sayfa başına TEK sorgu (Kavram 2, N+1'e karşı)
            var recipientIds = pageItems
                .Where(i => i.Folder != MailFolder.Inbox)
                .SelectMany(i => i.Message.Recipients)
                .Where(r => r.UserId != null)
                .Select(r => r.UserId!)
                .Distinct()
                .ToList();

            var userNames = new Dictionary<string, string>();
            var userPhotos = new Dictionary<string, string>();
            if (recipientIds.Count > 0)
            {
                var people = await _context.Users
                    .Where(u => recipientIds.Contains(u.Id))          // SQL: WHERE Id IN (...) — hâlâ TEK sorgu
                    .Select(u => new { u.Id, u.Name, u.Surname, u.Email, u.ImageUrl })
                    .ToListAsync();

                foreach (var p in people)
                {
                    string fullName = $"{p.Name} {p.Surname}".Trim();
                    userNames[p.Id] = fullName.Length > 0 ? fullName : p.Email ?? "";

                    if (AvatarHelper.IsAvatarFile(p.ImageUrl))
                    {
                        userPhotos[p.Id] = p.ImageUrl;
                    }
                }
            }

            var model = new MailboxViewModel
            {
                View = view,
                Query = query,
                Items = pageItems,
                Categories = await _context.Categories.OrderBy(c => c.CategoryId).ToListAsync(),
                AllCount = counts.All,
                UnreadCount = counts.Unread,
                StarredCount = counts.Starred,
                Page = page,
                TotalPages = totalPages,
                TotalItems = totalItems,
                PageSize = PageSize,
                UserNames = userNames,
                UserPhotos = userPhotos
            };

            // Tüm klasörler aynı view dosyasını kullanır (9.1, karar 1-B)
            return View("Mailbox", model);
        }

        // Görünüm koşulu + kategori + arama. ListAsync ve ListCounts aynı kapsamı kullanır (7.8).
        private IQueryable<MailboxItem> BuildScope(string userId, MailboxView view, MailboxQuery query)
        {
            // Sahiplik her görünümde ilk koşul: sadece BENİM satırlarım (Kavram 3)
            IQueryable<MailboxItem> scoped = _context.MailboxItems.Where(i => i.UserId == userId);

            // Görünüm koşulu (9.1'deki tablo). Diğer klasörler 9.3–9.6'da eklenecek.
            scoped = view switch
            {
                MailboxView.Inbox => scoped.Where(i => i.Folder == MailFolder.Inbox && i.DeletedDate == null),
                MailboxView.Sent => scoped.Where(i => i.Folder == MailFolder.Sent && i.DeletedDate == null),
                // Klasör değil özellik: karışık liste. Koşul sidebar sayacıyla AYNI (9.4, Kavram 2).
                MailboxView.Starred => scoped.Where(i => i.IsStarred && i.DeletedDate == null),
                // Çöp Kutusu bir klasör değil bir durum: her klasörden silinenler (9.1)
                MailboxView.Trash => scoped.Where(i => i.DeletedDate != null),
                MailboxView.Drafts => scoped.Where(i => i.Folder == MailFolder.Draft && i.DeletedDate == null),
                _ => throw new NotSupportedException($"{view} görünümü henüz tanımlı değil.")
            };
            
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
                    // Gelen kopyada karşı taraf gönderendir (Kavram 1)
                    (i.Folder == MailFolder.Inbox && (
                        i.Message.Sender.Email!.Contains(term) ||
                        i.Message.Sender.Name!.Contains(term) ||
                        i.Message.Sender.Surname!.Contains(term))) ||
                    // Gönderilen/taslak kopyada karşı taraf alıcılardır: adres ya da (kayıtlıysa) ad soyad
                    (i.Folder != MailFolder.Inbox && i.Message.Recipients.Any(r =>
                        r.Email.Contains(term) ||
                        _context.Users.Any(u => u.Id == r.UserId &&
                            (u.Name!.Contains(term) || u.Surname!.Contains(term))))));
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

            // Taslaklar yıldızlanmaz (9.1, karar 2-A). Arayüzde buton olmaması yetmez (Kavram 3).
            if (item.Folder == MailFolder.Draft)
            {
                return BadRequest();
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

        // Kalıcı sil: SADECE çöp kutusundaki kopyalarım (DeletedDate != null).
        // Gelen Kutusu'ndaki bir Id buraya gönderilse bile silinmez.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeleteForever(int[] ids)
        {
            if (ids.Length == 0 || ids.Length > BulkLimit)
            {
                return BadRequest();
            }

            string userId = _userManager.GetUserId(User)!;

            int count = await DeleteForeverAsync(_context.MailboxItems
                .Where(i => ids.Contains(i.MailboxItemId) && i.UserId == userId && i.DeletedDate != null));

            return Json(new { count, counts = await _counter.GetAsync(userId) });
        }

        // Çöpü boşalt: normal form POST → Redirect → GET (Kavram 5, PRG)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmptyTrash()
        {
            string userId = _userManager.GetUserId(User)!;

            int count = await DeleteForeverAsync(_context.MailboxItems
                .Where(i => i.UserId == userId && i.DeletedDate != null));

            TempData["Toast"] = count > 0
                ? $"Çöp kutusu boşaltıldı · {count} mesaj kalıcı olarak silindi."
                : "Çöp kutusu zaten boş.";
            TempData["ToastIcon"] = "bi-trash3-fill";

            return RedirectToAction("Trash");
        }

        // Kalıcı silmenin ortak kısmı (Kavram 3 ve 4)
        private async Task<int> DeleteForeverAsync(IQueryable<MailboxItem> myTrashedItems)
        {
            // İki ayrı komut → açık transaction. Commit'e ulaşılmazsa ikisi birden geri alınır.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            // 1) Benim kopyalarım
            int count = await myTrashedItems.ExecuteDeleteAsync();

            // 2) Hiç kopyası kalmayan mesajlar (alıcı satırları veritabanındaki ON DELETE CASCADE ile).
            //    "Kuralı bozan her mesaj": arada sahipsiz kalmış bir mesaj olursa o da temizlenir.
            await _context.Messages
                .Where(m => !m.MailboxItems.Any())          // SQL: NOT EXISTS (...)
                .ExecuteDeleteAsync();

            await transaction.CommitAsync();
            return count;
        }

        /* =====================================================================
           YENİ MESAJ ve TASLAKLAR (8.2 – 8.4, 9.6)
           Tek form, iki niyet: "send" → tam doğrulama + gönderim, "draft" → gevşek doğrulama + kayıt.
           ===================================================================== */

        [HttpGet]
        public async Task<IActionResult> CreateMessage(int? draftId, int? replyTo, int? forwardOf)
        {
            var model = new CreateMessageViewModel();

            if (draftId.HasValue)
            {
                var draft = await FindMyDraftAsync(draftId.Value);
                if (draft == null)
                {
                    return DraftGone();
                }

                model.DraftId = draft.MessageId;
                model.ReceiverEmail = draft.Recipients.FirstOrDefault()?.Email ?? string.Empty;
                model.CategoryId = draft.CategoryId;
                model.Subject = draft.Subject;
                model.Body = _sanitizer.Sanitize(draft.Body);   // çıktıda da temizle (8.2, derinlemesine savunma)
            }
            else if (replyTo.HasValue || forwardOf.HasValue)
            {
                // Yanıtla / İlet = önceden doldurulmuş yeni mesaj (Kavram 1)
                bool isReply = replyTo.HasValue;
                int sourceId = replyTo ?? forwardOf!.Value;

                var mine = await FindMyCopyAsync(sourceId);
                if (mine == null)
                {
                    // Detail ile aynı kural (7.5): mesaj hiç yok → 404, başkasının → 403
                    bool exists = await _context.Messages.AnyAsync(m => m.MessageId == sourceId);
                    return exists ? Forbid() : NotFound();
                }

                var source = mine.Message;
                if (source.SentDate == null)
                {
                    return NotFound();   // taslağa yanıt verilmez, taslak iletilmez
                }

                model.CategoryId = source.CategoryId;
                model.Subject = WithPrefix(isReply ? "Re:" : "Fwd:", source.Subject);

                if (isReply)
                {
                    // Gelen kopyada gönderene, gönderilen kopyada alıcıya (Kavram 2)
                    model.ReceiverEmail = mine.Folder == MailFolder.Inbox
                        ? source.Sender.Email ?? string.Empty
                        : source.Recipients.OrderBy(r => r.RecipientType).Select(r => r.Email).FirstOrDefault() ?? string.Empty;
                }

                model.Body = isReply ? BuildReplyQuote(source) : BuildForwardBlock(source);
                ViewData["ComposeHeading"] = isReply ? "Yanıtla" : "İlet";
            }

            await LoadCategoriesAsync();
            return View(model);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateMessage(CreateMessageViewModel model, string? intent)
        {
            bool isDraft = intent == "draft";
            string safeBody = _sanitizer.Sanitize(model.Body ?? string.Empty);

            if (isDraft)
            {
                // Taslak eksik olabilir: [Required]/[EmailAddress] hatalarını at, kendi kurallarımızı uygula (Kavram 2)
                ModelState.Clear();
                ValidateDraft(model, safeBody);
            }
            else if (!string.IsNullOrEmpty(model.Body) && !HasVisibleText(safeBody))
            {
                ModelState.AddModelError(nameof(model.Body), "Mesaj içeriği boş olamaz.");
            }

            if (model.CategoryId.HasValue && !await _context.Categories.AnyAsync(c => c.CategoryId == model.CategoryId))
            {
                ModelState.AddModelError(nameof(model.CategoryId), "Seçilen kategori bulunamadı.");
            }

            // DraftId istemciden gelir: benim mi, hâlâ taslak mı? (Kavram 4)
            Message? draft = null;
            if (model.DraftId.HasValue)
            {
                draft = await FindMyDraftAsync(model.DraftId.Value);
                if (draft == null)
                {
                    return DraftGone();
                }
            }

            if (!ModelState.IsValid)
            {
                model.Body = safeBody;
                await LoadCategoriesAsync();
                return View(model);
            }

            return isDraft
                ? await SaveDraftAsync(model, safeBody, draft)
                : await SendMessageAsync(model, safeBody, draft);
        }

        // Taslağı kaydet: yeni ya da mevcut taslağı güncelle
        private async Task<IActionResult> SaveDraftAsync(CreateMessageViewModel model, string safeBody, Message? draft)
        {
            string userId = _userManager.GetUserId(User)!;
            var now = DateTime.Now;

            if (draft == null)
            {
                draft = new Message { SenderId = userId, CreatedDate = now, SentDate = null };   // SentDate null = taslak (5.2)
                draft.MailboxItems.Add(new MailboxItem { UserId = userId, Folder = MailFolder.Draft, IsRead = true });
                _context.Messages.Add(draft);
            }

            draft.Subject = model.Subject?.Trim() ?? string.Empty;
            draft.Body = safeBody;
            draft.CategoryId = model.CategoryId;
            draft.UpdatedDate = now;

            // Taslakta alıcı sadece yazılan adrestir; kayıtlı kullanıcıyla eşleştirme GÖNDERİMDE (Kavram 6)
            draft.Recipients.Clear();                    // eski alıcı satırı listeden çıkar → SaveChanges'ta DELETE
            string? to = model.ReceiverEmail?.Trim();
            if (!string.IsNullOrEmpty(to))
            {
                draft.Recipients.Add(new MessageRecipient { Email = to, UserId = null, RecipientType = RecipientType.To });
            }

            await _context.SaveChangesAsync();

            TempData["Toast"] = "Taslak kaydedildi.";
            TempData["ToastIcon"] = "bi-file-earmark-check";

            // PRG: düzenleme adresine dön. Sonraki "Kaydet" artık AYNI taslağı günceller (Kavram 3).
            return RedirectToAction("CreateMessage", new { draftId = draft.MessageId });
        }

        // Gönder: yeni mesaj ya da taslaktan gönderim (karar 4-A: aynı satır)
        private async Task<IActionResult> SendMessageAsync(CreateMessageViewModel model, string safeBody, Message? draft)
        {
            string senderId = _userManager.GetUserId(User)!;
            string receiverEmail = model.ReceiverEmail.Trim();
            var receiver = await _userManager.FindByEmailAsync(receiverEmail);
            var now = DateTime.Now;

            Message message;
            if (draft == null)
            {
                message = new Message { SenderId = senderId, CreatedDate = now };
                message.MailboxItems.Add(new MailboxItem { UserId = senderId, Folder = MailFolder.Sent, IsRead = true });
                _context.Messages.Add(message);
            }
            else
            {
                // Taslak → gönderilmiş mesaj: Id aynı, gönderenin kopyası Draft'tan Sent'e geçer
                message = draft;
                message.MailboxItems.Single(i => i.UserId == senderId && i.Folder == MailFolder.Draft).Folder = MailFolder.Sent;
                message.Recipients.Clear();
            }

            message.Subject = model.Subject.Trim();
            message.Body = safeBody;
            message.CategoryId = model.CategoryId;
            message.UpdatedDate = now;
            message.SentDate = now;

            message.Recipients.Add(new MessageRecipient
            {
                Email = receiver?.Email ?? receiverEmail,
                UserId = receiver?.Id,
                RecipientType = RecipientType.To
            });

            if (receiver != null)
            {
                message.MailboxItems.Add(new MailboxItem { UserId = receiver.Id, Folder = MailFolder.Inbox, IsRead = false });
            }

            // Hepsi tek SaveChanges = tek transaction (5.4)
            await _context.SaveChangesAsync();

            // Kayıt tamam. Geri alınamayan işlem (dış mail) EN SONDA (8.1). Toast alıcıyı açıkça söyler (8.4).
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

            // Post-Redirect-Get: gönderilen mesajın detayı
            return RedirectToAction("Detail", new { id = message.MessageId });
        }

        // Benim, hâlâ taslak ve çöpte değil. Değilse null (gönderilmiş, silinmiş ya da başkasının).
        private Task<Message?> FindMyDraftAsync(int messageId)
        {
            string userId = _userManager.GetUserId(User)!;

            return _context.Messages
                .Include(m => m.Recipients)
                .Include(m => m.MailboxItems.Where(i => i.UserId == userId))   // filtreli Include: sadece benim kopyam
                .FirstOrDefaultAsync(m =>
                    m.MessageId == messageId &&
                    m.SenderId == userId &&
                    m.SentDate == null &&
                    m.MailboxItems.Any(i => i.UserId == userId && i.Folder == MailFolder.Draft && i.DeletedDate == null));
        }

        // Eski sekmede açık kalmış ya da başkasına ait bir taslak (Kavram 5)
        private IActionResult DraftGone()
        {
            TempData["Toast"] = "Bu taslak artık düzenlenemiyor: gönderilmiş ya da silinmiş olabilir.";
            TempData["ToastIcon"] = "bi-info-circle";
            return RedirectToAction("Drafts");
        }

        // Taslak kuralları: eksik olabilir ama boş olamaz ve sınırları aşamaz (Kavram 2)
        private void ValidateDraft(CreateMessageViewModel model, string safeBody)
        {
            // Boş bırakılan alanlar model binding'de null gelir (string.Empty varsayılanı ezilir): ?. şart
            string to = model.ReceiverEmail?.Trim() ?? string.Empty;
            string subject = model.Subject?.Trim() ?? string.Empty;

            bool hasAnything = to.Length > 0 || model.CategoryId.HasValue || subject.Length > 0 || HasVisibleText(safeBody);
            if (!hasAnything)
            {
                ModelState.AddModelError(string.Empty, "Boş bir taslak kaydedilemez.");
            }

            if (to.Length > 256)
            {
                ModelState.AddModelError(nameof(model.ReceiverEmail), "E-posta adresi en fazla 256 karakter olabilir.");
            }

            if (subject.Length > 200)
            {
                ModelState.AddModelError(nameof(model.Subject), "Konu en fazla 200 karakter olabilir.");
            }

            if (safeBody.Length > 100_000)
            {
                ModelState.AddModelError(nameof(model.Body), "Mesaj içeriği çok uzun.");
            }
        }

        /* ---------- Yanıtla / İlet yardımcıları (9.7) ---------- */

        // Mesajın BENİM kopyası (Gelen kopya öncelikli; kendine gönderilen mesajda iki kopya olabilir). Detail ile aynı kural.
        private Task<MailboxItem?> FindMyCopyAsync(int messageId)
        {
            string userId = _userManager.GetUserId(User)!;

            return _context.MailboxItems
                .Where(i => i.MessageId == messageId && i.UserId == userId)
                .OrderBy(i => i.Folder == MailFolder.Inbox ? 0 : 1)
                .Include(i => i.Message).ThenInclude(m => m.Sender)
                .Include(i => i.Message).ThenInclude(m => m.Recipients)
                .FirstOrDefaultAsync();
        }

        // "Re: Re: Re:" olmasın. Önek bir format metni: dil kurallarıyla değil Ordinal karşılaştırılır (Kavram 3).
        private static string WithPrefix(string prefix, string subject)
        {
            subject = (subject ?? string.Empty).Trim();
            string result = subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? subject
                : $"{prefix} {subject}".Trim();
            return result.Length > 200 ? result[..200] : result;   // CreateMessageViewModel sınırı
        }

        private static readonly CultureInfo Tr = new("tr-TR");

        private static string PersonName(AppUser user)
        {
            string fullName = $"{user.Name} {user.Surname}".Trim();
            return fullName.Length > 0 ? fullName : user.Email ?? string.Empty;
        }

        // Yanıt: üstte boş satır (imleç orada), altta alıntı. Adlar kullanıcı verisi → HtmlEncode (Kavram 4).
        private string BuildReplyQuote(Message source)
        {
            string when = (source.SentDate ?? source.UpdatedDate).ToString("d MMMM yyyy dddd HH:mm", Tr);
            string name = WebUtility.HtmlEncode(PersonName(source.Sender));
            string email = WebUtility.HtmlEncode(source.Sender.Email ?? string.Empty);

            return "<p><br></p>" +
                   $"<p>{when} tarihinde <b>{name}</b> &lt;{email}&gt; yazdı:</p>" +
                   $"<blockquote>{_sanitizer.Sanitize(source.Body)}</blockquote>";
        }

        // İlet: orijinal mesajın künyesi + gövdesi
        private string BuildForwardBlock(Message source)
        {
            string when = (source.SentDate ?? source.UpdatedDate).ToString("d MMMM yyyy dddd HH:mm", Tr);
            string name = WebUtility.HtmlEncode(PersonName(source.Sender));
            string email = WebUtility.HtmlEncode(source.Sender.Email ?? string.Empty);
            string subject = WebUtility.HtmlEncode(source.Subject);
            string to = WebUtility.HtmlEncode(string.Join(", ", source.Recipients.Select(r => r.Email)));

            return "<p><br></p>" +
                   "<p>---------- İletilen mesaj ----------<br>" +
                   $"<b>Kimden:</b> {name} &lt;{email}&gt;<br>" +
                   $"<b>Tarih:</b> {when}<br>" +
                   $"<b>Konu:</b> {subject}<br>" +
                   $"<b>Kime:</b> {to}</p>" +
                   _sanitizer.Sanitize(source.Body);
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
    }
}