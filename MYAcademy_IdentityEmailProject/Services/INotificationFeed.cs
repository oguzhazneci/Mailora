using MYAcademy_IdentityEmailProject.Models.Layout;

namespace MYAcademy_IdentityEmailProject.Services
{
    // Zil menüsünün verisi TEK yerde (12.6): navbar ilk çizimde, NotificationsController.Menu yenilemede kullanır.
    public interface INotificationFeed
    {
        Task<List<NotificationItemViewModel>> GetUnreadAsync(string userId, int take = 5);
    }
}