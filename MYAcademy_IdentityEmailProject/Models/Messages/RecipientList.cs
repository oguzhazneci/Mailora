using System.Text.RegularExpressions;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // Kime / Bilgi / Gizli alanlarının ayrıştırılmış hali (12.9).
    // Formdan virgülle (ya da ; boşluk satır sonu) ayrılmış METİN gelir; JS kapalı olsa da çalışır.
    public sealed class RecipientList
    {
        public const int MaxRecipients = 20;          // karar 2-A: üç alanın toplamı
        public const int MaxAddressLength = 256;

        private static readonly char[] Separators = { ',', ';', ' ', '\t', '\r', '\n' };

        // İstemcideki EMAIL_RE ile AYNI kural (8.5'teki uyumsuzluk burada kapandı):
        // boşluk, @, < > ( ) [ ] , ; : " yok; tek @; noktalı bir alan adı.
        private static readonly Regex EmailPattern = new(
            @"^[^\s@<>()\[\],;:""]+@[^\s@<>()\[\],;:""]+\.[A-Za-z]{2,}$",
            RegexOptions.Compiled);

        public List<string> To { get; } = new();
        public List<string> Cc { get; } = new();
        public List<string> Bcc { get; } = new();

        public int Count => To.Count + Cc.Count + Bcc.Count;

        // Bütün alıcılar türleriyle birlikte, öncelik sırasıyla (Kime → Bilgi → Gizli)
        public IEnumerable<(string Email, RecipientType Type)> All =>
            To.Select(e => (e, RecipientType.To))
              .Concat(Cc.Select(e => (e, RecipientType.Cc)))
              .Concat(Bcc.Select(e => (e, RecipientType.Bcc)));

        /* Ayrıştır + TEKİLLEŞTİR (karar 3-A, Kavram 3):
           - Büyük/küçük harf farkı yok sayılır (Umut@... = umut@...)
           - Bir adres birden çok alandaysa ÖNCELİKLİ alanda kalır: Kime > Bilgi > Gizli
             (alanlar bu sırayla işlendiği için "ilk gören alır")
           Tekilleştirme zorunlu: aynı kullanıcıya iki Gelen kopyası benzersiz indeksi ihlal eder (5.3). */
        public static RecipientList Parse(string? to, string? cc, string? bcc)
        {
            var list = new RecipientList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddTo(list.To, to);
            AddTo(list.Cc, cc);
            AddTo(list.Bcc, bcc);
            return list;

            void AddTo(List<string> target, string? raw)
            {
                foreach (string address in Split(raw))
                {
                    if (seen.Add(address))
                    {
                        target.Add(address);
                    }
                }
            }
        }

        public static IEnumerable<string> Split(string? raw) =>
            (raw ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public static bool IsValidAddress(string address) =>
            address.Length <= MaxAddressLength && EmailPattern.IsMatch(address);

        // Formu yeniden doldururken: "a@x.com, b@y.com"
        public static string Join(IEnumerable<string> addresses) => string.Join(", ", addresses);
    }
}