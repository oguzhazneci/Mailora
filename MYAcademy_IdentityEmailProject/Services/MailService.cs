using MailKit;                // MailKit 5.x'te SendAsync bir extension metodu ve bu namespace'te
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MYAcademy_IdentityEmailProject.Services
{
    public class MailService : IMailService
    {
        private readonly IConfiguration _configuration;

        public MailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendAsync(string toEmail, string subject, string body, bool isHtml = false,
                                    string? fromName = null, string? replyToEmail = null)
        {
            var settings = ReadSettings();
            var mimeMessage = CreateMessage(settings, subject, body, isHtml, fromName, replyToEmail);
            mimeMessage.To.Add(MailboxAddress.Parse(toEmail));

            using var smtpClient = await ConnectAsync(settings);
            await smtpClient.SendAsync(mimeMessage);
            await smtpClient.DisconnectAsync(true);
        }

        public async Task SendToManyAsync(IEnumerable<string> headerTo, IEnumerable<string> headerCc,
                                          IEnumerable<string> envelopeRecipients,
                                          string subject, string body, bool isHtml,
                                          string? fromName = null, string? replyToEmail = null)
        {
            var envelope = envelopeRecipients.Select(a => MailboxAddress.Parse(a)).ToList();
            if (envelope.Count == 0)
            {
                return;   // gönderilecek dış alıcı yok
            }

            var settings = ReadSettings();
            var mimeMessage = CreateMessage(settings, subject, body, isHtml, fromName, replyToEmail);

            // GÖRÜNEN başlıklar (12.10, karar 5-A)
            foreach (string address in headerTo) mimeMessage.To.Add(MailboxAddress.Parse(address));
            foreach (string address in headerCc) mimeMessage.Cc.Add(MailboxAddress.Parse(address));

            // Kime ve Bilgi boşsa (sadece Gizli alıcı varsa) standart boş grup: "undisclosed-recipients:;"
            if (mimeMessage.To.Count == 0 && mimeMessage.Cc.Count == 0)
            {
                mimeMessage.To.Add(new GroupAddress("undisclosed-recipients"));
            }

            // ZARF: SMTP sunucusuna "bu maili şu adreslere teslim et" denen liste.
            // Başlıklardan BAĞIMSIZ verildiği için:
            //  - Mailora içi adresler başlıkta görünür ama onlara SMTP maili GİTMEZ (zaten Gelen kopyaları var)
            //  - Gizli alıcılar zarfta var, başlıkta YOK → kimse onları göremez
            var sender = new MailboxAddress(fromName ?? settings.SenderName, settings.Username);

            using var smtpClient = await ConnectAsync(settings);
            await smtpClient.SendAsync(mimeMessage, sender, envelope);
            await smtpClient.DisconnectAsync(true);
        }

        /* ---------- Yardımcılar ---------- */

        private sealed record SmtpSettings(string Host, int Port, string Username, string Password, string? SenderName);

        private SmtpSettings ReadSettings()
        {
            var section = _configuration.GetSection("SmtpSettings");

            // Ayarlar eksikse (örn. User Secrets hiç girilmemiş) anlaşılır bir hatayla dur (8.4)
            string host = section["Host"] ?? throw new InvalidOperationException("SmtpSettings:Host tanımlı değil.");
            string username = section["Username"] ?? throw new InvalidOperationException("SmtpSettings:Username tanımlı değil (User Secrets).");
            string password = section["Password"] ?? throw new InvalidOperationException("SmtpSettings:Password tanımlı değil (User Secrets).");
            int port = int.Parse(section["Port"] ?? "587");

            return new SmtpSettings(host, port, username, password, section["SenderName"]);
        }

        private static MimeMessage CreateMessage(SmtpSettings settings, string subject, string body, bool isHtml,
                                                 string? fromName, string? replyToEmail)
        {
            var mimeMessage = new MimeMessage();
            // Görünen ad değişebilir, adres her zaman kimliği doğrulanmış SMTP hesabıdır (SPF/DKIM, 8.4)
            mimeMessage.From.Add(new MailboxAddress(fromName ?? settings.SenderName, settings.Username));
            if (!string.IsNullOrWhiteSpace(replyToEmail))
            {
                mimeMessage.ReplyTo.Add(MailboxAddress.Parse(replyToEmail));
            }
            mimeMessage.Subject = subject;

            var bodyBuilder = new BodyBuilder();
            if (isHtml)
                bodyBuilder.HtmlBody = body;
            else
                bodyBuilder.TextBody = body;
            mimeMessage.Body = bodyBuilder.ToMessageBody();

            return mimeMessage;
        }

        private static async Task<SmtpClient> ConnectAsync(SmtpSettings settings)
        {
            var smtpClient = new SmtpClient();
            try
            {
                await smtpClient.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.StartTls);
                await smtpClient.AuthenticateAsync(settings.Username, settings.Password);
                return smtpClient;
            }
            catch
            {
                smtpClient.Dispose();   // bağlantı kurulamadıysa nesne sızmasın
                throw;
            }
        }
    }
}