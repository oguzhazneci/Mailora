using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Context
{
    public class EmailContext : IdentityDbContext<AppUser>
    {
        // Bağlantı ayarları dışarıdan (Program.cs → appsettings.json) gelir.
        public EmailContext(DbContextOptions<EmailContext> options) : base(options)
        {
        }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<MessageRecipient> MessageRecipients { get; set; }
        public DbSet<MailboxItem> MailboxItems { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            // Identity tablolarının (AspNetUsers vb.) yapılandırması. Mutlaka ilk satır.
            base.OnModelCreating(builder);

            // Context/Configurations klasöründeki tüm IEntityTypeConfiguration sınıflarını uygular.
            builder.ApplyConfigurationsFromAssembly(typeof(EmailContext).Assembly);
        }
    }
}