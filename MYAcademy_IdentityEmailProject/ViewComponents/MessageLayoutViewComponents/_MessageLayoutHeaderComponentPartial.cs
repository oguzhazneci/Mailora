using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models;

namespace MYAcademy_IdentityEmailProject.ViewComponents.MessageLayoutViewComponents
{
    public class _MessageLayoutHeaderComponentPartial : ViewComponent
    {
        private static readonly CultureInfo Tr = new("tr-TR");

        private readonly EmailContext _context;
        private readonly UserManager<AppUser> _userManager;

        public _MessageLayoutHeaderComponentPartial(EmailContext context, UserManager<AppUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            // Kullanıcı bilgileri çerezdeki claim'lerden: veritabanı sorgusu yok.
            var user = UserClaimsPrincipal;

            string fullName = $"{user.FindFirstValue(ClaimTypes.GivenName)} {user.FindFirstValue(ClaimTypes.Surname)}".Trim();
            if (fullName.Length == 0)
            {
                fullName = user.Identity?.Name ?? "Kullanıcı";
            }

            var model = new NavbarUserViewModel
            {
                FullName = fullName,
                Email = user.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
                Initials = GetInitials(fullName)
            };

            // Bildirimler: son 5 okunmamış gelen mesaj.
            // Projeksiyon: sadece gösterilecek kolonlar okunur, Include gerekmez.
            string userId = _userManager.GetUserId(user)!;

            var unread = await _context.MailboxItems
                .Where(i => i.UserId == userId && i.Folder == MailFolder.Inbox && !i.IsRead && i.DeletedDate == null)
                .OrderByDescending(i => i.Message.SentDate)
                .Take(5)
                .Select(i => new
                {
                    i.MessageId,
                    i.Message.Subject,
                    i.Message.SentDate,
                    SenderName = i.Message.Sender.Name,
                    SenderSurname = i.Message.Sender.Surname,
                    SenderEmail = i.Message.Sender.Email
                })
                .ToListAsync();

            // Biçimlendirme veritabanında değil, burada (C#) yapılır.
            model.Notifications = unread.Select(n => new NotificationItemViewModel
            {
                MessageId = n.MessageId,
                Subject = string.IsNullOrWhiteSpace(n.Subject) ? "(Konu yok)" : n.Subject,
                SenderDisplayName = DisplayName(n.SenderName, n.SenderSurname, n.SenderEmail),
                RelativeTime = n.SentDate.HasValue ? RelativeTime(n.SentDate.Value) : string.Empty
            }).ToList();

            return View(model);
        }

        private static string GetInitials(string fullName)
        {
            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Take(2).Select(p => p[0])).ToUpper(Tr);
        }

        private static string DisplayName(string? name, string? surname, string? email)
        {
            string fullName = $"{name} {surname}".Trim();
            return fullName.Length > 0 ? fullName : (email ?? "Bilinmeyen gönderen");
        }

        private static string RelativeTime(DateTime date)
        {
            var diff = DateTime.Now - date;
            if (diff.TotalMinutes < 1) return "Az önce";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} dakika önce";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} saat önce";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} gün önce";
            return date.ToString("d MMMM yyyy", Tr);
        }
    }
}