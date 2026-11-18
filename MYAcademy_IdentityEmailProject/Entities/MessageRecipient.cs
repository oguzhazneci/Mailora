namespace MYAcademy_IdentityEmailProject.Entities
{
    // Zarfın üstündeki adres: alıcı kayıtlı olmasa da adresi burada durur.
    public class MessageRecipient
    {
        public int MessageRecipientId { get; set; }

        public int MessageId { get; set; }
        public Message Message { get; set; } = null!;

        public string Email { get; set; } = string.Empty;

        // Alıcı Mailora'da kayıtlıysa dolu, dış adresse null.
        public string? UserId { get; set; }
        public AppUser? User { get; set; }

        public RecipientType RecipientType { get; set; } = RecipientType.To;
    }
}