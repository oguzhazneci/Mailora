using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    // Global AuthorizeFilter korur (3.2).
    public class DashboardController : Controller
    {
        private readonly IDashboardQueries _queries;
        private readonly UserManager<AppUser> _userManager;

        public DashboardController(IDashboardQueries queries, UserManager<AppUser> userManager)
        {
            _queries = queries;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            string userId = _userManager.GetUserId(User)!;   // kimin verisi? her zaman oturumdan (10.2)
            return View(await _queries.GetAsync(userId));
        }
    }
}