using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    [AllowAnonymous]
    public class ErrorPagesController : Controller
    {
        public IActionResult Page401()
        {
            // ReExecute sırasında orijinal adres bu özellikte saklanır.
            // Giriş yapınca kullanıcıyı gitmek istediği sayfaya geri göndermek için kullanıyoruz.
            var reExecuteFeature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
            if (reExecuteFeature != null)
            {
                ViewBag.ReturnUrl = reExecuteFeature.OriginalPath + reExecuteFeature.OriginalQueryString;
            }
            return View();
        }

        public IActionResult Page403()
        {
            return View();
        }

        public IActionResult Page404()
        {
            return View();
        }
    }
}