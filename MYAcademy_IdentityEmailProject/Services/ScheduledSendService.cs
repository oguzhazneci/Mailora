using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Helpers;
using MYAcademy_IdentityEmailProject.Models.Messages;

namespace MYAcademy_IdentityEmailProject.Services
{
    // 18.5: Zamanı gelen mesajları gönderir. Kural burada, zamanlama ScheduledSender'da (Kavram 4).
    public interface IScheduledSendService
    {
        // Zamanı gelmiş mesajları gönderir. Dönen değer: bu turda gönderilen mesaj sayısı.
        Task<int> SendDueAsync(CancellationToken ct = default);
    }

    public class ScheduledSendService : IScheduledSendService
    {
        private const int BatchSize = 50;   // bir turda en fazla; kalanlar sonraki turda

        private readonly EmailContext _context;
        private readonly IMessageSender _sender;
        private readonly TimeProvider _time;
        private readonly ILogger<ScheduledSendService> _logger;

        public ScheduledSendService(EmailContext context, IMessageSender sender, TimeProvider time, ILogger<ScheduledSendService> logger)
        {
            _context = context;
            _sender = sender;
            _time = time;
            _logger = logger;
        }

        public async Task<int> SendDueAsync(CancellationToken ct = default)
        {
            // Yerel saat: ScheduledFor da yerel saatle yazılıyor (18.1 Kavram 4)
            DateTime now = _time.GetLocalNow().DateTime;

            // Gönderenin bekleyen, çöpte olmayan kopyası + zamanı gelmiş. En eski önce.
            var dueIds = await _context.MailboxItems
                .Where(i => i.Folder == MailFolder.Scheduled
                         && i.DeletedDate == null
                         && i.UserId == i.Message.SenderId
                         && i.Message.SentDate == null
                         && i.Message.ScheduledFor <= now)
                .OrderBy(i => i.Message.ScheduledFor)
                .Select(i => i.MessageId)
                .Take(BatchSize)
                .ToListAsync(ct);

            int sent = 0;
            foreach (int messageId in dueIds)
            {
                ct.ThrowIfCancellationRequested();   // kapanırken YENİ mesaja başlanmaz (Kavram 3)
                try
                {
                    if (await SendOneAsync(messageId, now, ct))
                    {
                        sent++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Sahiplenilmişse mesaj Draft'ta kaldı: kullanıcının Taslaklar'ında görünür (Kavram 2)
                    _logger.LogError(ex, "Zamanlanmış mesaj gönderilemedi; taslak olarak bırakıldı. MessageId: {MessageId}", messageId);
                }
                finally
                {
                    // Bir mesajın (başarılı ya da yarım) izleri sonrakini etkilemesin
                    _context.ChangeTracker.Clear();
                }
            }

            return sent;
        }

        private async Task<bool> SendOneAsync(int messageId, DateTime now, CancellationToken ct)
        {
            // 1) Sahiplen (Kavram 1): Scheduled → Draft, TEK UPDATE.
            //    İptal (18.6) aynı koşulu kullanır: ikisinden yalnızca biri 1 satır değiştirebilir.
            int claimed = await _context.MailboxItems
                .Where(i => i.MessageId == messageId && i.Folder == MailFolder.Scheduled && i.DeletedDate == null)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Folder, MailFolder.Draft), ct);

            if (claimed == 0)
            {
                return false;   // kullanıcı iptal etti, çöpe attı ya da başka bir sunucu aldı
            }

            // 2) Artık sıradan bir taslak. Gönderilmemiş mesajda sadece gönderenin kopyası vardır.
            var draft = await _context.Messages
                .Include(m => m.Recipients)
                .Include(m => m.MailboxItems)
                .SingleAsync(m => m.MessageId == messageId, CancellationToken.None);

            // Alıcılar ham adresler (18.4 Kavram 2): taslak açılırken yapılan dağıtımın aynısı
            var recipients = RecipientList.Parse(
                RecipientList.Join(draft.Recipients.Where(r => r.RecipientType == RecipientType.To).Select(r => r.Email)),
                RecipientList.Join(draft.Recipients.Where(r => r.RecipientType == RecipientType.Cc).Select(r => r.Email)),
                RecipientList.Join(draft.Recipients.Where(r => r.RecipientType == RecipientType.Bcc).Select(r => r.Email)));

            // 3) "Şimdi gönder" ile AYNI yol (18.2). Sahiplenildiyse sonuna kadar: iptal belirteci yok (Kavram 3)
            var outcome = await _sender.SendAsync(draft, recipients, now, CancellationToken.None);

            // 4) Dış alıcılara gidemediyse işaret (karar 4-A): gönderen o an ekranda değil (Kavram 2)
            if (!outcome.ExternalOk)
            {
                draft.ExternalDeliveryFailed = true;
                await _context.SaveChangesAsync(CancellationToken.None);
            }

            return true;
        }
    }
}