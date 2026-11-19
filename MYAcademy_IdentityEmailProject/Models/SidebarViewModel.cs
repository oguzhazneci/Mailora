using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Models
{
    public class SidebarViewModel
    {
        public MailboxCounts Counts { get; set; } = new();
        public List<Category> Categories { get; set; } = new();
    }
}