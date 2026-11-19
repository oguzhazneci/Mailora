namespace MYAcademy_IdentityEmailProject.Models
{
    public class NotificationItemViewModel
    {
        public int MessageId { get; set; }
        public string SenderDisplayName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string RelativeTime { get; set; } = string.Empty;
    }
}