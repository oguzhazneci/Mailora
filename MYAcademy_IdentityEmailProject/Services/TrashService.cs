using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Services
{
    // 15.3: MessageController'daki kalıcı silme buraya taşındı. Davranış aynı (9.5 Kavram 3 ve 4; 12.19 ek dosyaları).
    // Scoped: EmailContext gibi. Controller'da isteğin scope'u, arka plan servisinde kendi açtığı scope kullanılır.
    public class TrashService : ITrashService
    {
        private readonly EmailContext _context;
        private readonly IAttachmentStorage _attachmentStorage;
        private readonly TimeProvider _time;   // 15.4: "şu an" testte değiştirilebilsin (Kavram 2)

        public TrashService(EmailContext context, IAttachmentStorage attachmentStorage, TimeProvider time)
        {
            _context = context;
            _attachmentStorage = attachmentStorage;
            _time = time;
        }
        public async Task<int> DeleteForeverAsync(Expression<Func<MailboxItem, bool>> filter, CancellationToken ct = default)
        {
            // Sorguyu servis kurar (Kavram 1): aynı DbContext, aynı transaction. Çöp koşulu her zaman ilk koşul.
            IQueryable<MailboxItem> trashedItems = _context.MailboxItems
                .Where(i => i.DeletedDate != null)
                .Where(filter);

            int count;
            List<string> orphanFiles;

            // İki ayrı komut → açık transaction. Commit'e ulaşılmazsa ikisi birden geri alınır.
            await using (var transaction = await _context.Database.BeginTransactionAsync(ct))
            {
                // 1) Çöpteki kopyalar
                count = await trashedItems.ExecuteDeleteAsync(ct);

                // Hiç kopyası kalmayan mesajlar. Sorgu TANIMI: aşağıda iki kez kullanılıyor.
                var orphans = _context.Messages.Where(m => !m.MailboxItems.Any());          // SQL: NOT EXISTS (...)

                // 2) Silinecek mesajların ek dosya adları: SİLMEDEN ÖNCE (cascade satırları götürecek)
                orphanFiles = await orphans
                    .SelectMany(m => m.Attachments)
                    .Select(a => a.StoredName)
                    .ToListAsync(ct);

                // 3) Yetim mesajlar. Alıcı ve ek satırları veritabanındaki ON DELETE CASCADE ile gider.
                await orphans.ExecuteDeleteAsync(ct);

                await transaction.CommitAsync(ct);
            }

            // 4) Dosyalar COMMIT'TEN SONRA. Silinemeyen dosya görünmez kalır → 12.19.b süpürücüsü.
            //    Buraya iptal (ct) uygulanmaz: veritabanı değişikliği yapıldı, dosyalar da gitmeli.
            foreach (string storedName in orphanFiles)
            {
                _attachmentStorage.Delete(storedName);
            }

            return count;
        }
        // 15.4: Kural burada, zamanlama TrashPurger'da (Kavram 1). Testler bu metodu doğrudan çağırır (15.6).
        public Task<int> PurgeExpiredAsync(CancellationToken ct = default)
        {
            // Uygulama DeletedDate'i YEREL saatle yazıyor (15.2) → kesinti de yerel saatle hesaplanır
            DateTime cutoff = TrashRules.Cutoff(_time.GetLocalNow().DateTime);

            // Çöp koşulu (DeletedDate != null) DeleteForeverAsync'te zaten ekleniyor
            return DeleteForeverAsync(i => i.DeletedDate < cutoff, ct);
        }
    }
}