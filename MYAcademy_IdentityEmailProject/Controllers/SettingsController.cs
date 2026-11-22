using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models.Account;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    // Ayarlar = hesabın GÜVENLİĞİ (10.1, Kavram 3). Global AuthorizeFilter korur.
    public class SettingsController : Controller
    {
        private static readonly CultureInfo Tr = new("tr-TR");

        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IMailService _mailService;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager,
                                  IMailService mailService, ILogger<SettingsController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _mailService = mailService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index() => View(new ChangePasswordViewModel());

        /* =====================================================================
           ŞİFRE DEĞİŞTİR (4.4'ten taşındı, 10.5'te güçlendirildi)
           ===================================================================== */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (ModelState.IsValid && model.NewPassword == model.CurrentPassword)
            {
                ModelState.AddModelError(nameof(model.NewPassword), "Yeni şifre mevcut şifrenizle aynı olamaz.");
            }

            if (!ModelState.IsValid)
            {
                return View(nameof(Index), model);
            }

            // Mevcut şifreyi doğrular, yeni şifreyi Identity kurallarıyla (Program.cs) denetler,
            // başarılıysa SecurityStamp'i yeniler → diğer oturumlar düşer (4.4)
            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);

            if (!result.Succeeded)
            {
                bool wrongCurrent = false;
                foreach (var error in result.Errors)
                {
                    // Hata kodu dil bağımsızdır; açıklama CustomIdentityValidator'dan Türkçe gelir
                    bool isMismatch = error.Code == nameof(IdentityErrorDescriber.PasswordMismatch);
                    wrongCurrent |= isMismatch;
                    ModelState.AddModelError(isMismatch ? nameof(model.CurrentPassword) : nameof(model.NewPassword), error.Description);
                }

                // Mevcut şifreyi tahmin etmek de bir kaba kuvvet saldırısıdır (Kavram 2):
                // giriş sayfasıyla AYNI sayaç ve AYNI kilit (5 deneme / 5 dakika, 3.x)
                if (wrongCurrent)
                {
                    await _userManager.AccessFailedAsync(user);
                    if (await _userManager.IsLockedOutAsync(user))
                    {
                        _logger.LogWarning("Şifre değiştirme denemeleri nedeniyle hesap kilitlendi. UserId: {UserId}", user.Id);
                        await _signInManager.SignOutAsync();
                        return RedirectToAction("SignIn", "Login");   // giriş sayfası kilit mesajını gösterir
                    }
                }

                return View(nameof(Index), model);
            }

            await _userManager.ResetAccessFailedCountAsync(user);

            // Bu tarayıcının çerezi eski SecurityStamp'i taşıyor: yenile ki BU oturum düşmesin (4.4)
            await _signInManager.RefreshSignInAsync(user);

            await NotifyPasswordChangedAsync(user);

            TempData["Toast"] = "Şifreniz değiştirildi. Diğer cihazlardaki oturumlarınız en geç 30 dakika içinde kapanacak.";
            TempData["ToastIcon"] = "bi-shield-check";
            return RedirectToAction(nameof(Index));   // PRG
        }

        /* =====================================================================
           DİĞER OTURUMLARI KAPAT (10.5, Kavram 4)
           ===================================================================== */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignOutOtherSessions()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            // Şifre değişmeden SecurityStamp'i yenile: eski damgayı taşıyan bütün çerezler geçersiz
            await _userManager.UpdateSecurityStampAsync(user);
            await _signInManager.RefreshSignInAsync(user);   // bu oturum yeni damgayı alsın

            TempData["Toast"] = "Diğer cihazlardaki oturumlarınız en geç 30 dakika içinde kapanacak.";
            TempData["ToastIcon"] = "bi-box-arrow-right";
            return RedirectToAction(nameof(Index));
        }

        /* ---------- Yardımcılar ---------- */

        // "Şifreniz değiştirildi" bildirimi (Kavram 5). Gönderilemezse işlem yine başarılı sayılır.
        private async Task NotifyPasswordChangedAsync(AppUser user)
        {
            if (string.IsNullOrEmpty(user.Email))
            {
                return;
            }

            string when = DateTime.Now.ToString("d MMMM yyyy HH:mm", Tr);
            string name = WebUtility.HtmlEncode($"{user.Name} {user.Surname}".Trim());
            string resetUrl = Url.Action("ForgotPassword", "Password", null, Request.Scheme)!;

            string body =
                $"<p>Merhaba {name},</p>" +
                $"<p>Mailora hesabınızın şifresi <b>{when}</b> tarihinde değiştirildi.</p>" +
                $"<p>Bu değişikliği siz yapmadıysanız hemen <a href=\"{resetUrl}\">şifrenizi sıfırlayın</a>.</p>";

            try
            {
                await _mailService.SendAsync(user.Email, "Mailora şifreniz değiştirildi", body, isHtml: true);
            }
            catch (Exception ex)
            {
                // Loga adres değil Id (8.4, Kavram 3)
                _logger.LogWarning(ex, "Şifre değişikliği bildirimi gönderilemedi. UserId: {UserId}", user.Id);
            }
        }
    }
}