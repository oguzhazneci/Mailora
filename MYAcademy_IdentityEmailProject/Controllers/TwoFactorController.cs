using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models.Account;
using MYAcademy_IdentityEmailProject.Services;
using QRCoder;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    // 16: Authenticator uygulamasıyla iki adımlı doğrulama (TOTP).
    // [Authorize] yok: global AuthorizeFilter (3.2) zaten sadece giriş yapmış kullanıcıya izin veriyor.
    public class TwoFactorController : Controller
    {
        private const string Issuer = "Mailora";   // authenticator uygulamasında hesabın üstünde görünen ad
        private static readonly CultureInfo Tr = new("tr-TR");

        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IMailService _mailService;               // 16.6: güvenlik bildirimi (karar 5-A)
        private readonly ILogger<TwoFactorController> _logger;
        private readonly TimeProvider _time;

        public TwoFactorController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager,
                                   IMailService mailService, ILogger<TwoFactorController> logger, TimeProvider time)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _mailService = mailService;
            _logger = logger;
            _time = time;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            return View(new TwoFactorIndexViewModel
            {
                Enabled = user.TwoFactorEnabled,
                RecoveryCodesLeft = user.TwoFactorEnabled ? await _userManager.CountRecoveryCodesAsync(user) : 0
            });
        }

        // Kurulumu başlat: YENİ anahtar üretir. Yan etkili → POST (16.2 Kavram 1)
        [HttpPost]
        public async Task<IActionResult> Start()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            // 2FA açıkken anahtar buradan değişmez: yoksa çalışan kurulum sessizce bozulurdu (sıfırlama ResetKey'de, şifreyle)
            if (user.TwoFactorEnabled)
            {
                return RedirectToAction(nameof(Index));
            }

            await _userManager.ResetAuthenticatorKeyAsync(user);

            // Sıfırlama security stamp'i de yeniledi: bu oturumun çerezi eskimesin (16.2 Kavram 2)
            await _signInManager.RefreshSignInAsync(user);

            return RedirectToAction(nameof(Setup));
        }

        // QR ve anahtarı GÖSTERİR; hiçbir şey değiştirmez. Yenilemek anahtarı değiştirmez.
        [HttpGet]
        public async Task<IActionResult> Setup()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (user.TwoFactorEnabled)
            {
                return RedirectToAction(nameof(Index));   // açıkken anahtar tekrar gösterilmez
            }

            string? key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                return RedirectToAction(nameof(Index));   // önce "Kurulumu başlat"
            }

            // Sayfada gizli anahtar var: tarayıcı diske kaydetmesin (16.2 Kavram 3)
            Response.Headers.CacheControl = "no-store";
            return View(BuildSetupModel(user, key));
        }

        // Kodu doğrula → 2FA'yı aç → kurtarma kodlarını BİR KEZ göster (16.3)
        [HttpPost]
        public async Task<IActionResult> Setup(TwoFactorSetupViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            // Zaten açık: ikinci gönderim (F5) kodları yeniden üretmez
            if (user.TwoFactorEnabled)
            {
                return RedirectToAction(nameof(Index));
            }

            string? key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                return RedirectToAction(nameof(Index));
            }

            if (ModelState.IsValid)
            {
                string code = model.Code.Replace(" ", string.Empty);
                bool valid = await _userManager.VerifyTwoFactorTokenAsync(
                    user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

                if (valid)
                {
                    await _userManager.SetTwoFactorEnabledAsync(user, true);
                    await _signInManager.RefreshSignInAsync(user);   // stamp yenilendi

                    var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

                    await NotifyAsync(user, "İki adımlı doğrulama açıldı", "iki adımlı doğrulama açıldı");   // 16.6

                    // Yönlendirme YOK: kodlar bu yanıtta, bir kez
                    Response.Headers.CacheControl = "no-store";
                    return View("RecoveryCodes", recoveryCodes!.ToList());
                }

                ModelState.AddModelError(nameof(model.Code),
                    "Kod doğru değil. Uygulamadaki en güncel kodu girin ve telefonunuzun saatinin doğru olduğundan emin olun.");
            }

            // Hatalı ya da geçersiz: aynı sayfa, aynı anahtar (yazılan kod korunur)
            var page = BuildSetupModel(user, key);
            page.Code = model.Code;
            Response.Headers.CacheControl = "no-store";
            return View(page);
        }

        /* ---------- 16.5: Şifre tekrarı gerektiren işlemler (karar 4-A) ---------- */

        [HttpPost]
        public async Task<IActionResult> Disable(string? password)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }
            if (!await PasswordConfirmedAsync(user, password))
            {
                return RedirectToAction(nameof(Index));
            }

            await TurnOffAsync(user);
            await NotifyAsync(user, "İki adımlı doğrulama kapatıldı", "iki adımlı doğrulama kapatıldı");   // 16.6

            TempData["Toast"] = "İki adımlı doğrulama kapatıldı.";
            TempData["ToastIcon"] = "bi-shield-x";
            return RedirectToAction(nameof(Index));
        }

        // Telefon kayboldu ya da anahtar sızmış olabilir: kapat + yeni anahtar + kurulum ekranı (16.5 Kavram 3)
        [HttpPost]
        public async Task<IActionResult> ResetKey(string? password)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }
            if (!await PasswordConfirmedAsync(user, password))
            {
                return RedirectToAction(nameof(Index));
            }

            await TurnOffAsync(user);   // yeni anahtar da burada üretiliyor
            await NotifyAsync(user, "İki adımlı doğrulama anahtarı sıfırlandı",
                "iki adımlı doğrulama anahtarı sıfırlandı; kurulum yeniden tamamlanana kadar girişte kod istenmeyecek");   // 16.6

            TempData["Toast"] = "Anahtar sıfırlandı. Yeni QR kodunu okutup kurulumu tamamlayın.";
            TempData["ToastIcon"] = "bi-arrow-repeat";
            return RedirectToAction(nameof(Setup));
        }

        // Eski kodların hepsi geçersiz olur; yeni kodlar BİR KEZ gösterilir
        [HttpPost]
        public async Task<IActionResult> RegenerateCodes(string? password)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }
            if (!user.TwoFactorEnabled)
            {
                return RedirectToAction(nameof(Index));
            }
            if (!await PasswordConfirmedAsync(user, password))
            {
                return RedirectToAction(nameof(Index));
            }

            var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            await NotifyAsync(user, "Kurtarma kodları yenilendi", "kurtarma kodları yenilendi; eski kodlar artık geçersiz");   // 16.6

            Response.Headers.CacheControl = "no-store";
            return View("RecoveryCodes", recoveryCodes!.ToList());
        }

        /* ---------- Yardımcılar ---------- */

        // Yanlış şifre giriş ekranıyla AYNI sayaca eklenir: oturumu çalan biri burada sınırsız deneyemez (16.5 Kavram 2)
        private async Task<bool> PasswordConfirmedAsync(AppUser user, string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                TempData["Toast"] = "Bu işlem için şifrenizi girin.";
                TempData["ToastIcon"] = "bi-exclamation-circle";
                return false;
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                return true;
            }

            TempData["Toast"] = result.IsLockedOut
                ? "Çok fazla hatalı deneme yaptınız. Lütfen 5 dakika sonra tekrar deneyin."
                : "Şifre hatalı.";
            TempData["ToastIcon"] = "bi-exclamation-circle";
            return false;
        }

        // Kapat + anahtarı değiştir: eski telefonun ürettiği kodlar artık hiçbir işe yaramaz (16.5 Kavram 3)
        private async Task TurnOffAsync(AppUser user)
        {
            await _userManager.SetTwoFactorEnabledAsync(user, false);
            await _userManager.ResetAuthenticatorKeyAsync(user);
            await _signInManager.RefreshSignInAsync(user);   // stamp iki kez yenilendi; bu oturum kalsın
        }

        // 16.6: Güvenlik bildirimi (karar 5-A). Hesabı ele geçiren biri 2FA'yı SESSİZCE kapatamasın (Kavram 1).
        // Mail gönderilemezse işlem geri alınmaz: bildirim işlemin kendisinden daha az önemli (8.1'deki tutum).
        private async Task NotifyAsync(AppUser user, string subject, string whatHappened)
        {
            if (string.IsNullOrEmpty(user.Email))
            {
                return;
            }

            string name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(user.Name) ? user.UserName ?? "" : user.Name);
            string when = _time.GetLocalNow().ToString("d MMMM yyyy HH:mm", Tr);

            string body =
                $"<p>Merhaba {name},</p>" +
                $"<p>Mailora hesabınızda {when} tarihinde <b>{WebUtility.HtmlEncode(whatHappened)}</b>.</p>" +
                "<p>Bu işlemi siz yaptıysanız bir şey yapmanıza gerek yok.</p>" +
                "<p><b>Siz yapmadıysanız</b> hemen şifrenizi değiştirin ve iki adımlı doğrulamayı yeniden kurun.</p>";

            try
            {
                await _mailService.SendAsync(user.Email, $"Mailora güvenlik bildirimi: {subject}", body,
                                             isHtml: true, fromName: "Mailora Güvenlik");
            }
            catch (Exception ex)
            {
                // Loga adres yazılmaz, sadece Id (8.4)
                _logger.LogError(ex, "2FA güvenlik bildirimi gönderilemedi. UserId: {UserId}", user.Id);
            }
        }

        private static TwoFactorSetupViewModel BuildSetupModel(AppUser user, string key) => new()
        {
            SharedKey = FormatKey(key),
            QrCodeDataUri = QrDataUri(AuthenticatorUri(user.Email!, key))
        };

        // Authenticator uygulamalarının ortak adres biçimi: otpauth://totp/{Mailora}:{e-posta}?secret=...&issuer=...
        private static string AuthenticatorUri(string email, string key) =>
            $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}" +
            $"?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

        // QR sunucuda SVG olarak üretilir ve <img> içine veri adresi olarak gömülür:
        // Html.Raw gerekmez, harici JS kütüphanesi gerekmez (karar 1-A)
        private static string QrDataUri(string text)
        {
            using var generator = new QRCodeGenerator();
            using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            string svg = new SvgQRCode(data).GetGraphic(5);
            return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        }

        // "ABCDEFGHIJKL..." → "abcd efgh ijkl ...": elle yazarken kaybolmamak için
        private static string FormatKey(string key) =>
            string.Join(' ', key.ToLowerInvariant().Chunk(4).Select(c => new string(c)));
    }
}