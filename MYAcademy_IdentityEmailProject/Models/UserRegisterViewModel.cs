using System.ComponentModel.DataAnnotations;

namespace MYAcademy_IdentityEmailProject.Models
{
    public class UserRegisterViewModel
    {
        [Required(ErrorMessage = "Ad alanı zorunludur.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Soyad alanı zorunludur.")]
        public string Surname { get; set; } = string.Empty;

        [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-posta alanı zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre alanı zorunludur.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
        [Compare(nameof(Password), ErrorMessage = "Şifreler eşleşmiyor.")]
        public string PasswordConfirm { get; set; } = string.Empty;
    }
}