using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Context.Configurations
{
    public class MailboxItemConfiguration : IEntityTypeConfiguration<MailboxItem>
    {
        public void Configure(EntityTypeBuilder<MailboxItem> builder)
        {
            builder.Property(i => i.Folder)
                   .HasConversion<string>()          // veritabanında 'Inbox' / 'Sent' / 'Draft'
                   .HasMaxLength(10);

            builder.HasOne(i => i.Message)
                   .WithMany(m => m.MailboxItems)
                   .HasForeignKey(i => i.MessageId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(i => i.User)
                   .WithMany()
                   .HasForeignKey(i => i.UserId)
                   .OnDelete(DeleteBehavior.Restrict);

            // Bir mesajın aynı kullanıcının aynı klasöründe iki kopyası olamaz.
            builder.HasIndex(i => new { i.MessageId, i.UserId, i.Folder })
                   .IsUnique();

            // Klasör sorgularının hepsi "UserId = ben AND Folder = ..." ile başlar.
            builder.HasIndex(i => new { i.UserId, i.Folder });
        }
    }
}