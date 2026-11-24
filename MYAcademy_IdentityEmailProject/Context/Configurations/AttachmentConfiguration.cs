using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Context.Configurations
{
    // Ek tablosu (12.15). Dosyanın kendisi diskte; burada sadece bilgileri.
    public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
    {
        public void Configure(EntityTypeBuilder<Attachment> builder)
        {
            // Kullanıcının gördüğü ad: Türkçe karakter olabilir → nvarchar
            builder.Property(a => a.FileName)
                   .HasMaxLength(255)
                   .IsRequired();

            // Diskteki ad: her zaman 32 onaltılık karakter → char(32)
            builder.Property(a => a.StoredName)
                   .HasMaxLength(32)
                   .IsFixedLength()
                   .IsUnicode(false)
                   .IsRequired();

            // İki satır aynı dosyayı gösteremez: birini silmek diğerinin dosyasını yok ederdi (Kavram 2)
            builder.HasIndex(a => a.StoredName)
                   .IsUnique();

            builder.Property(a => a.ContentType)
                   .HasMaxLength(100)
                   .IsUnicode(false)
                   .IsRequired();

            // Mesaj silinince ek SATIRLARI da silinir. Diskteki dosyalar silinmez → 12.19 (Kavram 1)
            builder.HasOne(a => a.Message)
                   .WithMany(m => m.Attachments)
                   .HasForeignKey(a => a.MessageId)
                   .OnDelete(DeleteBehavior.Cascade);
        }
    }
}