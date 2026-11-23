namespace MYAcademy_IdentityEmailProject.Models.Dashboard
{
    // Dashboard'un ihtiyaç duyduğu bütün veri (11.2). View'a doğrudan bu model gider (11.3).
    public class DashboardData
    {
        public DashboardStats Stats { get; set; } = new();

        // Son 30 gün, BOŞ GÜNLER DAHİL (Kavram 3): her zaman tam 30 eleman, eskiden yeniye
        public List<DailyActivity> Daily { get; set; } = new();

        // Kategori dağılımı (karar 4-A), çoktan aza
        public List<CategoryShare> Categories { get; set; } = new();
        public int CategoryTotal { get; set; }

        public List<RecentMessage> Recent { get; set; } = new();
        public List<TopContact> Contacts { get; set; } = new();
    }

    // DURUM sayıları: kutunun ŞU ANKİ hali (çöptekiler hariç) — Kavram 1
    public class DashboardStats
    {
        public int InboxTotal { get; set; }
        public int InboxLast7 { get; set; }        // son 7 günde gelen (karar 1-A: sayı olarak)
        public int SentTotal { get; set; }
        public int SentLast7 { get; set; }
        public int Unread { get; set; }
        public int UnreadLast7 { get; set; }
        public int Starred { get; set; }           // yıldızlanma ZAMANI tutulmuyor → "son 7 gün" yok (Kavram 4)
        public int Drafts { get; set; }
    }

    // OLAY sayıları: o gün ne oldu (sonradan çöpe atılsa bile) — Kavram 1
    public class DailyActivity
    {
        public DateTime Date { get; set; }
        public int Received { get; set; }
        public int Sent { get; set; }
    }

    public class CategoryShare
    {
        public int? CategoryId { get; set; }       // null = kategorisiz (kategori silinmiş: SetNull, 5.3)
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#85899B";
        public int Count { get; set; }
    }

    public class RecentMessage
    {
        public int MailboxItemId { get; set; }
        public int MessageId { get; set; }
        public bool IsRead { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public DateTime? SentDate { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public string? SenderEmail { get; set; }
        public string? SenderPhoto { get; set; }
        public string? CategoryName { get; set; }
        public string? CategoryColor { get; set; }
    }

    public class TopContact
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Photo { get; set; }
        public int Received { get; set; }          // ondan bana
        public int Sent { get; set; }              // benden ona
        public int Total => Received + Sent;
    }
}