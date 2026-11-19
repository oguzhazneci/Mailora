using MYAcademy_IdentityEmailProject.Models;

namespace MYAcademy_IdentityEmailProject.Services
{
    public interface IMailboxCounter
    {
        Task<MailboxCounts> GetAsync(string userId);
    }
}