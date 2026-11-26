using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MYAcademy_IdentityEmailProject.Services;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models.Account;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    public class LoginController : Controller
    {
        private const string LockedOutMessage = "Çok fazla hatalı deneme yaptınız. Lütfen 5 dakika sonra tekrar deneyin.";

        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;

        public LoginController(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult SignIn(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignIn(UserLoginViewModel userLoginViewModel, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(userLoginViewModel);
            }

            var result = await _signInManager.PasswordSignInAsync(
                userLoginViewModel.Username,
                userLoginViewModel.Password,
                userLoginViewModel.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                return RedirectAfterSignIn(returnUrl);
            }

            // 16.4: Şifre doğru ama hesapta 2FA açık. Henüz giriş YOK: Identity 5 dakikalık ara bir çerez yazdı (Kavram 1)
            if (result.RequiresTwoFactor)
            {
                return RedirectToAction(nameof(LoginWith2fa), new { rememberMe = userLoginViewModel.RememberMe, returnUrl });
            }

            if (result.IsNotAllowed)
            {
                // Identity, doğrulanmamış hesapta şifreye bakmadan IsNotAllowed döner.
                // Doğrulama sayfasına yalnızca şifre doğruysa yönlendiriyoruz.
                var user = await _userManager.FindByNameAsync(userLoginViewModel.Username);
                if (user != null && await _userManager.CheckPasswordAsync(user, userLoginViewModel.Password))
                {
                    TempData["InfoMessage"] = "Giriş yapabilmek için önce e-posta adresinizi doğrulamalısınız.";
                    return RedirectToAction("ConfirmEmail", "Register", new { email = user.Email });
                }
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, LockedOutMessage);
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
            }

            return View(userLoginViewModel);
        }

        /* =====================================================================
           İKİ ADIMLI DOĞRULAMA: GİRİŞİN İKİNCİ ADIMI (16.4)
           ===================================================================== */

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> LoginWith2fa(bool rememberMe, string? returnUrl = null)
        {
            // Ara çerez yoksa (süresi dolmuş ya da adres doğrudan açılmış) bu sayfanın anlamı yok (Kavram 1)
            if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
            {
                return TwoFactorExpired(returnUrl);
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginWith2faViewModel { RememberMe = rememberMe });
        }

        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWith2fa(LoginWith2faViewModel model, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;

            if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
            {
                return TwoFactorExpired(returnUrl);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Yanlış kod kilitleme sayacına eklenir: 5 yanlışta 5 dakika (Kavram 3)
            var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
                model.Code.Replace(" ", string.Empty), model.RememberMe, model.RememberMachine);

            if (result.Succeeded)
            {
                return RedirectAfterSignIn(returnUrl);
            }

            ModelState.AddModelError(nameof(model.Code), result.IsLockedOut ? LockedOutMessage : "Kod doğru değil.");
            return View(model);
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> LoginWithRecoveryCode(string? returnUrl = null)
        {
            if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
            {
                return TwoFactorExpired(returnUrl);
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(new LoginWithRecoveryCodeViewModel());
        }

        [AllowAnonymous]
        [EnableRateLimiting(RateLimitPolicies.Auth)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LoginWithRecoveryCode(LoginWithRecoveryCodeViewModel model, string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;

            if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
            {
                return TwoFactorExpired(returnUrl);
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Kod kullanılınca silinir (tek kullanımlık). Yanlış kod sayaca eklenmez: 17. aşamadaki rate limiting (Kavram 3)
            // Identity'nin kodları sadece büyük harf ve rakam: küçük harfle yazan kullanıcı da girebilsin
            var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(
                model.RecoveryCode.Replace(" ", string.Empty).ToUpperInvariant());

            if (result.Succeeded)
            {
                TempData["Toast"] = "Kurtarma koduyla giriş yaptınız. Telefonunuz kaybolduysa iki adımlı doğrulamayı yeniden kurun.";
                TempData["ToastIcon"] = "bi-life-preserver";
                return RedirectAfterSignIn(returnUrl);
            }

            ModelState.AddModelError(nameof(model.RecoveryCode),
                result.IsLockedOut ? LockedOutMessage : "Kurtarma kodu geçersiz ya da daha önce kullanılmış.");
            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            TempData["SuccessMessage"] = "Oturumunuz kapatıldı.";
            return RedirectToAction("SignIn");
        }

        /* ---------- Yardımcılar ---------- */

        // Açık yönlendirmeye karşı: sadece bu sitenin adresleri (eski SignIn'deki kontrol, artık üç yerde kullanılıyor)
        private IActionResult RedirectAfterSignIn(string? returnUrl) =>
            !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction("Index", "Home");

        // Ara çerez yok ya da süresi doldu: şifre adımına geri
        private IActionResult TwoFactorExpired(string? returnUrl)
        {
            TempData["InfoMessage"] = "Doğrulama süresi doldu. Lütfen yeniden giriş yapın.";
            return RedirectToAction(nameof(SignIn), new { returnUrl });
        }
    }
}