using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Helpers;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    // Yeni mesaj bildirimleri (12.5). Global AuthorizeFilter korur.
    public class NotificationsController : Controller
    {
        private const int MaxItems = 5;

        private readonly EmailContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly IMailboxCounter _counter;
        private readonly INotificationFeed _feed;

        public NotificationsController(EmailContext context, UserManager<AppUser> userManager,
                                       IMailboxCounter counter, INotificationFeed feed)
        {
            _context = context;
            _userManager = userManager;
            _counter = counter;
            _feed = feed;
        }

        // GET /Notifications/Check?after=1250
        // "after": tarayıcının en son gördüğü gelen kopya Id'si (imleç, Kavram 2).
        // İstemciden gelse de zararsız: sadece BENİM satırlarım arasında "hangisinden sonra?" sorusunu seçer.
        [HttpGet]
        public async Task<IActionResult> Check(int after)
        {
            string userId = _userManager.GetUserId(User)!;

            // Yeni = imleçten büyük Id'li, Gelen Kutusu'nda DURAN kopyalarım (çöpte ya da arşivde değil)
            var fresh = _context.MailboxItems
                .Where(i => i.UserId == userId
                         && i.Folder == MailFolder.Inbox
                         && i.DeletedDate == null
                         && i.ArchivedDate == null
                         && i.MailboxItemId > after);

            var items = await fresh
                .OrderByDescending(i => i.MailboxItemId)
                .Take(MaxItems)
                .Select(i => new
                {
                    i.MailboxItemId,
                    i.MessageId,
                    i.Message.Subject,
                    i.Message.Sender.Name,
                    i.Message.Sender.Surname,
                    i.Message.Sender.Email,
                    i.Message.Sender.ImageUrl
                })
                .ToListAsync();

            // Toplam yeni sayısı 5'i geçebilir; ama sadece yeni bir şey varsa ikinci sorgu çalışır.
            // Çoğu yoklamada (yeni mesaj yok) toplam sorgu sayısı: bu + sayaç = 2.
            int newCount = items.Count < MaxItems ? items.Count : await fresh.CountAsync();

            var counts = await _counter.GetAsync(userId);

            // Bu yanıt asla önbelleğe alınmamalı: her yoklama sunucuya gerçekten ulaşmalı
            Response.Headers.CacheControl = "no-store";

            return Json(new
            {
                // Yeni imleç: o an kutudaki en büyük gelen Id. Arada çöpe/arşive gidenler de "görüldü" sayılır.
                cursor = Math.Max(after, counts.LatestInboxItemId),
                newCount,
                items = items.Select(i => new
                {
                    i.MailboxItemId,
                    i.MessageId,
                    subject = string.IsNullOrWhiteSpace(i.Subject) ? "(Konu yok)" : i.Subject,
                    senderName = FullName(i.Name, i.Surname, i.Email),
                    senderEmail = i.Email,
                    // Adres sunucuda kurulur; istemci dosya adından URL üretmez
                    senderPhotoUrl = AvatarHelper.IsAvatarFile(i.ImageUrl)
                        ? Url.Action("Avatar", "Profile", new { id = i.ImageUrl })
                        : null,
                    url = Url.Action("Detail", "Message", new { id = i.MessageId })
                }),
                counts
            });
        }

        // GET /Notifications/Menu — zil menüsünün liste kısmı, HTML olarak (12.6).
        // Navbar'ın ilk çizimiyle AYNI partial: iki çizim hiç farklılaşmaz.
        [HttpGet]
        public async Task<IActionResult> Menu()
        {
            string userId = _userManager.GetUserId(User)!;
            Response.Headers.CacheControl = "no-store";
            return PartialView("_NotificationList", await _feed.GetUnreadAsync(userId));
        }

        private static string FullName(string? name, string? surname, string? email)
        {
            string full = $"{name} {surname}".Trim();
            return full.Length > 0 ? full : email ?? "Bilinmeyen";
        }
    }
}