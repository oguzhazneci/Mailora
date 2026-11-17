using System.ComponentModel.DataAnnotations;

namespace MYAcademy_IdentityEmailProject.Models
{
    public class UserLoginViewModel
    {
        [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre alanı zorunludur.")]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}