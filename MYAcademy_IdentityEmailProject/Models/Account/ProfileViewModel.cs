using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MYAcademy_IdentityEmailProject.Models.Account
{
    public class ProfileViewModel
    {
        /* ---------- Formdan gelen alanlar: kullanıcı SADECE bunları değiştirebilir ---------- */

        [Required(ErrorMessage = "Ad zorunludur.")]
        [StringLength(PersonNameRules.MaxLength, ErrorMessage = "Ad en fazla 50 karakter olabilir.")]
        [RegularExpression(PersonNameRules.Pattern, ErrorMessage = PersonNameRules.PatternMessage)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Soyad zorunludur.")]
        [StringLength(PersonNameRules.MaxLength, ErrorMessage = "Soyad en fazla 50 karakter olabilir.")]
        [RegularExpression(PersonNameRules.Pattern, ErrorMessage = PersonNameRules.PatternMessage)]
        public string Surname { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Açıklama en fazla 500 karakter olabilir.")]
        public string? Description { get; set; }

        /* ---------- Salt okunur bilgiler: her zaman veritabanından doldurulur ----------
           [BindNever]: formda bu adlarla bir alan gönderilse bile model binding onları YOK SAYAR (overposting, 3.4). */

        [BindNever] public string Email { get; set; } = string.Empty;
        [BindNever] public bool EmailConfirmed { get; set; }
        public bool TwoFactorEnabled { get; set; }   // 16.2
        [BindNever] public string UserName { get; set; } = string.Empty;

        // Üst karttaki ad ve açıklama KAYITLI değerlerdir; formdaki (belki hatalı) değerler değil.
        [BindNever] public string DisplayName { get; set; } = string.Empty;
        [BindNever] public string Initials { get; set; } = string.Empty;
        [BindNever] public string? SavedDescription { get; set; }

        // Profil fotoğrafının dosya adı ({guid}.jpg) ya da null. Adres view'da Url.Action ile kurulur (10.3).
        [BindNever] public string? PhotoFile { get; set; }
    }
}