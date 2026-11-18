namespace MYAcademy_IdentityEmailProject.Entities
{
    // Mektubun kendisi: içerik bir kez saklanır.
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

        public List<MessageRecipient> Recipients { get; set; } = new();
        public List<MailboxItem> MailboxItems { get; set; } = new();
    }
}