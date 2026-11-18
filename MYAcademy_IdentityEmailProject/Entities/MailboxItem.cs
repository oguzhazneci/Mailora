namespace MYAcademy_IdentityEmailProject.Entities
{
    // Bir kullanıcının posta kutusundaki kopya: kişiye özel durum burada.
    public class MailboxItem
    {
        public int MailboxItemId { get; set; }

        public int MessageId { get; set; }
        public Message Message { get; set; } = null!;

        public string UserId { get; set; } = null!;
        public AppUser User { get; set; } = null!;

        public MailFolder Folder { get; set; }
        public bool IsRead { get; set; }
        public bool IsStarred { get; set; }
        public DateTime? DeletedDate { get; set; }   // null = çöpte değil
    }
}