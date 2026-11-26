using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    // UseStatusCodePagesWithReExecute bu sayfaları ORİJİNAL isteğin yöntemiyle çalıştırır: POST bir isteğin hatası
    // buraya POST olarak gelir. Global antiforgery filtresi (13.1) onu tekrar reddetmesin: bu sayfalar hiçbir şeyi
    // değiştirmez, sadece gösterir (14.7, CSRF testlerinin ortaya çıkardığı hata).
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public class ErrorPagesController : Controller
    {
        // 400: geçersiz istek. En sık sebep: süresi dolmuş ya da başka bir oturuma ait antiforgery token.
        public IActionResult Page400()
        {
            return View();
        }

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

        // 17.2: İstek sınırı aşıldı. Bekleme süresi rate limiter'ın OnRejected'ından gelir (HttpContext.Items)
        public IActionResult Page429()
        {
            ViewBag.Message = RateLimitPolicies.Message(HttpContext.Items[RateLimitPolicies.RetryAfterItemKey] as int?);
            return View();
        }

        public IActionResult Page404()
        {
            return View();
        }
    }
}