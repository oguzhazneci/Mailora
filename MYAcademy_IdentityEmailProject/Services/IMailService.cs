namespace MYAcademy_IdentityEmailProject.Services
{
    public interface IMailService
    {
        Task SendAsync(string toEmail, string subject, string body, bool isHtml = false);
    }
}