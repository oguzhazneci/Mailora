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

        public async Task SendAsync(string toEmail, string subject, string body, bool isHtml = false)
        {
            var smtpSettings = _configuration.GetSection("SmtpSettings");

            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(new MailboxAddress(smtpSettings["SenderName"], smtpSettings["Username"]));
            mimeMessage.To.Add(MailboxAddress.Parse(toEmail));
            mimeMessage.Subject = subject;

            var bodyBuilder = new BodyBuilder();
            if (isHtml)
                bodyBuilder.HtmlBody = body;
            else
                bodyBuilder.TextBody = body;
            mimeMessage.Body = bodyBuilder.ToMessageBody();

            using var smtpClient = new SmtpClient();
            await smtpClient.ConnectAsync(smtpSettings["Host"], int.Parse(smtpSettings["Port"]!), SecureSocketOptions.StartTls);
            await smtpClient.AuthenticateAsync(smtpSettings["Username"], smtpSettings["Password"]);
            await smtpClient.SendAsync(mimeMessage);
            await smtpClient.DisconnectAsync(true);
        }
    }
}