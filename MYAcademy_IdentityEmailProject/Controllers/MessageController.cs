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

        public MessageController(EmailContext context, UserManager<AppUser> userManager,
                                 IHtmlSanitizer sanitizer, IMailboxCounter counter)
        {
            _context = context;
            _userManager = userManager;
            _sanitizer = sanitizer;
            _counter = counter;
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
           YENİ MESAJ ve GÖNDERİLENLER — KÖPRÜ (5.6). 8. ve 9. aşamalarda yazılacak.
           ===================================================================== */

        public IActionResult CreateMessage()
        {
            ViewBag.Categories = new SelectList(_context.Categories.ToList(), "CategoryId", "CategoryName");
            return View();
        }

        [HttpPost]
        public IActionResult CreateMessage(CreateMessageViewModel createMessageViewModel)
        {
            return RedirectToAction("Sendbox");
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