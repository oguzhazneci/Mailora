using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Services;
using MYAcademy_IdentityEmailProject.Models.Account;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    [AllowAnonymous]
    public class RegisterController : Controller
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly IMailService _mailService;

        public RegisterController(UserManager<AppUser> userManager, IMailService mailService)
        {
            _userManager = userManager;
            _mailService = mailService;
        }

        [HttpGet]
        public IActionResult SignUp()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignUp(UserRegisterViewModel userRegisterViewModel)
        {
            if (!ModelState.IsValid)
            {
                return View(userRegisterViewModel);
            }

            int validCode = GenerateValidCode();

            AppUser appUser = new AppUser()
            {
                Name = userRegisterViewModel.Name,
                Surname = userRegisterViewModel.Surname,
                Email = userRegisterViewModel.Email,
                UserName = userRegisterViewModel.Username,
                ValidCode = validCode
            };

            var result = await _userManager.CreateAsync(appUser, userRegisterViewModel.Password);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return View(userRegisterViewModel);
            }

            await TrySendValidCodeAsync(appUser.Email!, validCode);
            return RedirectToAction("ConfirmEmail", new { email = appUser.Email });
        }

        [HttpGet]
        public IActionResult ConfirmEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction("SignUp");
            }
            return View(new ConfirmEmailViewModel { Email = email });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmEmail(ConfirmEmailViewModel confirmEmailViewModel)
        {
            if (!ModelState.IsValid)
            {
                return View(confirmEmailViewModel);
            }

            var user = await _userManager.FindByEmailAsync(confirmEmailViewModel.Email);

            if (user != null && user.EmailConfirmed)
            {
                TempData["InfoMessage"] = "Hesabınız zaten doğrulanmış. Giriş yapabilirsiniz.";
                return RedirectToAction("SignIn", "Login");
            }

            if (user == null || user.ValidCode == null || user.ValidCode.ToString() != confirmEmailViewModel.Code)
            {
                ModelState.AddModelError(string.Empty, "Doğrulama kodu hatalı.");
                return View(confirmEmailViewModel);
            }

            user.EmailConfirmed = true;
            user.ValidCode = null;
            await _userManager.UpdateAsync(user);

            TempData["SuccessMessage"] = "Hesabınız doğrulandı. Artık giriş yapabilirsiniz.";
            return RedirectToAction("SignIn", "Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendCode(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);

            if (user != null && !user.EmailConfirmed)
            {
                int validCode = GenerateValidCode();
                user.ValidCode = validCode;
                await _userManager.UpdateAsync(user);

                if (await TrySendValidCodeAsync(user.Email!, validCode))
                {
                    TempData["InfoMessage"] = "Yeni doğrulama kodu e-posta adresinize gönderildi.";
                }
            }

            return RedirectToAction("ConfirmEmail", new { email });
        }

        [HttpGet]
        public async Task<IActionResult> CheckUserName(string userName)
        {
            var user = await _userManager.FindByNameAsync(userName);
            return Json(new { available = user == null });
        }

        private static int GenerateValidCode()
        {
            return RandomNumberGenerator.GetInt32(100000, 1000000);
        }

        private async Task<bool> TrySendValidCodeAsync(string email, int validCode)
        {
            string body = "Merhaba,\n\n"
                        + "Mailora hesabınızı doğrulamak için kodunuz: " + validCode + "\n\n"
                        + "Bu işlemi siz yapmadıysanız bu e-postayı dikkate almayın.";
            try
            {
                await _mailService.SendAsync(email, "Mailora · E-posta Doğrulama Kodu", body);
                return true;
            }
            catch
            {
                TempData["ErrorMessage"] = "Doğrulama kodu gönderilemedi. Lütfen \"Kodu tekrar gönder\" ile yeniden deneyin.";
                return false;
            }
        }
    }
}