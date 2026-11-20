using MailKit;
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
            var smtpSettings = _configuration.GetSection("SmtpSettings");

            // Ayarlar eksikse (örn. User Secrets hiç girilmemiş) anlaşılır bir hatayla dur.
            string host = smtpSettings["Host"] ?? throw new InvalidOperationException("SmtpSettings:Host tanımlı değil.");
            string username = smtpSettings["Username"] ?? throw new InvalidOperationException("SmtpSettings:Username tanımlı değil (User Secrets).");
            string password = smtpSettings["Password"] ?? throw new InvalidOperationException("SmtpSettings:Password tanımlı değil (User Secrets).");
            int port = int.Parse(smtpSettings["Port"] ?? "587");

            var mimeMessage = new MimeMessage();
            // Görünen ad değişebilir, adres her zaman kimliği doğrulanmış SMTP hesabıdır (SPF/DKIM).
            mimeMessage.From.Add(new MailboxAddress(fromName ?? smtpSettings["SenderName"], username));
            if (!string.IsNullOrWhiteSpace(replyToEmail))
            {
                mimeMessage.ReplyTo.Add(MailboxAddress.Parse(replyToEmail));
            }
            mimeMessage.To.Add(MailboxAddress.Parse(toEmail));
            mimeMessage.Subject = subject;

            var bodyBuilder = new BodyBuilder();
            if (isHtml)
                bodyBuilder.HtmlBody = body;
            else
                bodyBuilder.TextBody = body;
            mimeMessage.Body = bodyBuilder.ToMessageBody();

            using var smtpClient = new SmtpClient();
            await smtpClient.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtpClient.AuthenticateAsync(username, password);
            await smtpClient.SendAsync(mimeMessage);
            await smtpClient.DisconnectAsync(true);
        }
    }
}