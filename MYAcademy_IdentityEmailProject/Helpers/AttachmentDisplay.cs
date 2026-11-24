using System.Globalization;

namespace MYAcademy_IdentityEmailProject.Helpers
{
    // Eklerin ekranda gösterimi (12.17): boyut metni ve ikon.
    // Yeni Mesaj formu ve Detay sayfası AYNI kuralı kullanır (AvatarHelper ile aynı yaklaşım, 10.4).
    public static class AttachmentDisplay
    {
        private static readonly CultureInfo Tr = new("tr-TR");
        private const long MB = 1024 * 1024;

        // "340 KB" / "2,4 MB". CreateMessage.cshtml'deki JS formatSize ile aynı kural.
        public static string Size(long bytes) => bytes < MB
            ? $"{Math.Max(1, (long)Math.Round(bytes / 1024d))} KB"
            : $"{(bytes / (double)MB).ToString("0.#", Tr)} MB";

        // Uzantıya göre Bootstrap Icons sınıfı. Sadece görünüm; tür kontrolü AttachmentRules'ta (Kavram 2).
        public static string Icon(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "bi-file-earmark-pdf",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => "bi-file-earmark-image",
            ".txt" => "bi-file-earmark-text",
            ".csv" or ".xlsx" => "bi-file-earmark-spreadsheet",
            ".docx" => "bi-file-earmark-word",
            ".pptx" => "bi-file-earmark-slides",
            ".zip" => "bi-file-earmark-zip",
            _ => "bi-file-earmark"
        };
    }
}