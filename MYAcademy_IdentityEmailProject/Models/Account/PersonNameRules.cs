namespace MYAcademy_IdentityEmailProject.Models.Account
{
    // Ad ve soyad için TEK kural tanımı: kayıt formu ve profil formu aynı kuralları kullanır (10.2).
    public static class PersonNameRules
    {
        public const int MaxLength = 50;

        // Harfler (Türkçe dahil: \p{L}), birleşik işaretler (\p{M}), boşluk, kesme işareti ve tire.
        // RegularExpression attribute'u değerin TAMAMININ eşleşmesini ister.
        public const string Pattern = @"^[\p{L}\p{M}' \-]+$";

        public const string PatternMessage = "Sadece harf, boşluk, kesme işareti (') ve tire (-) kullanılabilir.";
    }
}