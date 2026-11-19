using System.ComponentModel.DataAnnotations;

namespace MYAcademy_IdentityEmailProject.Models.Account
{
    public class ConfirmEmailViewModel
    {
        [Required]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Doğrulama kodunu girin.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Doğrulama kodu 6 haneli bir sayı olmalıdır.")]
        public string Code { get; set; } = string.Empty;
    }
}