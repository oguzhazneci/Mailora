using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    public class MessageDetailViewModel
    {
        public MailboxItem Item { get; set; } = null!;     // benim kopyam (yıldız, okundu, klasör)
        public Message Message => Item.Message;            // içerik
        public string SafeBodyHtml { get; set; } = string.Empty;   // temizlenmiş içerik
        // Mesajın ekleri (12.17): sadece gösterim bilgileri, StoredName yok (Kavram 3)
        public List<AttachmentItem> Attachments { get; set; } = new();
    }
}