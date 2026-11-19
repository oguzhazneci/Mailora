namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // 0 = parametre verilmediğinde varsayılan davranış (bilinçli).
    public enum InboxFilter { All, Unread, Starred }
    public enum InboxSort { Newest, Oldest }

    // Adresteki parametreler: /Message/Inbox?q=...&categoryId=3&filter=Unread&sort=Oldest&page=2
    // Geçersiz bir değer gelirse ilgili alan varsayılanında kalır.
    public class InboxQuery
    {
        public string? Q { get; set; }
        public int? CategoryId { get; set; }
        public InboxFilter Filter { get; set; } = InboxFilter.All;
        public InboxSort Sort { get; set; } = InboxSort.Newest;
        public int Page { get; set; } = 1;   // 7.3'te kullanılacak
    }
}