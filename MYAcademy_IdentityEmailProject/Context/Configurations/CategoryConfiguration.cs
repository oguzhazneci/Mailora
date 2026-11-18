using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Context.Configurations
{
    public class CategoryConfiguration : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            builder.Property(c => c.CategoryName)
                   .HasMaxLength(50);

            builder.Property(c => c.ColorCode)
                   .HasMaxLength(7);           // #RRGGBB

            // Aynı isimde iki kategori olamaz.
            builder.HasIndex(c => c.CategoryName)
                   .IsUnique();

            // Seed: herkesin veritabanında aynı olması gereken sabit veri.
            // Id'ler mevcut kayıtlarla aynı tutuldu (taşınacak mesajlar bunlara bağlı).
            builder.HasData(
                new Category { CategoryId = 1, CategoryName = "İş ve Kariyer", ColorCode = "#6F62C6" },
                new Category { CategoryId = 2, CategoryName = "Kişisel", ColorCode = "#3E9A77" },
                new Category { CategoryId = 3, CategoryName = "Finans", ColorCode = "#C8774A" },
                new Category { CategoryId = 4, CategoryName = "Sosyal ve Etkinlik", ColorCode = "#C2577A" },
                new Category { CategoryId = 5, CategoryName = "Bildirimler", ColorCode = "#4A7CC2" },
                new Category { CategoryId = 6, CategoryName = "Duyurular", ColorCode = "#B88A12" },
                new Category { CategoryId = 7, CategoryName = "Eğitim", ColorCode = "#2E8FA3" },
                new Category { CategoryId = 8, CategoryName = "Alışveriş", ColorCode = "#A855A8" },
                new Category { CategoryId = 9, CategoryName = "Seyahat", ColorCode = "#D35D47" },
                new Category { CategoryId = 10, CategoryName = "Teknoloji", ColorCode = "#5D6075" }
            );
        }
    }
}