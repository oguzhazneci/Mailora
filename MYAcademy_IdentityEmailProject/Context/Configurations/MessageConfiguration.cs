using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Context.Configurations
{
    public class MessageConfiguration : IEntityTypeConfiguration<Message>
    {
        public void Configure(EntityTypeBuilder<Message> builder)
        {
            builder.Property(m => m.Subject)
                   .HasMaxLength(200);
            // Body: uzunluk sınırı yok → nvarchar(max)

            // Gönderen: hesap silme özelliği yok; kullanıcı silinmek istenirse
            // mesajlarına ne olacağına bilinçli karar verilmeli.
            builder.HasOne(m => m.Sender)
                   .WithMany()                       // AppUser'da koleksiyon yok (5.3)
                   .HasForeignKey(m => m.SenderId)
                   .OnDelete(DeleteBehavior.Restrict);

            // Kategori silinirse mesaj kaybolmaz, kategorisiz kalır.
            builder.HasOne(m => m.Category)
                   .WithMany(c => c.Messages)
                   .HasForeignKey(m => m.CategoryId)
                   .OnDelete(DeleteBehavior.SetNull);
        }
    }
}