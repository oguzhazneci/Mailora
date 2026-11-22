namespace MYAcademy_IdentityEmailProject.Identity
{
    // Uygulamaya özel claim türleri (10.4). Standart türler (ClaimTypes.GivenName vb.) ile çakışmasın diye önekli.
    public static class AppClaimTypes
    {
        // Profil fotoğrafının dosya adı ({guid}.jpg). Fotoğraf yoksa claim hiç eklenmez.
        public const string Avatar = "mailora:avatar";
    }
}