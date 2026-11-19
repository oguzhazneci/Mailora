using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.ViewComponents.MessageLayoutViewComponents
{
    public class _MessageLayoutSidebarComponentPartial : ViewComponent
    {
        private readonly EmailContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly IMailboxCounter _counter;

        public _MessageLayoutSidebarComponentPartial(EmailContext context, UserManager<AppUser> userManager, IMailboxCounter counter)
        {
            _context = context;
            _userManager = userManager;
            _counter = counter;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            string userId = _userManager.GetUserId(UserClaimsPrincipal)!;

            var model = new SidebarViewModel
            {
                Counts = await _counter.GetAsync(userId),   // controller'daki JSON yanıtlarıyla aynı hesap
                Categories = await _context.Categories.OrderBy(c => c.CategoryId).ToListAsync()
            };

            return View(model);
        }
    }
}