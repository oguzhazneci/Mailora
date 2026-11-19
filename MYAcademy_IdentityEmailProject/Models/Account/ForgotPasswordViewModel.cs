using System.ComponentModel.DataAnnotations;

namespace MYAcademy_IdentityEmailProject.Models.Account
{
    public class ForgotPasswordViewModel
    {
        [Required(ErrorMessage = "E-posta alanı zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
        public string Email { get; set; } = string.Empty;
    }
}