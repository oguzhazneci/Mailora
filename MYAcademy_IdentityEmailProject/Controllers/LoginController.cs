using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models.Account;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    public class LoginController : Controller
    {
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
                return RedirectToAction("Inbox", "Message");
            }
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [AllowAnonymous]
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
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return LocalRedirect(returnUrl);
                }
                return RedirectToAction("Inbox", "Message");
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
                ModelState.AddModelError(string.Empty, "Çok fazla hatalı deneme yaptınız. Lütfen 5 dakika sonra tekrar deneyin.");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
            }

            return View(userLoginViewModel);
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
    }
}