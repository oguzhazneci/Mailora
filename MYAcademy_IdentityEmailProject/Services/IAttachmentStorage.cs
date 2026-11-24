using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Services
{
    // Kaydetme sonucu: ya bir Attachment (veritabanına HENÜZ eklenmemiş) ya da kullanıcıya gösterilecek hata
    public sealed record AttachmentSaveResult(Attachment? Attachment, string? Error)
    {
        public bool Succeeded => Attachment != null;
    }

    // Ek dosyalarının DİSK tarafı (12.15). Veritabanına dokunmaz: satırları controller ekler/siler.
    public interface IAttachmentStorage
    {
        // Kuralları uygular (boyut, izin listesi, imza), dosyayı rastgele adla diske yazar.
        Task<AttachmentSaveResult> SaveAsync(IFormFile? file, DateTime now, CancellationToken ct = default);

        // İndirme için tam yol. Ad geçersizse ya da dosya yoksa null.
        string? GetPath(string storedName);

        // İlet (karar 5-A): dosyanın YENİ adla kopyası. Kaynak yoksa null.
        string? Copy(string storedName);

        // Dosyayı siler. Yoksa sessizce geçer; silinemezse loglar.
        void Delete(string storedName);
    }
}