using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    public class MessageController : Controller
    {
        private readonly EmailContext _context;
        private readonly UserManager<AppUser> _userManager;

        public MessageController(EmailContext context, UserManager<AppUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // KÖPRÜ (5.6): 7. aşamada sayfalama, filtre ve arama ile yeniden yazılacak.
        public async Task<IActionResult> Inbox()
        {
            string userId = _userManager.GetUserId(User)!;

            var items = await _context.MailboxItems
                .Where(i => i.UserId == userId && i.Folder == MailFolder.Inbox && i.DeletedDate == null)
                .Include(i => i.Message)
                    .ThenInclude(m => m.Sender)
                .OrderByDescending(i => i.Message.SentDate)
                .ToListAsync();

            return View(items);
        }

        public IActionResult CreateMessage()
        {
            ViewBag.Categories = new SelectList(_context.Categories.ToList(), "CategoryId", "CategoryName");
            return View();
        }

        // KÖPRÜ (5.6): Kaydetme ve gönderme 8. aşamada.
        [HttpPost]
        public IActionResult CreateMessage(CreateMessageViewModel createMessageViewModel)
        {
            return RedirectToAction("Sendbox");
        }

        // KÖPRÜ (5.6): 9. aşamada tasarımlı haliyle yeniden yazılacak.
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