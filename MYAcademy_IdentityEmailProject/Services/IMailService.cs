namespace MYAcademy_IdentityEmailProject.Services
{
    public interface IMailService
    {
        // fromName: görünen gönderen adı (adres her zaman SMTP hesabıdır, Kavram 1)
        // replyToEmail: "Yanıtla" denince yanıtın gideceği adres
        Task SendAsync(string toEmail, string subject, string body, bool isHtml = false,
                       string? fromName = null, string? replyToEmail = null);
    }
}