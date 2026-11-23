using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Helpers;
using MYAcademy_IdentityEmailProject.Identity;
using MYAcademy_IdentityEmailProject.Models.Layout;
using MYAcademy_IdentityEmailProject.Services;
using System.Security.Claims;

namespace MYAcademy_IdentityEmailProject.ViewComponents.MessageLayoutViewComponents
{
    public class _MessageLayoutHeaderComponentPartial : ViewComponent
    {
        private readonly INotificationFeed _feed;
        private readonly UserManager<AppUser> _userManager;

        public _MessageLayoutHeaderComponentPartial(INotificationFeed feed, UserManager<AppUser> userManager)
        {
            _feed = feed;
            _userManager = userManager;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            // Kullanıcı bilgileri çerezdeki claim'lerden: veritabanı sorgusu yok (6.3).
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
                PhotoFile = user.FindFirstValue(AppClaimTypes.Avatar),
                Initials = AvatarHelper.Initials(fullName)       // tek kural (10.4)
            };

            // Zil menüsü: sorgu NotificationFeed'de (12.6). Aynı veri yenilemede de oradan gelir.
            string userId = _userManager.GetUserId(user)!;
            model.Notifications = await _feed.GetUnreadAsync(userId);

            return View(model);
        }
    }
}