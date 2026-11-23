using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Helpers;
using MYAcademy_IdentityEmailProject.Models.Dashboard;

namespace MYAcademy_IdentityEmailProject.Services
{
    public class DashboardQueries : IDashboardQueries
    {
        public const int Days = 30;            // grafik penceresi (karar 3-A: 30 gün sayfaya gömülür)
        private const int RecentCount = 5;
        private const int ContactCount = 5;

        private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

        private readonly EmailContext _context;

        public DashboardQueries(EmailContext context)
        {
            _context = context;
        }

        /* Sorgu bütçesi (Kavram 2): istatistik 1 + günlük seri 1 + kategori 2 + son mesajlar 1 + kişiler 2-3
           = 7-8 sorgu. Sırayla çalışır: tek DbContext aynı anda tek sorgu yürütebilir (Kavram 5). */
        public async Task<DashboardData> GetAsync(string userId)
        {
            DateTime today = DateTime.Today;                    // yerel saat (Aşama 5'teki DateTime.Now kararı)
            DateTime seriesStart = today.AddDays(-(Days - 1));  // bugün dahil 30 gün
            DateTime weekStart = today.AddDays(-6);             // bugün dahil 7 gün

            var data = new DashboardData
            {
                Stats = await GetStatsAsync(userId, weekStart),
                Daily = await GetDailyAsync(userId, seriesStart)
            };

            (data.Categories, data.CategoryTotal) = await GetCategoriesAsync(userId);
            data.Recent = await GetRecentAsync(userId);
            data.Contacts = await GetContactsAsync(userId, seriesStart);

            return data;
        }

        /* ---------- 1) Durum sayıları: TEK sorgu (6.4'teki teknik) ----------
           Koşullar sidebar sayacıyla (MailboxCounter) AYNI: rozetle kart aynı sayıyı göstermeli (9.4). */
        private async Task<DashboardStats> GetStatsAsync(string userId, DateTime weekStart)
        {
            var stats = await _context.MailboxItems
                .Where(i => i.UserId == userId)
                // Önce düz sütunlara indir (Message.SentDate bir JOIN olur), sonra grupla:
                // gruplanmış sayımlarda gezinme özelliği yerine düz sütun kullanmak çeviriyi garantiler.
                .Select(i => new { i.UserId, i.Folder, i.IsRead, i.IsStarred, i.DeletedDate, i.ArchivedDate, i.Message.SentDate })
                .GroupBy(x => x.UserId)
                .Select(g => new DashboardStats
                {
                    // "Gelen Kutusu" = gelen + çöpte değil + ARŞİVDE DEĞİL (12.2: durum sayısı)
                    InboxTotal = g.Count(x => x.Folder == MailFolder.Inbox && x.DeletedDate == null && x.ArchivedDate == null),
                    InboxLast7 = g.Count(x => x.Folder == MailFolder.Inbox && x.DeletedDate == null && x.ArchivedDate == null && x.SentDate >= weekStart),
                    SentTotal = g.Count(x => x.Folder == MailFolder.Sent && x.DeletedDate == null),
                    SentLast7 = g.Count(x => x.Folder == MailFolder.Sent && x.DeletedDate == null && x.SentDate >= weekStart),
                    // Arşivdeki okunmamışlar sayılmaz (karar 3-A) — sidebar rozetiyle aynı koşul
                    Unread = g.Count(x => x.Folder == MailFolder.Inbox && !x.IsRead && x.DeletedDate == null && x.ArchivedDate == null),
                    UnreadLast7 = g.Count(x => x.Folder == MailFolder.Inbox && !x.IsRead && x.DeletedDate == null && x.ArchivedDate == null && x.SentDate >= weekStart),
                    Starred = g.Count(x => x.IsStarred && x.DeletedDate == null),
                    Drafts = g.Count(x => x.Folder == MailFolder.Draft && x.DeletedDate == null)
                })
                .FirstOrDefaultAsync();

            return stats ?? new DashboardStats();   // hiç mesajı olmayan kullanıcı (ahmet01)
        }

