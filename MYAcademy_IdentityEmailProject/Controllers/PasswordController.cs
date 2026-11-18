using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    public class PasswordController : Controller
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IMailService _mailService;
        private readonly ILogger<PasswordController> _logger;

        public PasswordController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager,
                                  IMailService mailService, ILogger<PasswordController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _mailService = mailService;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel forgotPasswordViewModel)
        {
            if (!ModelState.IsValid)
            {
                return View(forgotPasswordViewModel);
            }

            var user = await _userManager.FindByEmailAsync(forgotPasswordViewModel.Email);

            // Yalnızca kayıtlı ve doğrulanmış hesaplara bağlantı gönderilir.
            if (user != null && user.EmailConfirmed)
            {
                string token = await _userManager.GeneratePasswordResetTokenAsync(user);
                string encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

                string resetLink = Url.Action("ResetPassword", "Password",
                    new { userId = user.Id, token = encodedToken }, Request.Scheme)!;

                string body =
                    $"<p>Merhaba {WebUtility.HtmlEncode(user.Name)},</p>" +
                    "<p>Mailora hesabınız için şifre yenileme talebi aldık. Yeni şifrenizi belirlemek için aşağıdaki bağlantıya tıklayın:</p>" +
                    $"<p><a href=\"{resetLink}\">Şifremi Yenile</a></p>" +
                    "<p>Bu bağlantı <b>1 saat</b> geçerlidir ve yalnızca bir kez kullanılabilir.</p>" +
                    "<p>Bu talebi siz yapmadıysanız bu e-postayı dikkate almayın; şifreniz değişmeyecektir.</p>";

                try
                {
                    await _mailService.SendAsync(user.Email!, "Mailora · Şifre Yenileme", body, isHtml: true);
                }
                catch (Exception ex)
                {
                    // Kullanıcıya gösterilmez: hata mesajı adresin kayıtlı olduğunu ele verirdi.
                    _logger.LogError(ex, "Şifre yenileme e-postası gönderilemedi. UserId: {UserId}", user.Id);
                }
            }

            // Adres kayıtlı olsun ya da olmasın aynı mesaj (numaralandırmaya karşı).
            TempData["InfoMessage"] = "Bu e-posta adresi kayıtlıysa şifre yenileme bağlantısı gönderildi. Bağlantı 1 saat geçerlidir.";
            return RedirectToAction("ForgotPassword");
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> ResetPassword(string? userId, string? token)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            {
                return InvalidResetLink();
            }

            var user = await _userManager.FindByIdAsync(userId);
            string? decodedToken = DecodeToken(token);

            // Kullanıcı deneyimi için ön kontrol: geçersiz bağlantıda şifre yazdırmadan uyar.
            // Asıl güvenlik kontrolü POST'taki ResetPasswordAsync içinde tekrar yapılır.
            if (user == null || decodedToken == null ||
                !await _userManager.VerifyUserTokenAsync(user,
                    _userManager.Options.Tokens.PasswordResetTokenProvider,
                    UserManager<AppUser>.ResetPasswordTokenPurpose,
                    decodedToken))
            {
                return InvalidResetLink();
            }

            return View(new ResetPasswordViewModel { UserId = userId, Token = token });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel resetPasswordViewModel)
        {
            if (!ModelState.IsValid)
            {
                return View(resetPasswordViewModel);
            }

            var user = await _userManager.FindByIdAsync(resetPasswordViewModel.UserId);
            string? decodedToken = DecodeToken(resetPasswordViewModel.Token);

            if (user == null || decodedToken == null)
            {
                return InvalidResetLink();
            }

            var result = await _userManager.ResetPasswordAsync(user, decodedToken, resetPasswordViewModel.NewPassword);

            if (result.Succeeded)
            {
                // E-postasının sahibi olduğunu kanıtlayan kullanıcı kilitli kalmasın.
                await _userManager.SetLockoutEndDateAsync(user, null);
                await _userManager.ResetAccessFailedCountAsync(user);

                TempData["SuccessMessage"] = "Şifreniz güncellendi. Yeni şifrenizle giriş yapabilirsiniz.";
                return RedirectToAction("SignIn", "Login");
            }

            // Token hatası: bu sayfada tekrar denemenin anlamı yok.
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                return InvalidResetLink();
            }

            // Şifre kuralı hataları: token hâlâ geçerli, kullanıcı aynı sayfada düzeltebilir.
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(resetPasswordViewModel);
        }

        // Giriş zorunlu: [AllowAnonymous] yok, global filtre korur.
        [HttpGet]
        public IActionResult ChangePassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel changePasswordViewModel)
        {
            if (!ModelState.IsValid)
            {
                return View(changePasswordViewModel);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var result = await _userManager.ChangePasswordAsync(user,
                changePasswordViewModel.CurrentPassword, changePasswordViewModel.NewPassword);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    // "Mevcut şifre hatalı" mevcut şifre alanının, kural hataları yeni şifre alanının altında görünür.
                    string fieldName = error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)
                        ? nameof(ChangePasswordViewModel.CurrentPassword)
                        : nameof(ChangePasswordViewModel.NewPassword);
                    ModelState.AddModelError(fieldName, error.Description);
                }
                return View(changePasswordViewModel);
            }

            // Security stamp değişti: bu tarayıcıya yeni stamp'li çerez ver ki kullanıcı oturumda kalsın.
            // Diğer cihazlardaki eski çerezler SecurityStampValidator tarafından geçersiz sayılacak.
            await _signInManager.RefreshSignInAsync(user);

            TempData["SuccessMessage"] = "Şifreniz değiştirildi. Diğer cihazlardaki oturumlarınız kapatılacak.";
            return RedirectToAction("ChangePassword");
        }

        private static string? DecodeToken(string encodedToken)
        {
            try
            {
                return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
            }
            catch (FormatException)
            {
                // Base64Url biçimine uymayan (bozulmuş) token
                return null;
            }
        }

        private IActionResult InvalidResetLink()
        {
            TempData["ErrorMessage"] = "Şifre yenileme bağlantısı geçersiz veya süresi dolmuş. Lütfen yeni bir bağlantı isteyin.";
            return RedirectToAction("ForgotPassword");
        }
    }
}