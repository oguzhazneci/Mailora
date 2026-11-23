using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Models;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    public class HomeController : Controller
    {
        // Uygulamanın TEK "ana sayfa" kararı (11.5). Ana sayfaya giden her yol buradan geçer.
        [AllowAnonymous]
        public IActionResult Index()
        {
            return User.Identity?.IsAuthenticated == true
                ? RedirectToAction("Index", "Dashboard")
                : RedirectToAction("SignIn", "Login");
        }
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}