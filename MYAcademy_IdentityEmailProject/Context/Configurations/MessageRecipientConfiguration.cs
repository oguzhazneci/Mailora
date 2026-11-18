using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Context.Configurations
{
    public class MessageRecipientConfiguration : IEntityTypeConfiguration<MessageRecipient>
    {
        public void Configure(EntityTypeBuilder<MessageRecipient> builder)
        {
            builder.Property(r => r.Email)
                   .HasMaxLength(256);               // Identity'deki e-posta uzunluğuyla aynı

            builder.Property(r => r.RecipientType)
                   .HasConversion<string>()          // veritabanında 'To' / 'Cc' / 'Bcc'
                   .HasMaxLength(10);

            builder.HasOne(r => r.Message)
                   .WithMany(m => m.Recipients)
                   .HasForeignKey(r => r.MessageId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(r => r.User)
                   .WithMany()
                   .HasForeignKey(r => r.UserId)
                   .OnDelete(DeleteBehavior.Restrict);
        }
    }
}