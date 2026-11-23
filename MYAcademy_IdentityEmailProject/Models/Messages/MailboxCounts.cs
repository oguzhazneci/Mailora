namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // Sidebar rozetleri + bildirim imleci. Her sayfa yüklemesinde ve her POST yanıtında gelir (6.4, 7.8).
    public class MailboxCounts
    {
        public int UnreadInbox { get; set; }
        public int Drafts { get; set; }
        public int Starred { get; set; }
        public int Trash { get; set; }

        // Gelen kopyalarımın en büyük Id'si: "buraya kadarını gördüm" imleci (12.5, Kavram 2)
        public int LatestInboxItemId { get; set; }
    }
}