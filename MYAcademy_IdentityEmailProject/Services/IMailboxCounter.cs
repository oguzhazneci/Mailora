using MYAcademy_IdentityEmailProject.Models;
using MYAcademy_IdentityEmailProject.Models.Messages;

namespace MYAcademy_IdentityEmailProject.Services
{
    public interface IMailboxCounter
    {
        Task<MailboxCounts> GetAsync(string userId);
    }
}