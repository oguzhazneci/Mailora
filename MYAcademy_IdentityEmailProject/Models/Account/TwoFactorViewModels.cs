using System.ComponentModel.DataAnnotations;

namespace MYAcademy_IdentityEmailProject.Models.Account
{
    // 16: İki adımlı doğrulama sayfalarının modelleri
    public class TwoFactorIndexViewModel
    {
        public bool Enabled { get; set; }
        public int RecoveryCodesLeft { get; set; }
    }

    public class TwoFactorSetupViewModel
    {
        public string SharedKey { get; set; } = string.Empty;       // elle girmek için (QR okuyamayanlar)
        public string QrCodeDataUri { get; set; } = string.Empty;   // sunucuda üretilen QR (karar 1-A)

        // 16.3'te doğrulanacak. Boşluklu yazılabilir ("123 456"): uygulamalar kodu öyle gösteriyor
        [Required(ErrorMessage = "Uygulamadaki 6 haneli kodu girin.")]
        [RegularExpression(@"^\s*\d{3}\s?\d{3}\s*$", ErrorMessage = "Kod 6 rakamdan oluşmalı.")]
        public string Code { get; set; } = string.Empty;
    }
}