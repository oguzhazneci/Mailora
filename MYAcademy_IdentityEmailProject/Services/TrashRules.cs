namespace MYAcademy_IdentityEmailProject.Services
{
    // Çöp kutusu kuralları (15.4, 15.5). Servis ve görünüm aynı hesabı kullanır: rozet ile silme birbirini tutar.
    public static class TrashRules
    {
        public const int RetentionDays = 30;   // çöpe taşındıktan bu kadar gün sonra kalıcı silinir
        public const int WarningDays = 3;      // son bu kadar günde satırda uyarı rozeti (15.5, karar 1-B)

        // Bu andan ÖNCE çöpe taşınmış kopyaların süresi dolmuştur (servis kullanır)
        public static DateTime Cutoff(DateTime now) => now.AddDays(-RetentionDays);

        // Bir kopyanın süresinin dolduğu an (görünüm kullanır). Silme, bundan sonraki ilk turda (en fazla 6 saat).
        public static DateTime ExpiresAt(DateTime deletedDate) => deletedDate.AddDays(RetentionDays);
    }
}