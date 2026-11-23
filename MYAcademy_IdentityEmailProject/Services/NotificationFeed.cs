using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models.Layout;

namespace MYAcademy_IdentityEmailProject.Services
{
    public class NotificationFeed : INotificationFeed
    {
        private static readonly CultureInfo Tr = new("tr-TR");
        private readonly EmailContext _context;

        public NotificationFeed(EmailContext context)
        {
            _context = context;
        }

        // Son N okunmamış gelen mesaj. Koşul sidebar rozetiyle AYNI (MailboxCounter.UnreadInbox):
        // zildeki nokta ile rozet aynı soruya cevap verir.
        public async Task<List<NotificationItemViewModel>> GetUnreadAsync(string userId, int take = 5)
        {
            // Projeksiyon: sadece gösterilecek kolonlar okunur, Include gerekmez.
            var unread = await _context.MailboxItems
                .Where(i => i.UserId == userId
                         && i.Folder == MailFolder.Inbox
                         && !i.IsRead
                         && i.DeletedDate == null
                         && i.ArchivedDate == null)
                .OrderByDescending(i => i.Message.SentDate)
                .Take(take)
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
            return unread.Select(n => new NotificationItemViewModel
            {
                MessageId = n.MessageId,
                Subject = string.IsNullOrWhiteSpace(n.Subject) ? "(Konu yok)" : n.Subject,
                SenderDisplayName = DisplayName(n.SenderName, n.SenderSurname, n.SenderEmail),
                RelativeTime = n.SentDate.HasValue ? RelativeTime(n.SentDate.Value) : string.Empty
            }).ToList();
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