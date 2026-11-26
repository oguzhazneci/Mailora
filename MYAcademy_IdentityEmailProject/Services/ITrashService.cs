using System.Linq.Expressions;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Services
{
    // Çöp kutusu işlemleri (15.3). Controller ("Kalıcı sil", "Çöpü boşalt") ve arka plan servisi (15.4) ortak kullanır.
    public interface ITrashService
    {
        // Filtreye uyan ÇÖPTEKİ kopyaları kalıcı siler; hiç kopyası kalmayan mesajları ve onların ek dosyalarını da.
        // "Çöpte mi?" (DeletedDate != null) koşulunu servis KENDİSİ ekler: çağıran unutsa bile çöpte olmayan silinmez.
        // Dönen değer: silinen kopya sayısı.
        Task<int> DeleteForeverAsync(Expression<Func<MailboxItem, bool>> filter, CancellationToken ct = default);

        // 15.4: Çöpte TrashRules.RetentionDays günden uzun süredir duran kopyaları (bütün kullanıcıların) kalıcı siler.
        Task<int> PurgeExpiredAsync(CancellationToken ct = default);
    }
}