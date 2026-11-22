using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Helpers;
using MYAcademy_IdentityEmailProject.Models.Account;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MYAcademy_IdentityEmailProject.Controllers
{
    // [Authorize] yazmaya gerek yok: global AuthorizeFilter (3.2) bütün action'ları korur.
    public class ProfileController : Controller
    {
        /* ---------- Profil fotoğrafı kuralları (10.3) ---------- */
        private const long MaxPhotoBytes = 5 * 1024 * 1024;    // 5 MB (dosya boyutu)
        private const int MaxPhotoSide = 8000;                 // piksel (açılmış boyut: Kavram 3)
        private const int AvatarSize = 256;                    // kaydedilen kare avatar
        // Ad kuralı (32 onaltılık karakter + .jpg) AvatarHelper'da: liste, Detay ve navbar da aynı kuralı kullanır (10.4)

        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager,
                                 IWebHostEnvironment env, ILogger<ProfileController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _env = env;
            _logger = logger;
        }

        // wwwroot DIŞINDA (karar 3-C): bu klasördeki dosyalara tarayıcı doğrudan ulaşamaz, sadece Avatar action'ı sunar.
        private string AvatarFolder => Path.Combine(_env.ContentRootPath, "App_Data", "avatars");

        /* =====================================================================
           PROFİL BİLGİLERİ (10.2)
           ===================================================================== */

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();   // çerez var ama kullanıcı silinmiş: tekrar giriş
            }

            var model = new ProfileViewModel
            {
                Name = user.Name ?? string.Empty,
                Surname = user.Surname ?? string.Empty,
                Description = user.Description
            };
            FillReadOnly(model, user);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(ProfileViewModel model)
        {
            // Kimin profili? Formdan değil, OTURUMDAN. Formda bir UserId alanı yok.
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (!ModelState.IsValid)
            {
                FillReadOnly(model, user);
                return View(model);
            }

            // Sadece izin verilen üç alan kopyalanır
            user.Name = model.Name.Trim();
            user.Surname = model.Surname.Trim();
            user.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();

            // Context.SaveChanges değil UserManager: Identity doğrulayıcıları çalışır ve ConcurrencyStamp yenilenir
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                FillReadOnly(model, user);
                return View(model);
            }

            // Çerezdeki ad/soyad claim'leri eskidi: oturumu kapatmadan yeniden üret
            await _signInManager.RefreshSignInAsync(user);

            TempData["Toast"] = "Profiliniz güncellendi.";
            TempData["ToastIcon"] = "bi-person-check";

            // Post-Redirect-Get
            return RedirectToAction(nameof(Index));
        }

        /* =====================================================================
           PROFİL FOTOĞRAFI (10.3)
           ===================================================================== */

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPhoto(IFormFile? photo)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            // 1) Dosya var mı, boyutu makul mü? (Kavram 3: dosya boyutu)
            if (photo == null || photo.Length == 0)
            {
                return PhotoError("Bir fotoğraf seçin.");
            }
            if (photo.Length > MaxPhotoBytes)
            {
                return PhotoError("Fotoğraf en fazla 5 MB olabilir.");
            }

            // Dosya adına ve Content-Type'a BAKMIYORUZ: ikisini de istemci yazar (Kavram 1).
            await using var buffer = new MemoryStream();
            await photo.CopyToAsync(buffer);

            // 2) İmza: biçim, dosyanın ilk byte'larından anlaşılır. Resmi açmadan, sadece başlığı okur.
            ImageInfo info;
            try
            {
                buffer.Position = 0;
                info = await Image.IdentifyAsync(buffer);
            }
            catch (ImageFormatException)          // tanınmayan biçim ya da bozuk içerik
            {
                return PhotoError("Dosya bir resim değil ya da bozuk.");
            }

            if (info.Metadata.DecodedImageFormat is not (JpegFormat or PngFormat or WebpFormat))
            {
                return PhotoError("Sadece JPEG, PNG veya WebP yükleyebilirsiniz.");
            }

            // 3) Açılmış boyut: 200 KB'lık bir dosya 50.000 × 50.000 piksel olabilir (Kavram 3)
            if (info.Width > MaxPhotoSide || info.Height > MaxPhotoSide)
            {
                return PhotoError($"Fotoğraf çok büyük (en fazla {MaxPhotoSide} × {MaxPhotoSide} piksel).");
            }
            if (info.FrameCount > 1)
            {
                return PhotoError("Hareketli resimler desteklenmiyor.");
            }

            // 4) Piksellere aç → işle → piksellerden SIFIRDAN yeni bir resim üret (Kavram 2)
            Rgba32[] pixels = new Rgba32[AvatarSize * AvatarSize];
            try
            {
                buffer.Position = 0;
                using var source = await Image.LoadAsync<Rgba32>(buffer);
                source.Mutate(x => x
                    .AutoOrient()                                  // EXIF'teki "yan çekildi" bilgisini piksellere uygula
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(AvatarSize, AvatarSize),
                        Mode = ResizeMode.Crop                     // ortadan kare kırp
                    })
                    .BackgroundColor(Color.White));                // PNG şeffaflığı JPEG'de siyah olmasın
                source.CopyPixelDataTo(pixels);
            }
            catch (ImageFormatException)
            {
                return PhotoError("Fotoğraf okunamadı.");
            }

            // Yeni resmin tek kaynağı piksel dizisi: EXIF, GPS, yorum, gömülü veri... hiçbiri buraya taşınamaz.
            string fileName = $"{Guid.NewGuid():N}.jpg";      // adı SUNUCU üretir (Kavram 1)
            Directory.CreateDirectory(AvatarFolder);
            string newPath = Path.Combine(AvatarFolder, fileName);

            using (var clean = Image.LoadPixelData<Rgba32>(pixels, AvatarSize, AvatarSize))
            {
                await clean.SaveAsJpegAsync(newPath, new JpegEncoder { Quality = 85 });
            }

            // 5) Sıralama (Kavram 4): yeni dosya yazıldı → veritabanı → eski dosya EN SON silinir
            string? oldFile = user.ImageUrl;
            user.ImageUrl = fileName;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                DeleteAvatarFile(fileName);   // kayıt olmadıysa yeni dosya sahipsiz kalmasın
                return PhotoError("Fotoğraf kaydedilemedi.");
            }

            DeleteAvatarFile(oldFile);

            // Navbar fotoğrafı çerezdeki claim'den okur (10.4): claim'leri yeniden üret
            await _signInManager.RefreshSignInAsync(user);

            TempData["Toast"] = "Profil fotoğrafınız güncellendi.";
            TempData["ToastIcon"] = "bi-camera";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePhoto()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            string? oldFile = user.ImageUrl;
            if (oldFile != null)
            {
                user.ImageUrl = null;
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    return PhotoError("Fotoğraf kaldırılamadı.");
                }
                DeleteAvatarFile(oldFile);
                await _signInManager.RefreshSignInAsync(user);   // claim'deki eski dosya adı silinsin (10.4)
            }

            TempData["Toast"] = "Profil fotoğrafınız kaldırıldı.";
            TempData["ToastIcon"] = "bi-person-circle";
            return RedirectToAction(nameof(Index));
        }

        // Fotoğrafı sunan action (karar 3-C). Global filtre sayesinde sadece giriş yapmış kullanıcılar ulaşır.
        // Adres: /Profile/Avatar/{id}  (id = "{guid}.jpg")
        [HttpGet]
        public IActionResult Avatar(string? id)
        {
            // Yol manipülasyonuna karşı: sadece sunucunun ürettiği biçimdeki adlar (örn. "../appsettings.json" eşleşmez)
            if (!AvatarHelper.IsAvatarFile(id))
            {
                return NotFound();
            }

            string path = Path.Combine(AvatarFolder, id);
            if (!System.IO.File.Exists(path))
            {
                return NotFound();
            }

            // Ad her yüklemede değişir (yeni guid): aynı adres hep aynı resmi gösterir → uzun süre önbelleğe alınabilir (Kavram 5)
            Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            Response.Headers.XContentTypeOptions = "nosniff";
            return PhysicalFile(path, "image/jpeg");
        }

        /* ---------- Yardımcılar ---------- */

        // Fotoğraf hatası: küçük ayrı bir form olduğu için hata toast ile gösterilir (PRG)
        private IActionResult PhotoError(string message)
        {
            TempData["Toast"] = message;
            TempData["ToastIcon"] = "bi-exclamation-triangle";
            return RedirectToAction(nameof(Index));
        }

        // Veritabanındaki değer de bir dosya YOLU olarak kullanılmadan önce doğrulanır.
        private void DeleteAvatarFile(string? fileName)
        {
            if (!AvatarHelper.IsAvatarFile(fileName))
            {
                return;
            }

            try
            {
                System.IO.File.Delete(Path.Combine(AvatarFolder, fileName));   // dosya yoksa hata vermez
            }
            catch (IOException ex)
            {
                // Silinemeyen eski dosya kullanıcıyı ilgilendirmez; logla ve devam et
                _logger.LogWarning(ex, "Eski avatar dosyası silinemedi: {FileName}", fileName);
            }
        }

        // Salt okunur alanlar her zaman veritabanındaki kullanıcıdan gelir
        private static void FillReadOnly(ProfileViewModel model, AppUser user)
        {
            string fullName = $"{user.Name} {user.Surname}".Trim();

            model.Email = user.Email ?? string.Empty;
            model.EmailConfirmed = user.EmailConfirmed;
            model.UserName = user.UserName ?? string.Empty;
            model.DisplayName = fullName.Length > 0 ? fullName : model.UserName;
            model.SavedDescription = user.Description;
            model.PhotoFile = AvatarHelper.IsAvatarFile(user.ImageUrl) ? user.ImageUrl : null;
            model.Initials = AvatarHelper.Initials(model.DisplayName);
        }
    }
}