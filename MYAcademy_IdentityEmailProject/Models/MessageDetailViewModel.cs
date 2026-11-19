using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Models
{
    public class MessageDetailViewModel
    {
        public MailboxItem Item { get; set; } = null!;     // benim kopyam (yıldız, okundu, klasör)
        public Message Message => Item.Message;            // içerik
        public string SafeBodyHtml { get; set; } = string.Empty;   // temizlenmiş içerik
    }
}