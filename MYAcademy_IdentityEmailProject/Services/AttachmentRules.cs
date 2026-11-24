using System.Text;

namespace MYAcademy_IdentityEmailProject.Services
{
    // Ek kuralları tek yerde (12.14 kararları 3-A ve 4-A). Controller, view (accept özniteliği) ve servis bunu okur.
    public static class AttachmentRules
    {
        public const long MaxFileBytes = 10 * 1024 * 1024;    // dosya başı 10 MB
        public const long MaxTotalBytes = 20 * 1024 * 1024;   // mesaj başı toplam 20 MB
        public const int MaxFiles = 10;                       // mesaj başı en fazla 10 dosya
        public const int MaxFileNameLength = 255;             // AttachmentConfiguration ile aynı

        // İmza kontrolü için dosyanın başından okunan byte sayısı. Metin kontrolü bu aralığa bakar.
        public const int HeaderSize = 8192;

        // Span alan bir delegate: Func<ReadOnlySpan<byte>, bool> yerine kendi tipimiz (span'ler genel tip argümanı olamaz)
        public delegate bool SignatureCheck(ReadOnlySpan<byte> header);

        public sealed record FileKind(string ContentType, SignatureCheck Matches);

        // Dosya imzaları (magic bytes). Dizi sabitleri derleyici tarafından programın veri bölümüne gömülür, her çağrıda dizi oluşmaz.
        private static ReadOnlySpan<byte> PngSig => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        private static ReadOnlySpan<byte> JpegSig => [0xFF, 0xD8, 0xFF];

        // ZIP kapsayıcısı: docx / xlsx / pptx de içeride birer ZIP'tir
        private static bool IsZip(ReadOnlySpan<byte> h) => h.StartsWith("PK\x03\x04"u8) || h.StartsWith("PK\x05\x06"u8);

        // İmzası olmayan metin dosyaları: ikili (binary) içerik barındırmamalı. NUL byte metinde olmaz.
        private static bool IsText(ReadOnlySpan<byte> h) => h.IndexOf((byte)0) < 0;

        // İZİN LİSTESİ: uzantı → (bizim belirlediğimiz tür, içerik kontrolü). Burada olmayan uzantı reddedilir.
        private static readonly Dictionary<string, FileKind> Allowed = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new("application/pdf", h => h.StartsWith("%PDF-"u8)),
            [".png"] = new("image/png", h => h.StartsWith(PngSig)),
            [".jpg"] = new("image/jpeg", h => h.StartsWith(JpegSig)),
            [".jpeg"] = new("image/jpeg", h => h.StartsWith(JpegSig)),
            [".gif"] = new("image/gif", h => h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8)),
            [".webp"] = new("image/webp", h => h.Length >= 12 && h.StartsWith("RIFF"u8) && h.Slice(8, 4).SequenceEqual("WEBP"u8)),
            [".txt"] = new("text/plain", IsText),
            [".csv"] = new("text/csv", IsText),
            [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", IsZip),
            [".xlsx"] = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", IsZip),
            [".pptx"] = new("application/vnd.openxmlformats-officedocument.presentationml.presentation", IsZip),
            [".zip"] = new("application/zip", IsZip),
        };

        // Formdaki <input type="file" accept="..."> için: ".pdf,.png,..."
        public static string AcceptAttribute => string.Join(",", Allowed.Keys);

        // Hata mesajlarında gösterilen liste: "pdf, png, ..."
        public static string AllowedList => string.Join(", ", Allowed.Keys.Select(k => k.TrimStart('.')));

        public static FileKind? Find(string extension) =>
            Allowed.TryGetValue(extension, out var kind) ? kind : null;

        // Kullanıcının verdiği adı EKRANDA ve İNDİRMEDE kullanılabilir hale getirir. Diskte hiçbir zaman kullanılmaz.
        public static string CleanFileName(string? raw)
        {
            string name = raw ?? string.Empty;

            // Bazı tarayıcılar tam yolu gönderir ("C:\Users\...\rapor.pdf"). Hem / hem \ ayırıcı sayılır:
            // Linux'ta Path.GetFileName \ karakterini ayırıcı olarak tanımaz.
            int cut = name.LastIndexOfAny(['/', '\\']);
            if (cut >= 0)
            {
                name = name[(cut + 1)..];
            }

            // Kontrol karakterleri ve Windows'ta dosya adında geçersiz olanlar → _
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(char.IsControl(c) || "\"*:<>?|".Contains(c) ? '_' : c);
            }
            name = sb.ToString().Trim().TrimEnd('.');

            string extension = Path.GetExtension(name);
            string stem = Path.GetFileNameWithoutExtension(name);

            if (stem.Length == 0)
            {
                stem = "ek";                       // ".pdf" gibi adsız dosyalar
            }

            // Uzantıyı koruyarak kısalt
            int maxStem = MaxFileNameLength - extension.Length;
            if (stem.Length > maxStem)
            {
                stem = stem[..maxStem].TrimEnd();
            }

            return stem + extension;
        }
    }
}