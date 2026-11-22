namespace MYAcademy_IdentityEmailProject.Models.Layout
{
    public class NavbarUserViewModel
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public List<NotificationItemViewModel> Notifications { get; set; } = new();
        // Profil fotoğrafının dosya adı (claim'den) ya da null — 10.4
        public string? PhotoFile { get; set; }
    }
}