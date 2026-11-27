using Microsoft.EntityFrameworkCore;

namespace MYAcademy_IdentityEmailProject.Entities
{
    // Mektubun kendisi: içerik bir kez saklanır.
    // 18.3: Arka plan servisi "zamanı gelenleri" ScheduledFor'a göre arar: indeks
    [Index(nameof(ScheduledFor))]
    public class Message
    {
        public int MessageId { get; set; }

        // Gönderen her zaman kayıtlı bir kullanıcıdır.
        public string SenderId { get; set; } = null!;
        public AppUser Sender { get; set; } = null!;

        // Taslakta boş olabilir; gönderim anında zorunlu tutulur.
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;

        // Taslakta seçilmemiş olabilir; gönderim anında zorunlu tutulur.
        public int? CategoryId { get; set; }
        public Category? Category { get; set; }

        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
        public DateTime? SentDate { get; set; }   // null = taslak

        // 18.3: İleri tarihli gönderim (yerel saat, 15.2). null = zamanlanmamış.
        // Gönderenin kopyası bu sürede MailFolder.Scheduled'da durur (Kavram 1).
        public DateTime? ScheduledFor { get; set; }

        // 18.3 (karar 4-A): zamanlanmış gönderimde dış alıcılara SMTP başarısız oldu.
        // Gönderen o an ekranda değil; uyarı mesajın üzerinde kalır.
        public bool ExternalDeliveryFailed { get; set; }

        public List<MessageRecipient> Recipients { get; set; } = new();
        public List<MailboxItem> MailboxItems { get; set; } = new();
        public List<Attachment> Attachments { get; set; } = new();   // 12.15: ekler mesaja aittir
    }
}