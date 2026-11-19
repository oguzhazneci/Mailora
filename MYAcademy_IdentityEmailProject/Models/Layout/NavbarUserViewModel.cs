namespace MYAcademy_IdentityEmailProject.Models.Layout
{
    public class NavbarUserViewModel
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public List<NotificationItemViewModel> Notifications { get; set; } = new();
    }
}