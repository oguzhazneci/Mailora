using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models;

namespace MYAcademy_IdentityEmailProject.Services
{
    // "Okunmamış", "yıldızlı" vb. tanımlarının projedeki TEK yeri.
    public class MailboxCounter : IMailboxCounter
    {
        private readonly EmailContext _context;

        public MailboxCounter(EmailContext context)
        {
            _context = context;
        }

        public async Task<MailboxCounts> GetAsync(string userId)
        {
            // Dört sayaç TEK sorguda: COUNT(CASE WHEN ... THEN 1 END) x 4, GROUP BY UserId
            return await _context.MailboxItems
                .Where(i => i.UserId == userId)
                .GroupBy(i => i.UserId)
                .Select(g => new MailboxCounts
                {
                    UnreadInbox = g.Count(i => i.Folder == MailFolder.Inbox && !i.IsRead && i.DeletedDate == null),
                    Drafts = g.Count(i => i.Folder == MailFolder.Draft && i.DeletedDate == null),
                    Starred = g.Count(i => i.IsStarred && i.DeletedDate == null),
                    Trash = g.Count(i => i.DeletedDate != null)
                })
                .FirstOrDefaultAsync()
                ?? new MailboxCounts();   // hiç mesajı olmayan kullanıcı: hepsi 0
        }
    }
}