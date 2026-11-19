namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // Sidebar sayaçları. Hem View Component hem JSON yanıtları bu tipi kullanır.
    public class MailboxCounts
    {
        public int UnreadInbox { get; set; }
        public int Drafts { get; set; }
        public int Starred { get; set; }
        public int Trash { get; set; }
    }
}