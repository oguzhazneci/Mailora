using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Services
{
    public class AttachmentStorage : IAttachmentStorage
    {
        private readonly string _folder;
        private readonly ILogger<AttachmentStorage> _logger;

        public AttachmentStorage(IWebHostEnvironment env, ILogger<AttachmentStorage> logger)
        {
            // wwwroot DIŞINDA (avatarlarla aynı yaklaşım, 10.3): tarayıcı bu klasöre doğrudan ulaşamaz
            _folder = Path.Combine(env.ContentRootPath, "App_Data", "attachments");
            _logger = logger;
        }

        public async Task<AttachmentSaveResult> SaveAsync(IFormFile? file, DateTime now, CancellationToken ct = default)
        {
            // 1) Boyut: IFormFile.Length dosyayı okumadan bilinir → en ucuz kontrol önce
            if (file == null || file.Length == 0)
            {
                return Fail("Boş dosya eklenemez.");
            }
            if (file.Length > AttachmentRules.MaxFileBytes)
            {
                return Fail($"Bir dosya en fazla {AttachmentRules.MaxFileBytes / (1024 * 1024)} MB olabilir.");
            }

            // 2) İzin listesi: uzantı TEMİZLENMİŞ addan alınır. Tür (ContentType) bizim tablomuzdan gelir.
            string fileName = AttachmentRules.CleanFileName(file.FileName);
            var kind = AttachmentRules.Find(Path.GetExtension(fileName));
            if (kind == null)
            {
                return Fail($"Bu dosya türü eklenemez. İzin verilenler: {AttachmentRules.AllowedList}.");
            }

            // 3) İmza: dosyanın başı, uzantının iddia ettiği türle uyuşmalı
            await using var input = file.OpenReadStream();
            byte[] header = new byte[AttachmentRules.HeaderSize];
            int read = await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);

            if (!kind.Matches(header.AsSpan(0, read)))
            {
                return Fail("Dosyanın içeriği uzantısıyla uyuşmuyor.");
            }

            // 4) Diske yaz: ad SUNUCU tarafından üretilir, uzantı içermez
            string storedName = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(_folder);
            string path = Path.Combine(_folder, storedName);

            try
            {
                // CreateNew: aynı adda dosya varsa ÜZERİNE YAZMAZ, hata verir
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                                                        FileShare.None, bufferSize: 81920, useAsync: true);
                await output.WriteAsync(header.AsMemory(0, read), ct);   // başını zaten okumuştuk
                await input.CopyToAsync(output, ct);                     // kalanı
            }
            catch
            {
                TryDelete(path);   // yarım kalmış dosya diskte kalmasın
                throw;
            }

            return new AttachmentSaveResult(new Attachment
            {
                FileName = fileName,
                StoredName = storedName,
                ContentType = kind.ContentType,
                SizeBytes = file.Length,
                CreatedDate = now
            }, null);
        }

        public string? GetPath(string storedName)
        {
            if (!IsStoredName(storedName))
            {
                return null;
            }

            string path = Path.Combine(_folder, storedName);
            return File.Exists(path) ? path : null;
        }

        public string? Copy(string storedName)
        {
            string? source = GetPath(storedName);
            if (source == null)
            {
                return null;
            }

            string newName = Guid.NewGuid().ToString("N");
            File.Copy(source, Path.Combine(_folder, newName), overwrite: false);
            return newName;
        }

        public void Delete(string storedName)
        {
            // Veritabanından gelen değer de doğrulanmadan YOL olarak kullanılmaz (10.3 DeleteAvatarFile ile aynı ilke)
            if (!IsStoredName(storedName))
            {
                return;
            }
            TryDelete(Path.Combine(_folder, storedName));
        }

        public IReadOnlyList<(string StoredName, DateTime LastWriteUtc)> ListFiles()
        {
            if (!Directory.Exists(_folder))
            {
                return [];   // henüz hiç dosya yüklenmemiş
            }

            // Sadece bizim ürettiğimiz biçimdeki adlar: klasöre elle bırakılmış başka dosyalara dokunulmaz
            return Directory.EnumerateFiles(_folder)
                .Select(path => (Name: Path.GetFileName(path), Path: path))
                .Where(f => IsStoredName(f.Name))
                .Select(f => (f.Name, File.GetLastWriteTimeUtc(f.Path)))
                .ToList();
        }

        /* ---------- Yardımcılar ---------- */

        // Sunucunun ürettiği biçim: tam 32 küçük onaltılık karakter. "../x" gibi bir değer asla eşleşmez.
        private static bool IsStoredName(string? name) =>
            name is { Length: 32 } && name.All(char.IsAsciiHexDigitLower);

        private void TryDelete(string path)
        {
            try
            {
                File.Delete(path);   // dosya yoksa hata vermez
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Ek dosyası silinemedi: {StoredName}", Path.GetFileName(path));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Ek dosyası silinemedi (erişim): {StoredName}", Path.GetFileName(path));
            }
        }

        private static AttachmentSaveResult Fail(string error) => new(null, error);
    }
}