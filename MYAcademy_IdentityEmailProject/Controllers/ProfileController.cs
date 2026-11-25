using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Helpers;
using MYAcademy_IdentityEmailProject.Models.Account;
using SkiaSharp;   // 14.8: ImageSharp yerine (Release derlemesi lisans anahtarı istiyordu)

namespace MYAcademy_IdentityEmailProject.Controllers
{
    // [Authorize] yazmaya gerek yok: global AuthorizeFilter (3.2) bütün action'ları korur.
    public class ProfileController : Controller
    {
        /* ---------- Profil fotoğrafı kuralları (10.3) ---------- */
        private const long MaxPhotoBytes = 5 * 1024 * 1024;    // 5 MB (dosya boyutu)
        private const int MaxPhotoSide = 8000;                 // piksel (açılmış boyut: Kavram 3)
        private const int AvatarSize = 256;                    // kaydedilen kare avatar
        private const int AvatarJpegQuality = 85;
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
           PROFİL FOTOĞRAFI (10.3; 14.8'de SkiaSharp'a taşındı)
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

            // 2) İmza: SKCodec sadece başlığı okur, pikselleri açmaz.
            //    Tanımadığı ya da bozuk içerikte istisna fırlatmaz, null döner.
            using var data = SKData.CreateCopy(buffer.ToArray());
            using var codec = SKCodec.Create(data);
            if (codec == null)
            {
                return PhotoError("Dosya bir resim değil ya da bozuk.");
            }

            if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            {
                return PhotoError("Sadece JPEG, PNG veya WebP yükleyebilirsiniz.");
            }

            // 3) Açılmış boyut: 200 KB'lık bir dosya 50.000 × 50.000 piksel olabilir (Kavram 3)
            if (codec.Info.Width > MaxPhotoSide || codec.Info.Height > MaxPhotoSide)
            {
                return PhotoError($"Fotoğraf çok büyük (en fazla {MaxPhotoSide} × {MaxPhotoSide} piksel).");
            }
            if (codec.FrameCount > 1)
            {
                return PhotoError("Hareketli resimler desteklenmiyor.");
            }

            // 4) Piksellere aç → işle → piksellerden SIFIRDAN yeni bir JPEG üret (Kavram 2)
            byte[]? jpeg = RenderAvatar(codec);
            if (jpeg == null)
            {
                return PhotoError("Fotoğraf okunamadı.");
            }

            // Yeni resmin tek kaynağı piksel yüzeyi: EXIF, GPS, yorum, gömülü veri... hiçbiri buraya taşınamaz.
            string fileName = $"{Guid.NewGuid():N}.jpg";      // adı SUNUCU üretir (Kavram 1)
            Directory.CreateDirectory(AvatarFolder);
            string newPath = Path.Combine(AvatarFolder, fileName);
            await System.IO.File.WriteAllBytesAsync(newPath, jpeg);

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

        // 14.8: Pikselleri aç → EXIF yönünü uygula → ortadan kare kırp → 256'ya ölçekle → beyaz zemin → JPEG.
        // Başarısızsa null: çağıran taraf kullanıcıya "okunamadı" der.
        private static byte[]? RenderAvatar(SKCodec codec)
        {
            // Kaynak pikselleri aç. Yarım ya da bozuk dosyada GetPixels "Success" dışında bir sonuç döner: kabul etmiyoruz.
            var sourceInfo = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(sourceInfo);
            if (codec.GetPixels(sourceInfo, bitmap.GetPixels()) != SKCodecResult.Success)
            {
                return null;
            }
            bitmap.SetImmutable();                       // SKImage'a kopyalamadan devredilebilsin
            using var source = SKImage.FromBitmap(bitmap);

            // Yön düzeltildikten sonraki en/boy (90°/270° dönüşlerde yer değiştirir)
            SKMatrix orient = OrientationMatrix(codec.EncodedOrigin, sourceInfo.Width, sourceInfo.Height,
                                                out int width, out int height);

            // Ortadaki en büyük kare, AvatarSize × AvatarSize'a ölçeklenir (ImageSharp'taki ResizeMode.Crop)
            int side = Math.Min(width, height);
            float cropX = (width - side) / 2f;
            float cropY = (height - side) / 2f;
            float scale = (float)AvatarSize / side;

            // Üç dönüşüm tek matriste. PostConcat sırası: önce yön → sonra kareyi başlangıca kaydır → sonra ölçekle
            SKMatrix transform = orient
                .PostConcat(SKMatrix.CreateTranslation(-cropX, -cropY))
                .PostConcat(SKMatrix.CreateScale(scale, scale));

            using var surface = SKSurface.Create(new SKImageInfo(AvatarSize, AvatarSize));
            if (surface == null)
            {
                return null;
            }

            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.White);                // PNG şeffaflığı JPEG'de siyah olmasın
            canvas.SetMatrix(transform);
            // Mipmap: büyük resim çok küçültülürken (ör. 4000 → 256) pürüzlü görünmesin
            canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            // Yüzeyde sadece piksel var: metadata taşınamaz (Kavram 2)
            using var avatar = surface.Snapshot();
            using var encoded = avatar.Encode(SKEncodedImageFormat.Jpeg, AvatarJpegQuality);
            return encoded?.ToArray();
        }

        // 14.8: EXIF yönü (1–8) için dönüşüm matrisi. ImageSharp'taki AutoOrient() karşılığı.
        // Matris, kaydedilmiş (sx, sy) noktasını görüntülenecek (dx, dy) noktasına taşır:
        //   dx = scaleX·sx + skewX·sy + transX
        //   dy = skewY·sx  + scaleY·sy + transY
        // 2–8'in yedisi de Pillow'un EXIF dönüşümleriyle piksel piksel karşılaştırılarak doğrulandı.
        private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h, out int width, out int height)
        {
            bool swapsSides = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                                     or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            width = swapsSides ? h : w;
            height = swapsSides ? w : h;

            return origin switch
            {
                //                                                 scaleX skewX transX  skewY scaleY transY  perspektif
                SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),  // 2: yatay ayna
                SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),  // 3: 180°
                SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),  // 4: dikey ayna
                SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),  // 5: köşegen ayna (transpose)
                SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),  // 6: saat yönünde 90° (telefonlarda en yaygın)
                SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),  // 7: ters köşegen ayna (transverse)
                SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),  // 8: saat yönünün tersine 90°
                _ => SKMatrix.Identity                                 // 1 (TopLeft): dokunma
            };
        }

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