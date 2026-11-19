using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Models.Messages;

namespace MYAcademy_IdentityEmailProject.Models.Layout
{
    public class SidebarViewModel
    {
        public MailboxCounts Counts { get; set; } = new();
        public List<Category> Categories { get; set; } = new();
    }
}