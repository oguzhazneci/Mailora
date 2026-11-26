using Ganss.Xss;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;
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
        private readonly IAttachmentStorage _attachmentStorage;
        private readonly ISendRateLimiter _sendLimiter;   // 17.4
        private readonly ITrashService _trash;   // 15.3: kalıcı silme (arka plan servisiyle ortak)

        public MessageController(EmailContext context, UserManager<AppUser> userManager,
                                 IHtmlSanitizer sanitizer, IMailboxCounter counter,
                                 IMailService mailService, ILogger<MessageController> logger,
                                 IAttachmentStorage attachmentStorage, ITrashService trash, ISendRateLimiter sendLimiter)
        {
            _context = context;
            _userManager = userManager;
            _sanitizer = sanitizer;
            _counter = counter;
            _mailService = mailService;
            _logger = logger;
            _attachmentStorage = attachmentStorage;
            _trash = trash;
            _sendLimiter = sendLimiter;
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

        public Task<IActionResult> Archive(MailboxQuery query) => ListAsync(MailboxView.Archive, query);

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

            // Hangi satırların eki var? Sayfa başına TEK sorgu (12.17, Kavram 1). Sadece MessageId'ler gelir.
            var pageMessageIds = pageItems.Select(i => i.MessageId).Distinct().ToList();
            var withAttachments = (await _context.Attachments
                .Where(a => pageMessageIds.Contains(a.MessageId))          // SQL: WHERE MessageId IN (...)
                .Select(a => a.MessageId)
                .Distinct()
                .ToListAsync()).ToHashSet();

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
                UserPhotos = userPhotos,
                MessagesWithAttachments = withAttachments
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
                MailboxView.Inbox => scoped.Where(i => i.Folder == MailFolder.Inbox && i.DeletedDate == null && i.ArchivedDate == null),
                MailboxView.Sent => scoped.Where(i => i.Folder == MailFolder.Sent && i.DeletedDate == null),
                // Klasör değil özellik: karışık liste. Koşul sidebar sayacıyla AYNI (9.4, Kavram 2).
                MailboxView.Starred => scoped.Where(i => i.IsStarred && i.DeletedDate == null),
                // Çöp Kutusu bir klasör değil bir durum: her klasörden silinenler (9.1)
                MailboxView.Trash => scoped.Where(i => i.DeletedDate != null),
                // Arşiv: gelen + arşivlenmiş + çöpte değil. Folder hâlâ Inbox (12.1, Kavram 2)
                MailboxView.Archive => scoped.Where(i => i.Folder == MailFolder.Inbox && i.ArchivedDate != null && i.DeletedDate == null),
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
                SafeBodyHtml = _sanitizer.Sanitize(item.Message.Body),
                Attachments = await LoadAttachmentsAsync(item.MessageId)   // 12.17 (12.16.b'deki yardımcı)
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
        // Arşivle (12.2): sadece gelen, arşivde olmayan, çöpte olmayan kopyalarım (karar 2-A, Kavram 2)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkArchive(int[] ids)
        {
            if (ids.Length == 0 || ids.Length > BulkLimit)
            {
                return BadRequest();
            }

            string userId = _userManager.GetUserId(User)!;
            var now = DateTime.Now;

            int count = await _context.MailboxItems
                .Where(i => ids.Contains(i.MailboxItemId)
                         && i.UserId == userId
                         && i.Folder == MailFolder.Inbox
                         && i.ArchivedDate == null
                         && i.DeletedDate == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ArchivedDate, now));

            return Json(new { count, counts = await _counter.GetAsync(userId) });
        }

        // Gelen Kutusu'na taşı (12.2)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkUnarchive(int[] ids)
        {
            if (ids.Length == 0 || ids.Length > BulkLimit)
            {
                return BadRequest();
            }

            string userId = _userManager.GetUserId(User)!;

            int count = await _context.MailboxItems
                .Where(i => ids.Contains(i.MailboxItemId)
                         && i.UserId == userId
                         && i.ArchivedDate != null
                         && i.DeletedDate == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ArchivedDate, (DateTime?)null));

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

            // "Çöpte mi?" koşulunu servis kendisi ekler (15.3): burada sadece hangi kopyalar ve kimin
            int count = await _trash.DeleteForeverAsync(i => ids.Contains(i.MailboxItemId) && i.UserId == userId);

            return Json(new { count, counts = await _counter.GetAsync(userId) });
        }

        // Çöpü boşalt: normal form POST → Redirect → GET (Kavram 5, PRG)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EmptyTrash()
        {
            string userId = _userManager.GetUserId(User)!;

            int count = await _trash.DeleteForeverAsync(i => i.UserId == userId);

            TempData["Toast"] = count > 0
                ? $"Çöp kutusu boşaltıldı · {count} mesaj kalıcı olarak silindi."
                : "Çöp kutusu zaten boş.";
            TempData["ToastIcon"] = "bi-trash3-fill";

            return RedirectToAction(nameof(Trash));
        }

        // Kalıcı silmenin ortak kısmı 15.3'te TrashService'e taşındı (arka plan servisi de kullanıyor).

        /* =====================================================================
                   YENİ MESAJ ve TASLAKLAR (8.2 – 8.4, 9.6, 9.7, 12.9)
                   Tek form, iki niyet: "send" → tam doğrulama + gönderim, "draft" → gevşek doğrulama + kayıt.
                   Alıcılar: Kime / Bilgi / Gizli metinleri → RecipientList (ayrıştır + tekilleştir).
                   ===================================================================== */

        [HttpGet]
        public async Task<IActionResult> CreateMessage(int? draftId, int? replyTo, int? forwardOf, string? to, bool replyAll = false)
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
                // Taslaktaki alıcı satırları türlerine göre üç alana dağıtılır (12.9)
                model.To = RecipientList.Join(draft.Recipients.Where(r => r.RecipientType == RecipientType.To).Select(r => r.Email));
                model.Cc = RecipientList.Join(draft.Recipients.Where(r => r.RecipientType == RecipientType.Cc).Select(r => r.Email));
                model.Bcc = RecipientList.Join(draft.Recipients.Where(r => r.RecipientType == RecipientType.Bcc).Select(r => r.Email));
                model.CategoryId = draft.CategoryId;
                model.Subject = draft.Subject;
                model.Body = _sanitizer.Sanitize(draft.Body);   // çıktıda da temizle (8.2, derinlemesine savunma)
                model.Attachments = await LoadAttachmentsAsync(draft.MessageId);   // 12.16
            }
            else if (replyTo.HasValue || forwardOf.HasValue)
            {
                // Yanıtla / Tümünü Yanıtla / İlet = önceden doldurulmuş yeni mesaj (9.7, 12.12)
                bool isReply = replyTo.HasValue;
                int sourceId = replyTo ?? forwardOf!.Value;

                var mine = await FindMyCopyAsync(sourceId);
                if (mine == null)
                {
                    bool exists = await _context.Messages.AnyAsync(m => m.MessageId == sourceId);
                    return exists ? Forbid() : NotFound();
                }

                var source = mine.Message;
                if (source.SentDate == null)
                {
                    return NotFound();
                }

                model.CategoryId = source.CategoryId;
                model.Subject = WithPrefix(isReply ? "Re:" : "Fwd:", source.Subject);

                string heading = "İlet";
                if (isReply)
                {
                    // 12.12: hedefler tek kuraldan. Gizli liste asla girmez, bakan kişi çıkarılır,
                    // Gizli alıcıda replyAll yok sayılır (karar 4-A). mine = benim kopyam → mine.UserId = bakan kişi.
                    var rv = RecipientView.For(source, mine.UserId);
                    var targets = rv.GetReplyTargets(source.Sender.Email, replyAll);

                    model.To = RecipientList.Join(targets.To);
                    model.Cc = RecipientList.Join(targets.Cc);
                    heading = replyAll && !rv.ViewerIsBcc ? "Tümünü Yanıtla" : "Yanıtla";
                }

                model.Body = isReply ? BuildReplyQuote(source) : BuildForwardBlock(source);
                // İlet (karar 5-A): kaynağın ekleri LİSTELENİR, henüz kopyalanmaz. GET yan etkisiz olmalı (Kavram 1).
                if (!isReply)
                {
                    model.ForwardOf = source.MessageId;
                    model.Attachments = (await LoadAttachmentsAsync(source.MessageId))
                        .Select(a => a with { Forwarded = true })
                        .ToList();
                    model.ForwardAttachmentIds = model.Attachments.Select(a => a.Id).ToList();
                }
                ViewData["ComposeHeading"] = heading;
            }
            else if (!string.IsNullOrWhiteSpace(to) && to.Length <= RecipientList.MaxAddressLength)
            {
                // Genel Bakış'taki "yaz" butonu (11.3): sadece başlangıç değeri, doğrulama gönderimde
                model.To = to.Trim();
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

            // Üç alan BİRLİKTE ayrıştırılır: toplam sayı ve alanlar arası tekrar ancak böyle görülür
            var recipients = RecipientList.Parse(model.To, model.Cc, model.Bcc);

            // DraftId istemciden gelir: benim mi, hâlâ taslak mı? (9.6, Kavram 4)
            // 12.16: doğrulamadan ÖNCE bulunur; taslağın ekleri doğrulamayı etkiler
            Message? draft = null;
            if (model.DraftId.HasValue)
            {
                draft = await FindMyDraftAsync(model.DraftId.Value);
                if (draft == null)
                {
                    return DraftGone();
                }
            }

            // Taslaktaki ekler + İlet ile gelecek ekler (12.18). Sınırlar ikisinin TOPLAMINA uygulanır (Kavram 4).
            long[] draftSizes = draft == null
                ? []
                : await _context.Attachments.Where(a => a.MessageId == draft.MessageId).Select(a => a.SizeBytes).ToArrayAsync();
            var forwarded = await FindForwardAttachmentsAsync(model.ForwardOf, model.ForwardAttachmentIds);
            bool hasAttachments = draftSizes.Length > 0 || forwarded.Count > 0;

            if (isDraft)
            {
                // Taslak eksik olabilir: [Required] hatalarını at, kendi kurallarımızı uygula (9.6)
                ModelState.Clear();
                ValidateDraft(model, safeBody, recipients, hasAttachments);
            }
            else
            {
                ValidateRecipients(recipients);
                if (!string.IsNullOrEmpty(model.Body) && !HasVisibleText(safeBody))
                {
                    ModelState.AddModelError(nameof(model.Body), "Mesaj içeriği boş olamaz.");
                }
            }

            if (draftSizes.Length + forwarded.Count > AttachmentRules.MaxFiles)
            {
                ModelState.AddModelError(string.Empty, $"Bir mesaja en fazla {AttachmentRules.MaxFiles} dosya eklenebilir.");
            }
            else if (draftSizes.Sum() + forwarded.Sum(a => a.SizeBytes) > AttachmentRules.MaxTotalBytes)
            {
                ModelState.AddModelError(string.Empty, $"Eklerin toplam boyutu en fazla {AttachmentRules.MaxTotalBytes / (1024 * 1024)} MB olabilir.");
            }

            if (model.CategoryId.HasValue && !await _context.Categories.AnyAsync(c => c.CategoryId == model.CategoryId))
            {
                ModelState.AddModelError(nameof(model.CategoryId), "Seçilen kategori bulunamadı.");
            }

            // 17.4 (karar 7-B): Gönderme sınırı. Sadece diğer kontrollerden geçen istek hak harcar (Kavram 3).
            // Reddedilirse aşağıdaki "geçersiz" yol formu alıcılar, gövde ve eklerle geri doldurur: mesaj kaybolmaz.
            if (ModelState.IsValid)
            {
                string? limitError = await _sendLimiter.TryAcquireAsync(_userManager.GetUserId(User)!, HttpContext.RequestAborted);
                if (limitError != null)
                {
                    ModelState.AddModelError(string.Empty, limitError);
                    Response.StatusCode = StatusCodes.Status429TooManyRequests;   // gövde form, kod 429 (Kavram 4)
                }
            }

            if (!ModelState.IsValid)
            {
                // Formu TEKİLLEŞTİRİLMİŞ haliyle geri doldur: kullanıcı neyin gönderileceğini görsün
                model.To = RecipientList.Join(recipients.To);
                model.Cc = RecipientList.Join(recipients.Cc);
                model.Bcc = RecipientList.Join(recipients.Bcc);
                model.Body = safeBody;

                // Ekler doğrulama hatasında kaybolmaz: taslaktakiler + hâlâ seçili iletilecekler (12.16, 12.18)
                model.Attachments = draft == null ? new() : await LoadAttachmentsAsync(draft.MessageId);
                model.Attachments.AddRange(forwarded.Select(a => new AttachmentItem(a.AttachmentId, a.FileName, a.SizeBytes, true)));
                model.ForwardAttachmentIds = forwarded.Select(a => a.AttachmentId).ToList();   // sadece DOĞRULANANLAR geri yazılır

                await LoadCategoriesAsync();
                return View(model);
            }

            // İletilecek ekler bir taslağa kopyalanır; sonrası değişmeden Save/Send akışı (Kavram 3)
            if (forwarded.Count > 0)
            {
                draft ??= NewDraft(_userManager.GetUserId(User)!, DateTime.Now);
                CopyForwardedInto(draft, forwarded);
            }

            return isDraft
                ? await SaveDraftAsync(model, safeBody, draft, recipients)
                : await SendMessageAsync(model, safeBody, draft, recipients);
        }

        // Taslağı kaydet: yeni ya da mevcut taslağı güncelle
        private async Task<IActionResult> SaveDraftAsync(CreateMessageViewModel model, string safeBody, Message? draft, RecipientList recipients)
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

            // Taslakta alıcılar sadece yazılan adreslerdir (türleriyle). Kayıtlı kullanıcıyla eşleştirme GÖNDERİMDE (9.6).
            draft.Recipients.Clear();
            foreach (var (email, type) in recipients.All)
            {
                draft.Recipients.Add(new MessageRecipient { Email = email, UserId = null, RecipientType = type });
            }

            await _context.SaveChangesAsync();

            TempData["Toast"] = "Taslak kaydedildi.";
            TempData["ToastIcon"] = "bi-file-earmark-check";

            // PRG: düzenleme adresine dön. Sonraki "Kaydet" AYNI taslağı günceller (9.6).
            return RedirectToAction(nameof(CreateMessage), new { draftId = draft.MessageId });
        }

        // Gönder: yeni mesaj ya da taslaktan gönderim (karar 4-A: aynı satır) — çok alıcılı (12.10)
        private async Task<IActionResult> SendMessageAsync(CreateMessageViewModel model, string safeBody, Message? draft, RecipientList recipients)
        {
            string senderId = _userManager.GetUserId(User)!;
            var now = DateTime.Now;

            // 1) Kayıtlı alıcılar TEK sorguda (Kavram 1). FindByEmailAsync döngüsü 20 sorgu olurdu (N+1).
            //    Identity e-postaları NormalizedEmail sütununda büyük harfle tutar; aynı normalleştirmeyle ararız.
            var normalized = recipients.All.Select(r => _userManager.NormalizeEmail(r.Email)).ToList();
            var registered = await _userManager.Users
                .Where(u => normalized.Contains(u.NormalizedEmail!))          // SQL: WHERE NormalizedEmail IN (...)
                .Select(u => new { u.Id, u.Email, u.NormalizedEmail, u.Name, u.Surname })
                .ToDictionaryAsync(u => u.NormalizedEmail!);

            Message message;
            if (draft == null)
            {
                message = new Message { SenderId = senderId, CreatedDate = now };
                message.MailboxItems.Add(new MailboxItem { UserId = senderId, Folder = MailFolder.Sent, IsRead = true });
                _context.Messages.Add(message);
            }
            else
            {
                // Taslak → gönderilmiş mesaj: Id aynı, gönderenin kopyası Draft'tan Sent'e geçer (9.6)
                message = draft;
                message.MailboxItems.Single(i => i.UserId == senderId && i.Folder == MailFolder.Draft).Folder = MailFolder.Sent;
                message.Recipients.Clear();
            }

            message.Subject = model.Subject.Trim();
            message.Body = safeBody;
            message.CategoryId = model.CategoryId;
            message.UpdatedDate = now;
            message.SentDate = now;

            // 2) Her alıcı için bir alıcı satırı (türüyle); her KAYITLI alıcı için bir Gelen kopyası.
            //    RecipientList zaten tekilleştirdi (12.9): aynı kullanıcıya iki kopya oluşamaz (benzersiz indeks, 5.3).
            var external = new List<string>();
            var internalNames = new List<string>();

            foreach (var (email, type) in recipients.All)
            {
                if (registered.TryGetValue(_userManager.NormalizeEmail(email), out var user))
                {
                    message.Recipients.Add(new MessageRecipient { Email = user.Email!, UserId = user.Id, RecipientType = type });
                    message.MailboxItems.Add(new MailboxItem { UserId = user.Id, Folder = MailFolder.Inbox, IsRead = false });

                    string fullName = $"{user.Name} {user.Surname}".Trim();
                    internalNames.Add(fullName.Length > 0 ? fullName : user.Email!);
                }
                else
                {
                    message.Recipients.Add(new MessageRecipient { Email = email, UserId = null, RecipientType = type });
                    external.Add(email);
                }
            }

            // Hepsi tek SaveChanges = tek transaction (5.4)
            await _context.SaveChangesAsync();

            // 3) Dış alıcılar: kayıttan SONRA, tek SMTP mailiyle (8.1: geri alınamaz işlem en sonda; karar 5-A)
            bool externalOk = external.Count == 0 || await TrySendExternalAsync(message, recipients, external);

            // 4) Toast: kime gittiğini açıkça söyler (8.4). Tek alıcıda eski, ayrıntılı metin.
            if (recipients.Count == 1)
            {
                TempData["Toast"] = internalNames.Count == 1
                    ? $"Mesajınız gönderildi · Alıcı: {internalNames[0]} (Mailora)"
                    : externalOk
                        ? $"Mesajınız e-posta olarak gönderildi · Alıcı: {external[0]} (Mailora dışı)"
                        : $"Mesajınız kaydedildi ancak {external[0]} adresine iletilemedi.";
            }
            else
            {
                var parts = new List<string>();
                if (internalNames.Count > 0) parts.Add($"{internalNames.Count} Mailora");
                if (external.Count > 0) parts.Add($"{external.Count} e-posta");

                TempData["Toast"] = externalOk
                    ? $"Mesajınız {recipients.Count} alıcıya gönderildi ({string.Join(", ", parts)})."
                    : $"Mesajınız Mailora alıcılarına iletildi ancak {external.Count} dış adrese gönderilemedi.";
            }

            if (!externalOk)
            {
                TempData["ToastIcon"] = "bi-exclamation-triangle";
            }
            else if (external.Count > 0)
            {
                TempData["ToastIcon"] = "bi-envelope-arrow-up";
            }

            // Post-Redirect-Get: gönderilen mesajın detayı
            return RedirectToAction(nameof(Detail), new { id = message.MessageId });
        }

        /* =====================================================================
   EKLER: YÜKLEME ve KALDIRMA (12.16)
   Dosya seçildiği anda yüklenir ve bir TASLAĞA bağlanır (karar 2-A).
   Taslak yoksa boş bir taslak oluşturulur; istemci dönen draftId'yi formdaki gizli alana yazar.
   ===================================================================== */

        [HttpPost]
        [ValidateAntiForgeryToken]
        // Büyük istek controller'a gelmeden reddedilir (413). Pay: form alanları ve multipart sınırları için 1 MB.
        [RequestSizeLimit(AttachmentRules.MaxFileBytes + 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = AttachmentRules.MaxFileBytes + 1024 * 1024)]
        [EnableRateLimiting(RateLimitPolicies.UserUpload)]   // 17.4: kullanıcı başına; reddedilirse JSON { error }
        public async Task<IActionResult> UploadAttachment(int? draftId, IFormFile? file)
        {
            string userId = _userManager.GetUserId(User)!;
            var now = DateTime.Now;

            // 1) Taslak: varsa SAHİPLİĞİ yeniden doğrulanır (formdaki draftId de istemci girdisidir, 9.6)
            Message draft;
            long[] existingSizes;
            if (draftId.HasValue)
            {
                var found = await FindMyDraftAsync(draftId.Value);
                if (found == null)
                {
                    return AttachmentError("Bu taslak artık düzenlenemiyor: gönderilmiş ya da silinmiş olabilir.",
                                           StatusCodes.Status404NotFound);
                }
                draft = found;

                // En fazla 10 satır: boyutları çekip sayıyı ve toplamı bellekte hesaplamak tek sorgu
                existingSizes = await _context.Attachments
                    .Where(a => a.MessageId == draft.MessageId)
                    .Select(a => a.SizeBytes)
                    .ToArrayAsync();
            }
            else
            {
                draft = new Message { SenderId = userId, CreatedDate = now, SentDate = null };   // boş taslak (Kavram 1)
                draft.MailboxItems.Add(new MailboxItem { UserId = userId, Folder = MailFolder.Draft, IsRead = true });
                _context.Messages.Add(draft);
                existingSizes = [];
            }

            // 2) Mesaj başı sınırlar: mevcut eklere bakmayı gerektirdiği için servis değil controller kontrol eder
            if (existingSizes.Length >= AttachmentRules.MaxFiles)
            {
                return AttachmentError($"Bir mesaja en fazla {AttachmentRules.MaxFiles} dosya eklenebilir.");
            }
            if (file != null && existingSizes.Sum() + file.Length > AttachmentRules.MaxTotalBytes)
            {
                return AttachmentError($"Eklerin toplam boyutu en fazla {AttachmentRules.MaxTotalBytes / (1024 * 1024)} MB olabilir.");
            }

            // 3) Dosya kuralları ve diske yazma (boyut, izin listesi, imza → 12.15.c)
            var result = await _attachmentStorage.SaveAsync(file, now, HttpContext.RequestAborted);
            if (!result.Succeeded)
            {
                return AttachmentError(result.Error!);
            }

            // 4) Sıralama (Kavram 4): dosya yazıldı → veritabanı. Kayıt başarısızsa dosya geri alınır.
            var attachment = result.Attachment!;
            draft.Attachments.Add(attachment);
            draft.UpdatedDate = now;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                _attachmentStorage.Delete(attachment.StoredName);
                throw;
            }

            return Json(new
            {
                draftId = draft.MessageId,
                attachment = new
                {
                    id = attachment.AttachmentId,
                    name = attachment.FileName,
                    size = attachment.SizeBytes
                }
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveAttachment(int id)
        {
            string userId = _userManager.GetUserId(User)!;

            // Sadece KENDİ, hâlâ düzenlenebilir TASLAĞIMIN eki kaldırılabilir. Gönderilmiş mesajın eki değişmez.
            // Koşullar FindMyDraftAsync ile aynı dört şart: benim, gönderilmemiş, Taslak klasöründe, çöpte değil.
            var attachment = await _context.Attachments.FirstOrDefaultAsync(a =>
                a.AttachmentId == id &&
                a.Message.SenderId == userId &&
                a.Message.SentDate == null &&
                a.Message.MailboxItems.Any(i => i.UserId == userId && i.Folder == MailFolder.Draft && i.DeletedDate == null));

            if (attachment == null)
            {
                return AttachmentError("Ek bulunamadı ya da artık kaldırılamıyor.", StatusCodes.Status404NotFound);
            }

            // Sıralama (Kavram 4): önce satır, sonra dosya
            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();
            _attachmentStorage.Delete(attachment.StoredName);

            return Json(new { ok = true });
        }

        // Eki indir (12.17). Adres: /Message/DownloadAttachment/{id}
        // Yetki: bu mesajın bende bir kopyası var mı? (Kavram 1, IDOR'a karşı). Çöp ve arşiv dahil.
        [HttpGet]
        public async Task<IActionResult> DownloadAttachment(int id)
        {
            string userId = _userManager.GetUserId(User)!;

            // Sahiplik koşulu SORGUNUN İÇİNDE. Sadece gereken üç alan çekilir.
            var attachment = await _context.Attachments
                .Where(a => a.AttachmentId == id && a.Message.MailboxItems.Any(i => i.UserId == userId))
                .Select(a => new { a.AttachmentId, a.FileName, a.StoredName, a.ContentType })
                .FirstOrDefaultAsync();

            // Yok ya da benim değil: ikisi de 404. 403 "var ama senin değil" bilgisini sızdırırdı (Kavram 2).
            if (attachment == null)
            {
                return NotFound();
            }

            // Satır var ama dosya yok: disk ile veritabanı tutarsız. Kullanıcıya 404, bize log.
            string? path = _attachmentStorage.GetPath(attachment.StoredName);
            if (path == null)
            {
                _logger.LogWarning("Ek dosyası diskte bulunamadı. AttachmentId: {AttachmentId}", attachment.AttachmentId);
                return NotFound();
            }

            // Tarayıcıda asla "sayfa" olarak açılmasın (Kavram 3) ve diskte önbelleğe alınmasın (Kavram 4)
            Response.Headers.XContentTypeOptions = "nosniff";
            Response.Headers.ContentSecurityPolicy = "sandbox";
            Response.Headers.CacheControl = "private, no-store";

            // fileDownloadName → Content-Disposition: attachment; filename="..."; filename*=UTF-8''...
            return PhysicalFile(path, attachment.ContentType, attachment.FileName);
        }

        // JSON hata yanıtı: { "error": "..." }. Mesajlar kullanıcı girdisi içermez (12.15.c).
        private IActionResult AttachmentError(string message, int status = StatusCodes.Status400BadRequest)
            => StatusCode(status, new { error = message });

        // Boş taslak (12.16, 12.18): yükleme ve iletme aynı şekilde oluşturur. SaveChanges çağıran kaydeder.
        private Message NewDraft(string userId, DateTime now)
        {
            var draft = new Message { SenderId = userId, CreatedDate = now, SentDate = null };   // SentDate null = taslak (5.2)
            draft.MailboxItems.Add(new MailboxItem { UserId = userId, Folder = MailFolder.Draft, IsRead = true });
            _context.Messages.Add(draft);
            return draft;
        }

        // İletilecek ekler (12.18). İKİ şart birlikte (Kavram 2): ek gerçekten o mesajın eki mi,
        // ve o mesajın bende bir kopyası var mı? Uymayan Id'ler sessizce düşer.
        private async Task<List<Attachment>> FindForwardAttachmentsAsync(int? forwardOf, List<int> ids)
        {
            if (!forwardOf.HasValue || ids.Count == 0)
            {
                return new();
            }

            string userId = _userManager.GetUserId(User)!;
            var wanted = ids.Distinct().Take(AttachmentRules.MaxFiles).ToList();   // istemci 10.000 Id gönderse bile

            return await _context.Attachments
                .AsNoTracking()                                                      // sadece okunur, kaynak satırlar değişmez
                .Where(a => a.MessageId == forwardOf.Value
                         && wanted.Contains(a.AttachmentId)
                         && a.Message.SentDate != null
                         && a.Message.MailboxItems.Any(i => i.UserId == userId))
                .OrderBy(a => a.AttachmentId)
                .ToListAsync();
        }

        // Kopyala (karar 5-A): her ek YENİ bir dosya ve YENİ bir satır. Kaynak mesaj silinse de iletilen mesaj etkilenmez.
        // SaveChanges başarısız olursa kopyalar diskte sahipsiz kalır → 12.19'daki temizlik.
        private void CopyForwardedInto(Message draft, List<Attachment> forwarded)
        {
            var now = DateTime.Now;
            foreach (var source in forwarded)
            {
                string? copy = _attachmentStorage.Copy(source.StoredName);
                if (copy == null)
                {
                    // Satır var, kaynak dosya yok: o eki atla, gönderimi engelleme
                    _logger.LogWarning("İletilecek ek dosyası bulunamadı. AttachmentId: {AttachmentId}", source.AttachmentId);
                    continue;
                }

                draft.Attachments.Add(new Attachment
                {
                    FileName = source.FileName,
                    StoredName = copy,                 // YENİ ad: unique index iki satırın aynı dosyayı göstermesine izin vermez (12.15.b)
                    ContentType = source.ContentType,
                    SizeBytes = source.SizeBytes,
                    CreatedDate = now
                });
            }
        }

        // Taslağın ekleri, formda listelenmek üzere (12.16). Sadece gösterim bilgileri; StoredName forma çıkmaz.
        private Task<List<AttachmentItem>> LoadAttachmentsAsync(int messageId) =>
            _context.Attachments
                .Where(a => a.MessageId == messageId)
                .OrderBy(a => a.AttachmentId)                 // yükleme sırası
                .Select(a => new AttachmentItem(a.AttachmentId, a.FileName, a.SizeBytes, false))
                .ToListAsync();

        // Benim, hâlâ taslak ve çöpte değil. Değilse null (gönderilmiş, silinmiş ya da başkasının).
        private Task<Message?> FindMyDraftAsync(int messageId)
        {
            string userId = _userManager.GetUserId(User)!;

            return _context.Messages
                .Include(m => m.Recipients)
                .Include(m => m.MailboxItems.Where(i => i.UserId == userId))
                .FirstOrDefaultAsync(m =>
                    m.MessageId == messageId &&
                    m.SenderId == userId &&
                    m.SentDate == null &&
                    m.MailboxItems.Any(i => i.UserId == userId && i.Folder == MailFolder.Draft && i.DeletedDate == null));
        }

        private IActionResult DraftGone()
        {
            TempData["Toast"] = "Bu taslak artık düzenlenemiyor: gönderilmiş ya da silinmiş olabilir.";
            TempData["ToastIcon"] = "bi-info-circle";
            return RedirectToAction(nameof(Drafts));
        }

        // GÖNDERİM kuralları (12.9): en az 1, en fazla 20 alıcı; her adres geçerli.
        // Hata, sorunun olduğu ALANA yazılır; geçersiz adresler tek tek listelenir.
        private void ValidateRecipients(RecipientList recipients)
        {
            if (recipients.Count == 0)
            {
                ModelState.AddModelError(nameof(CreateMessageViewModel.To), "En az bir alıcı girin.");
                return;
            }

            if (recipients.Count > RecipientList.MaxRecipients)
            {
                ModelState.AddModelError(nameof(CreateMessageViewModel.To),
                    $"Bir mesaj en fazla {RecipientList.MaxRecipients} alıcıya gönderilebilir (şu an {recipients.Count}).");
            }

            AddInvalid(nameof(CreateMessageViewModel.To), recipients.To);
            AddInvalid(nameof(CreateMessageViewModel.Cc), recipients.Cc);
            AddInvalid(nameof(CreateMessageViewModel.Bcc), recipients.Bcc);

            void AddInvalid(string field, List<string> addresses)
            {
                var invalid = addresses.Where(a => !RecipientList.IsValidAddress(a)).ToList();
                if (invalid.Count > 0)
                {
                    string shown = string.Join(", ", invalid.Take(3)) + (invalid.Count > 3 ? $" (+{invalid.Count - 3})" : "");
                    ModelState.AddModelError(field, $"Geçersiz adres: {shown}");
                }
            }
        }

        // TASLAK kuralları: eksik olabilir ama boş olamaz ve sınırları aşamaz (9.6, 12.9)
        private void ValidateDraft(CreateMessageViewModel model, string safeBody, RecipientList recipients, bool hasAttachments)
        {
            string subject = model.Subject?.Trim() ?? string.Empty;

            // 12.16: sadece eki olan taslak da "boş" değildir (Kavram 2)
            bool hasAnything = hasAttachments || recipients.Count > 0 || model.CategoryId.HasValue || subject.Length > 0 || HasVisibleText(safeBody);
            if (!hasAnything)
            {
                ModelState.AddModelError(string.Empty, "Boş bir taslak kaydedilemez.");
            }

            if (recipients.Count > RecipientList.MaxRecipients)
            {
                ModelState.AddModelError(nameof(model.To), $"En fazla {RecipientList.MaxRecipients} alıcı yazılabilir.");
            }

            // Taslakta format serbest (yarım yazılmış adres olabilir), ama veritabanı sütun sınırı geçerli
            if (recipients.All.Any(r => r.Email.Length > RecipientList.MaxAddressLength))
            {
                ModelState.AddModelError(nameof(model.To), "E-posta adresi en fazla 256 karakter olabilir.");
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
        // 12.11: künyeyi İLETİLEN KİŞİLER okur → dışarıdan bakan görünümü (Gizli yok, "siz" yok)
        private string BuildForwardBlock(Message source)
        {
            string when = (source.SentDate ?? source.UpdatedDate).ToString("d MMMM yyyy dddd HH:mm", Tr);
            string name = WebUtility.HtmlEncode(PersonName(source.Sender));
            string email = WebUtility.HtmlEncode(source.Sender.Email ?? string.Empty);
            string subject = WebUtility.HtmlEncode(source.Subject);

            var rv = RecipientView.ForOutsider(source);
            static string Emails(IEnumerable<VisibleRecipient> list) =>
                WebUtility.HtmlEncode(string.Join(", ", list.Select(r => r.Email)));

            var lines = new List<string>
            {
                $"<b>Kimden:</b> {name} &lt;{email}&gt;",
                $"<b>Tarih:</b> {when}",
                $"<b>Konu:</b> {subject}"
            };
            if (rv.To.Count > 0) lines.Add($"<b>Kime:</b> {Emails(rv.To)}");
            if (rv.Cc.Count > 0) lines.Add($"<b>Bilgi:</b> {Emails(rv.Cc)}");
            if (rv.To.Count == 0 && rv.Cc.Count == 0) lines.Add("<b>Kime:</b> açıklanmayan alıcılar");

            return "<p><br></p>" +
                   "<p>---------- İletilen mesaj ----------<br>" +
                   string.Join("<br>", lines) + "</p>" +
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
        // Dış alıcılara TEK SMTP maili (12.10). Başlıkta Kime/Bilgi listeleri TAM, Gizli YOK; zarfta sadece dış adresler.
        // Başarısızlık kaydı geri almaz; sadece false döner (8.1).
        private async Task<bool> TrySendExternalAsync(Message message, RecipientList recipients, List<string> externalEnvelope)
        {
            string senderName = SenderDisplayName();
            string? senderEmail = User.FindFirstValue(ClaimTypes.Email);

            // Ekler (12.18): yüklenenler ve İlet ile kopyalananlar artık veritabanında, mesajın Id'siyle.
            // Satırı olup dosyası olmayan ek atlanır ve loglanır (12.17'deki indirmeyle aynı tutum).
            var attachments = new List<MailAttachment>();
            var rows = await _context.Attachments
                .Where(a => a.MessageId == message.MessageId)
                .OrderBy(a => a.AttachmentId)
                .Select(a => new { a.AttachmentId, a.FileName, a.ContentType, a.StoredName })
                .ToListAsync();

            foreach (var row in rows)
            {
                string? path = _attachmentStorage.GetPath(row.StoredName);
                if (path == null)
                {
                    _logger.LogWarning("SMTP eki diskte bulunamadı. AttachmentId: {AttachmentId}", row.AttachmentId);
                    continue;
                }
                attachments.Add(new MailAttachment(row.FileName, row.ContentType, path));
            }

            string html = message.Body +
                "<hr style=\"border:none;border-top:1px solid #ddd;margin:24px 0 12px\">" +
                "<p style=\"color:#888;font-size:12px\">Bu e-posta <b>" + WebUtility.HtmlEncode(senderName) +
                "</b> tarafından Mailora üzerinden gönderildi. Yanıtınız doğrudan göndericiye iletilir.</p>";

            try
            {
                await _mailService.SendToManyAsync(
                    headerTo: recipients.To,
                    headerCc: recipients.Cc,
                    envelopeRecipients: externalEnvelope,
                    subject: message.Subject,
                    body: html,
                    isHtml: true,
                    fromName: $"{senderName} (Mailora)",
                    replyToEmail: senderEmail,
                    attachments: attachments);
                return true;
            }
            catch (Exception ex)
            {
                // Loga kişisel veri (alıcı adresleri) değil, sadece Id ve sayı (8.4)
                _logger.LogError(ex, "Dış adreslere gönderim başarısız. MessageId: {MessageId}, Alıcı sayısı: {Count}",
                                 message.MessageId, externalEnvelope.Count);
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