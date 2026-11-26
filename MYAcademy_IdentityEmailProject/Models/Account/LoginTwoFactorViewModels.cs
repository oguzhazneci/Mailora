using System.ComponentModel.DataAnnotations;

namespace MYAcademy_IdentityEmailProject.Models.Account
{
    // 16.4: Girişin ikinci adımı. Kullanıcı adı YOK: kim olduğu ara çerezden gelir (Kavram 1)
    public class LoginWith2faViewModel
    {
        [Required(ErrorMessage = "Uygulamadaki 6 haneli kodu girin.")]
        [RegularExpression(@"^\s*\d{3}\s?\d{3}\s*$", ErrorMessage = "Kod 6 rakamdan oluşmalı.")]
        public string Code { get; set; } = string.Empty;

        public bool RememberMachine { get; set; }   // "Bu cihazı hatırla" (karar 2-A)
        public bool RememberMe { get; set; }        // ilk ekrandaki "Beni hatırla", gizli alanla taşınır (Kavram 2)
    }

    public class LoginWithRecoveryCodeViewModel
    {
        [Required(ErrorMessage = "Kurtarma kodunu girin.")]
        [StringLength(20, ErrorMessage = "Kurtarma kodu geçersiz.")]
        public string RecoveryCode { get; set; } = string.Empty;
    }
}