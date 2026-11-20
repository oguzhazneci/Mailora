using System.ComponentModel.DataAnnotations;

namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // Gönderen alanı BİLEREK yok: göndereni sunucu oturumdan alır (overposting, 5.6).
    // Uzunluk sınırları veritabanı kolonlarıyla aynı (5.5).
    public class CreateMessageViewModel
    {
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