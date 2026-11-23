using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models.Messages;

namespace MYAcademy_IdentityEmailProject.Services
{
    public class MailboxCounter : IMailboxCounter
    {
        private readonly EmailContext _context;

        public MailboxCounter(EmailContext context)
        {
            _context = context;
        }

        // Bütün sayılar + imleç TEK sorguda (6.4). Koşullar Genel Bakış kartlarıyla AYNI (9.4, 12.2).
        public async Task<MailboxCounts> GetAsync(string userId)
        {
            var counts = await _context.MailboxItems
                .Where(i => i.UserId == userId)
                .GroupBy(i => i.UserId)
                .Select(g => new MailboxCounts
                {
                    // Arşivdeki okunmamışlar sayılmaz (12.1, karar 3-A)
                    UnreadInbox = g.Count(i => i.Folder == MailFolder.Inbox && !i.IsRead && i.DeletedDate == null && i.ArchivedDate == null),
                    Drafts = g.Count(i => i.Folder == MailFolder.Draft && i.DeletedDate == null),
                    Starred = g.Count(i => i.IsStarred && i.DeletedDate == null),
                    Trash = g.Count(i => i.DeletedDate != null),

                    // SQL: MAX(CASE WHEN Folder = 'Inbox' THEN MailboxItemId ELSE 0 END) — ek sorgu yok
                    LatestInboxItemId = g.Max(i => i.Folder == MailFolder.Inbox ? i.MailboxItemId : 0)
                })
                .FirstOrDefaultAsync();

            return counts ?? new MailboxCounts();
        }
    }
}