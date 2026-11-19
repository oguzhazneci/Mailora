using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Models
{
    public class SidebarViewModel
    {
        public int UnreadInbox { get; set; }
        public int Drafts { get; set; }
        public int Starred { get; set; }
        public int Trash { get; set; }
        public List<Category> Categories { get; set; } = new();
    }
}