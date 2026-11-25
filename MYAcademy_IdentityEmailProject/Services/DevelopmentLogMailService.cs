namespace MYAcademy_IdentityEmailProject.Services
{
    // SMTP'siz geliştirme (13.3): mailler GÖNDERİLMEZ, Output'a / konsola yazılır.
    // Kayıt kodu, şifre yenileme linki ve bildirimler SMTP ayarlamadan denenebilir.
    // SADECE Development'ta kaydedilir (Program.cs). Başka bir ortamda oluşturulursa hata verir (Kavram 2).
    public class DevelopmentLogMailService : IMailService
    {
        private readonly ILogger<DevelopmentLogMailService> _logger;

        public DevelopmentLogMailService(ILogger<DevelopmentLogMailService> logger, IHostEnvironment env)
        {
            // İkinci kilit: Program.cs'teki koşul bir gün bozulsa bile Production'da asla çalışmaz
            if (!env.IsDevelopment())
            {
                throw new InvalidOperationException("DevelopmentLogMailService yalnızca Development ortamında kullanılabilir.");
            }
            _logger = logger;
        }

        public Task SendAsync(string toEmail, string subject, string body, bool isHtml = false,
                              string? fromName = null, string? replyToEmail = null)
        {
            Write([toEmail], subject, body, attachmentCount: 0);
            return Task.CompletedTask;
        }

        public Task SendToManyAsync(IEnumerable<string> headerTo, IEnumerable<string> headerCc,
                                    IEnumerable<string> envelopeRecipients,
                                    string subject, string body, bool isHtml,
                                    string? fromName = null, string? replyToEmail = null,
                                    IReadOnlyList<MailAttachment>? attachments = null)
        {
            // Gerçek serviste olduğu gibi: zarf boşsa gönderilecek bir şey yok (12.10)
            var envelope = envelopeRecipients.ToList();
            if (envelope.Count > 0)
            {
                Write(envelope, subject, body, attachments?.Count ?? 0);
            }
            return Task.CompletedTask;
        }

        // BİLİNÇLİ İSTİSNA: 8.4'teki "loga kişisel veri yazılmaz" kuralı burada geçerli değil,
        // çünkü amaç maili okumak. Bu yüzden servis Production'da hiç oluşturulamıyor.
        private void Write(IReadOnlyCollection<string> to, string subject, string body, int attachmentCount)
        {
            _logger.LogWarning(
                "[GELİŞTİRME MAİLİ — gönderilmedi]\nKime: {To}\nKonu: {Subject}\nEk sayısı: {AttachmentCount}\n{Body}",
                string.Join(", ", to), subject, attachmentCount, body);
        }
    }
}