        /* ---------- 2) Günlük seri: SQL gruplar, C# boş günleri doldurur (Kavram 3) ----------
           OLAY sayısı: o gün gelen/gönderilen her şey, sonradan çöpe atılmış olsa bile (Kavram 1). */
        private async Task<List<DailyActivity>> GetDailyAsync(string userId, DateTime seriesStart)
        {
            var rows = await _context.MailboxItems
                .Where(i => i.UserId == userId
                         && i.Folder != MailFolder.Draft
                         && i.Message.SentDate >= seriesStart)
                .Select(i => new { Day = i.Message.SentDate!.Value.Date, i.Folder })   // SQL: CONVERT(date, SentDate)
                .GroupBy(x => new { x.Day, x.Folder })
                .Select(g => new { g.Key.Day, g.Key.Folder, Count = g.Count() })
                .ToListAsync();

            // SQL sadece mesaj OLAN günleri döndürür. Eksik günler burada 0 ile tamamlanır.
            var lookup = rows.ToDictionary(r => (r.Day, r.Folder), r => r.Count);

            var series = new List<DailyActivity>(Days);
            for (int d = 0; d < Days; d++)
            {
                DateTime day = seriesStart.AddDays(d);
                series.Add(new DailyActivity
                {
                    Date = day,
                    Received = lookup.GetValueOrDefault((day, MailFolder.Inbox)),
                    Sent = lookup.GetValueOrDefault((day, MailFolder.Sent))
                });
            }
            return series;
        }

        /* ---------- 3) Kategori dağılımı (karar 4-A): Gelen + Gönderilen, çöp ve taslak hariç ----------
           DURUM sayısı: kutuda şu an ne var. 2 sorgu: sayımlar + kategori adları/renkleri (10 satır). */
        private async Task<(List<CategoryShare> Items, int Total)> GetCategoriesAsync(string userId)
        {
            var counts = await _context.MailboxItems
                .Where(i => i.UserId == userId
                         && i.DeletedDate == null
                         && (i.Folder == MailFolder.Inbox || i.Folder == MailFolder.Sent))
                .Select(i => i.Message.CategoryId)
                .GroupBy(categoryId => categoryId)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .ToListAsync();

            var categories = await _context.Categories
                .Select(c => new { c.CategoryId, c.CategoryName, c.ColorCode })
                .ToDictionaryAsync(c => c.CategoryId);

            var items = counts
                .Select(c =>
                {
                    bool known = c.CategoryId.HasValue && categories.ContainsKey(c.CategoryId.Value);
                    return new CategoryShare
                    {
                        CategoryId = c.CategoryId,
                        Name = known ? categories[c.CategoryId!.Value].CategoryName : "Kategorisiz",
                        Color = known ? categories[c.CategoryId!.Value].ColorCode : "#85899B",
                        Count = c.Count
                    };
                })
                .OrderByDescending(c => c.Count)
                .ThenBy(c => c.Name)
                .ToList();

            return (items, items.Sum(c => c.Count));
        }

        /* ---------- 4) Son gelen 5 mesaj: Include değil PROJEKSİYON (7.2) ----------
           Sadece gereken sütunlar gelir; özet (snippet) C#'ta hesaplanır. */
        private async Task<List<RecentMessage>> GetRecentAsync(string userId)
        {
            var rows = await _context.MailboxItems
                .Where(i => i.UserId == userId && i.Folder == MailFolder.Inbox && i.DeletedDate == null && i.ArchivedDate == null)
                .OrderByDescending(i => i.Message.SentDate)
                .ThenByDescending(i => i.MailboxItemId)
                .Take(RecentCount)
                .Select(i => new
                {
                    i.MailboxItemId,
                    i.MessageId,
                    i.IsRead,
                    i.Message.Subject,
                    i.Message.Body,
                    i.Message.SentDate,
                    i.Message.Sender.Name,
                    i.Message.Sender.Surname,
                    i.Message.Sender.Email,
                    i.Message.Sender.ImageUrl,
                    CategoryName = i.Message.Category != null ? i.Message.Category.CategoryName : null,
                    CategoryColor = i.Message.Category != null ? i.Message.Category.ColorCode : null
                })
                .ToListAsync();

            return rows.Select(r => new RecentMessage
            {
                MailboxItemId = r.MailboxItemId,
                MessageId = r.MessageId,
                IsRead = r.IsRead,
                Subject = string.IsNullOrWhiteSpace(r.Subject) ? "(Konu yok)" : r.Subject,
                Snippet = Snippet(r.Body),
                SentDate = r.SentDate,
                SenderName = FullName(r.Name, r.Surname, r.Email),
                SenderEmail = r.Email,
                SenderPhoto = AvatarHelper.IsAvatarFile(r.ImageUrl) ? r.ImageUrl : null,
                CategoryName = r.CategoryName,
                CategoryColor = r.CategoryColor
            }).ToList();
        }

