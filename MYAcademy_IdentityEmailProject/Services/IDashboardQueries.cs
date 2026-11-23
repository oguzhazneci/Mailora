using MYAcademy_IdentityEmailProject.Models.Dashboard;

namespace MYAcademy_IdentityEmailProject.Services
{
    // Dashboard sorguları tek yerde (karar 5-A). Controller sadece çağırır.
    public interface IDashboardQueries
    {
        Task<DashboardData> GetAsync(string userId);
    }
}