using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Models;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    public class HomeController : Controller
    {
        [AllowAnonymous]
        public IActionResult Index()
        {
            // Ana sayfa bir yönlendirici: giriş yapmış kullanıcıyı uygulamaya,
            // misafiri giriş sayfasına gönderir. (11. aşamada hedef Dashboard olacak.)
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Inbox", "Message");
            }
            return RedirectToAction("SignIn", "Login");
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}