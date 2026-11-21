using System.ComponentModel.DataAnnotations;

namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // Yeni Mesaj formu. Buradaki kurallar GÖNDERİM içindir (8.2).
    // Taslak kaydında [Required]/[EmailAddress] devre dışı kalır; sınırlar ValidateDraft'ta (9.6).
    public class CreateMessageViewModel
    {
        // Düzenlenen taslağın MessageId'si; yeni mesajda null.
        // İstemciden gelir: controller sahipliği ve hâlâ taslak olduğunu her seferinde kontrol eder.
        public int? DraftId { get; set; }

        [Required(ErrorMessage = "Alıcı adresi zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
        [StringLength(256, ErrorMessage = "E-posta adresi en fazla 256 karakter olabilir.")]
        public string ReceiverEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Bir kategori seçin.")]
        public int? CategoryId { get; set; }

        [Required(ErrorMessage = "Konu zorunludur.")]
        [StringLength(200, ErrorMessage = "Konu en fazla 200 karakter olabilir.")]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mesaj içeriği boş olamaz.")]
        [StringLength(100_000, ErrorMessage = "Mesaj içeriği çok uzun.")]
        public string Body { get; set; } = string.Empty;
    }
}