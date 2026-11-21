namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // Hangi liste? Kullanıcıdan gelmez; her action kendi görünümünü sabit verir (9.2).
    public enum MailboxView { Inbox, Sent, Starred, Drafts, Trash }

    // Durum çipleri
    public enum MailboxFilter { All, Unread, Starred }

    // Sıralama
    public enum MailboxSort { Newest, Oldest }

    // Adres çubuğundan gelen liste parametreleri (GET formu ve linkler, 7.3–7.4).
    // Tüm klasörler aynı parametreleri kullanır.
    public class MailboxQuery
    {
        public string? Q { get; set; }
        public int? CategoryId { get; set; }
        public MailboxFilter Filter { get; set; } = MailboxFilter.All;
        public MailboxSort Sort { get; set; } = MailboxSort.Newest;
        public int Page { get; set; } = 1;
    }
}