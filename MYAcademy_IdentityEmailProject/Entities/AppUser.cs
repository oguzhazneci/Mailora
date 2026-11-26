using Microsoft.AspNetCore.Identity;

namespace MYAcademy_IdentityEmailProject.Entities
{
    public class AppUser:IdentityUser
    {
        public string? Name { get; set; }
        public string? Surname { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int? ValidCode { get; set; }
        // 17.3 (karar 6-A): bu kodla yapılan yanlış deneme sayısı. 5'te kod geçersiz olur.
        public int ValidCodeAttempts { get; set; }
    }
}