        /* ---------- 5) Son 30 günde en çok yazışılan kişiler ----------
           OLAY sayısı. İki yön ayrı gruplanır, C#'ta birleştirilir; adlar tek sorguyla (9.3'teki N+1 dersi). */
        private async Task<List<TopContact>> GetContactsAsync(string userId, DateTime since)
        {
            // Ondan bana: gelen kopyalarımın göndereni
            var received = await _context.MailboxItems
                .Where(i => i.UserId == userId && i.Folder == MailFolder.Inbox && i.Message.SentDate >= since)
                .Select(i => i.Message.SenderId)
                .GroupBy(id => id)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync();

            // Benden ona: gönderilen kopyalarımın KAYITLI alıcıları (dış adreslerin UserId'si yok)
            var sent = await _context.MailboxItems
                .Where(i => i.UserId == userId && i.Folder == MailFolder.Sent && i.Message.SentDate >= since)
                .SelectMany(i => i.Message.Recipients)
                .Where(r => r.UserId != null)
                .Select(r => r.UserId!)
                .GroupBy(id => id)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToListAsync();

            var totals = new Dictionary<string, TopContact>();
            foreach (var r in received)
            {
                totals[r.UserId] = new TopContact { UserId = r.UserId, Received = r.Count };
            }
            foreach (var s in sent)
            {
                if (!totals.TryGetValue(s.UserId, out var contact))
                {
                    contact = new TopContact { UserId = s.UserId };
                    totals[s.UserId] = contact;
                }
                contact.Sent = s.Count;
            }
            totals.Remove(userId);   // kendime gönderdiklerim "kişi" sayılmaz

            var top = totals.Values
                .OrderByDescending(c => c.Total)
                .Take(ContactCount)
                .ToList();

            if (top.Count == 0)
            {
                return top;
            }

            var ids = top.Select(c => c.UserId).ToList();
            var people = await _context.Users
                .Where(u => ids.Contains(u.Id))                  // SQL: WHERE Id IN (...)
                .Select(u => new { u.Id, u.Name, u.Surname, u.Email, u.ImageUrl })
                .ToDictionaryAsync(u => u.Id);

            foreach (var c in top)
            {
                if (people.TryGetValue(c.UserId, out var p))
                {
                    c.Name = FullName(p.Name, p.Surname, p.Email);
                    c.Email = p.Email;
                    c.Photo = AvatarHelper.IsAvatarFile(p.ImageUrl) ? p.ImageUrl : null;
                }
            }

            return top.OrderByDescending(c => c.Total).ThenBy(c => c.Name).ToList();
        }

        /* ---------- Yardımcılar ---------- */

        private static string FullName(string? name, string? surname, string? email)
        {
            string full = $"{name} {surname}".Trim();
            return full.Length > 0 ? full : email ?? "Bilinmeyen";
        }

        // HTML gövdeden tek satırlık düz metin özet (liste sayfasındaki Snippet ile aynı fikir)
        private static string Snippet(string? html, int max = 90)
        {
            string text = WebUtility.HtmlDecode(Tags.Replace(html ?? string.Empty, " "));
            text = Spaces.Replace(text, " ").Trim();
            return text.Length > max ? text[..max].TrimEnd() + "…" : text;
        }
    }
}