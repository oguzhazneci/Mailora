using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // Formda listelenen bir ek (12.16). Forwarded: iletilen mesajdan gelen, henüz kopyalanmamış ek (12.18)
    public sealed record AttachmentItem(int Id, string Name, long Size, bool Forwarded);

    // Yeni Mesaj formu. Alıcı alanları METİN: "a@x.com, b@y.com" (12.9).
    // Alıcı kuralları (format, tekilleştirme, en fazla 20) attribute ile değil RecipientList ile denetlenir:
    // üç alan BİRLİKTE değerlendirilmeli (toplam sayı, alanlar arası tekrar).
    public class CreateMessageViewModel
    {
        // Düzenlenen taslağın MessageId'si; yeni mesajda null (9.6)
        public int? DraftId { get; set; }

        // Kaba bir üst sınır: 20 adres × 256 karakter + ayırıcılar. Asıl kurallar RecipientList'te.
        [StringLength(6000)] public string? To { get; set; }
        [StringLength(6000)] public string? Cc { get; set; }
        [StringLength(6000)] public string? Bcc { get; set; }

        [Required(ErrorMessage = "Bir kategori seçin.")]
        public int? CategoryId { get; set; }

        [Required(ErrorMessage = "Konu zorunludur.")]
        [StringLength(200, ErrorMessage = "Konu en fazla 200 karakter olabilir.")]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mesaj içeriği boş olamaz.")]
        [StringLength(100_000, ErrorMessage = "Mesaj içeriği çok uzun.")]
        public string Body { get; set; } = string.Empty;

        // Taslağın ekleri (12.16). Formdan GELMEZ: her zaman veritabanından doldurulur (Kavram 1).
        [BindNever]
        public List<AttachmentItem> Attachments { get; set; } = new();

        // İlet (12.18): kaynak mesaj ve iletilmek üzere SEÇİLİ ekleri.
        // İstemciden gelir: sunucu ikisini birlikte doğrular (Kavram 2).
        public int? ForwardOf { get; set; }
        public List<int> ForwardAttachmentIds { get; set; } = new();
    }
}