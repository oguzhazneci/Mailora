using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models;

namespace MYAcademy_IdentityEmailProject.ViewComponents.MessageLayoutViewComponents
{
    public class _MessageLayoutSidebarComponentPartial : ViewComponent
    {
        private readonly EmailContext _context;
        private readonly UserManager<AppUser> _userManager;

        public _MessageLayoutSidebarComponentPartial(EmailContext context, UserManager<AppUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            string userId = _userManager.GetUserId(UserClaimsPrincipal)!;

            // Sidebar her sayfada çizilir: dört sayaç TEK sorguda hesaplanır.
            // SQL karşılığı: COUNT(CASE WHEN ... THEN 1 END) x 4, GROUP BY UserId
            var model = await _context.MailboxItems
                .Where(i => i.UserId == userId)
                .GroupBy(i => i.UserId)
                .Select(g => new SidebarViewModel
                {
                    UnreadInbox = g.Count(i => i.Folder == MailFolder.Inbox && !i.IsRead && i.DeletedDate == null),
                    Drafts = g.Count(i => i.Folder == MailFolder.Draft && i.DeletedDate == null),
                    Starred = g.Count(i => i.IsStarred && i.DeletedDate == null),
                    Trash = g.Count(i => i.DeletedDate != null)
                })
                .FirstOrDefaultAsync()
                ?? new SidebarViewModel();   // hiç mesajı olmayan kullanıcı: hepsi 0

            model.Categories = await _context.Categories
                .OrderBy(c => c.CategoryId)
                .ToListAsync();

            return View(model);
        }
    }
}