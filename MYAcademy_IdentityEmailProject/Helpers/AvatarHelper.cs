using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MYAcademy_IdentityEmailProject.Helpers
{
    // Avatarla ilgili kurallar TEK yerde (10.4): profil, liste, Detay ve navbar aynı kuralları kullanır.
    public static class AvatarHelper
    {
        private static readonly CultureInfo Tr = new("tr-TR");
        private static readonly string[] ColorClasses = { "av-lav", "av-blue", "av-mint", "av-rose", "av-peach", "av-sun" };

        // Sunucunun ürettiği avatar adları: 32 küçük onaltılık karakter + .jpg (10.3, Kavram 5)
        private static readonly Regex FileNamePattern = new(@"^[0-9a-f]{32}\.jpg$", RegexOptions.Compiled);

        // [NotNullWhen(true)]: true dönerse derleyici fileName'in null olmadığını bilir
        public static bool IsAvatarFile([NotNullWhen(true)] string? fileName) =>
            fileName != null && FileNamePattern.IsMatch(fileName);

        // "Ada Yılmaz" → "AY". Türkçe kurallarla büyük harf: "ilker" → "İ" (6.3)
        public static string Initials(string? name)
        {
            var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(p => p[0])).ToUpper(Tr);
        }

        // Aynı kişi (aynı e-posta) her sayfada aynı renkte görünür
        public static string ColorClass(string? key)
        {
            int h = 0;
            foreach (char c in key ?? string.Empty)
            {
                h = (h * 31 + c) & 0x7fffffff;
            }
            return ColorClasses[h % ColorClasses.Length];
        }
    }
}