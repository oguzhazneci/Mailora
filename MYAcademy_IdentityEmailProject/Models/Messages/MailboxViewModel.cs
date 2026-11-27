using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    public class MailboxViewModel
    {
        public MailboxView View { get; set; } = MailboxView.Inbox;
        public MailboxQuery Query { get; set; } = new();
        public List<MailboxItem> Items { get; set; } = new();
        public List<Category> Categories { get; set; } = new();

        // Bu sayfadaki kayıtlı alıcıların adları: UserId → "Ad Soyad" (9.3)
        public Dictionary<string, string> UserNames { get; set; } = new();

        // Aynı alıcıların profil fotoğrafları: UserId → "{guid}.jpg" (fotoğrafı olmayanlar sözlükte yok) — 10.4
        public Dictionary<string, string> UserPhotos { get; set; } = new();
        // Eki olan mesajların Id'leri (12.17): satırdaki ataç ikonu için. Ekler yüklenmez (Kavram 1).
        public HashSet<int> MessagesWithAttachments { get; set; } = new();

        // Aktif kapsamdaki (kategori + arama) çip sayıları
        public int AllCount { get; set; }
        public int UnreadCount { get; set; }
        public int StarredCount { get; set; }

        // Sayfalama
        public int Page { get; set; }
        public int TotalPages { get; set; }
        public int TotalItems { get; set; }
        public int PageSize { get; set; }

        /* ---------- Görünüm tanımı: klasörler arasındaki farklar TEK yerde (9.1, Kavram 2) ----------
           Yeni bir klasör eklendiğinde switch'lere bir satır eklenir; view dosyası değişmez. */

        public string Title => View switch
        {
            MailboxView.Inbox => "Gelen Kutusu",
            MailboxView.Sent => "Gönderilenler",
            MailboxView.Starred => "Yıldızlı Mesajlar",
            MailboxView.Trash => "Çöp Kutusu",
            MailboxView.Drafts => "Taslaklar",
            MailboxView.Archive => "Arşiv",
            MailboxView.Scheduled => "Zamanlanmış",
            _ => View.ToString()
        };

        public string SearchPlaceholder => View switch
        {
            MailboxView.Inbox or MailboxView.Archive => "Gönderen, konu veya içerik ara",
            MailboxView.Sent or MailboxView.Drafts or MailboxView.Scheduled => "Alıcı, konu veya içerik ara",
            _ => "Kişi, konu veya içerik ara"
        };

        // Boş liste (filtre yokken)
        public string EmptyIcon => View switch
        {
            MailboxView.Sent => "bi-send",
            MailboxView.Starred => "bi-star",
            MailboxView.Trash => "bi-trash3",
            MailboxView.Drafts => "bi-file-earmark-text",
            MailboxView.Archive => "bi-archive",
            MailboxView.Scheduled => "bi-clock",
            _ => "bi-inbox"
        };

        public string EmptyTitle => View switch
        {
            MailboxView.Inbox => "Gelen kutunuz boş",
            MailboxView.Sent => "Henüz mesaj göndermediniz",
            MailboxView.Starred => "Yıldızlı mesajınız yok",
            MailboxView.Trash => "Çöp kutunuz boş",
            MailboxView.Drafts => "Taslağınız yok",
            MailboxView.Archive => "Arşiviniz boş",
            MailboxView.Scheduled => "Zamanlanmış mesajınız yok",
            _ => "Burada mesaj yok"
        };

        public string EmptyText => View switch
        {
            MailboxView.Inbox => "Yeni mesajlar geldiğinde burada listelenecek.",
            MailboxView.Sent => "Gönderdiğiniz mesajlar burada listelenecek.",
            MailboxView.Starred => "Önemli mesajları yıldızlayın; hangi klasörde olurlarsa olsunlar burada toplanır.",
            MailboxView.Trash => "Silinen mesajlar siz kalıcı olarak silene kadar burada tutulur.",
            MailboxView.Archive => "Gelen Kutusu'ndan arşivlediğiniz mesajlar burada saklanır.",
            MailboxView.Drafts => "Yeni Mesaj sayfasında \"Taslak Olarak Kaydet\" dediğiniz mesajlar burada listelenir.",
            MailboxView.Scheduled => "Yeni Mesaj sayfasında \"Zamanla\" ile ileri bir tarihe kurduğunuz mesajlar gönderilene kadar burada bekler.",
            _ => ""
        };

        // Okundu/okunmadı bilgisi anlamlı mı? Gönderilen ve taslak kopyalar hep "okundu" (8.2).
        // false ise Okunmamış çipi, rozeti ve okundu/okunmadı toplu butonları gizlenir.
        public bool TracksReadState => View is not (MailboxView.Sent or MailboxView.Drafts or MailboxView.Scheduled);

        // "Yıldızlı" çipi anlamlı mı? Yıldızlı sayfasında zaten hepsi yıldızlı; taslaklarda yıldız yok (9.1, karar 2-A).
        public bool ShowsStarFilter => View is not (MailboxView.Starred or MailboxView.Drafts or MailboxView.Scheduled);

        // Çöp Kutusu'na özel butonlar: geri al, kalıcı sil, çöpü boşalt (9.5)
        public bool IsTrash => View == MailboxView.Trash;

        // Arşiv butonları (12.2): "Arşivle" sadece Gelen Kutusu'nda (karar 2-A), "Gelen Kutusu'na taşı" sadece Arşiv'de
        public bool CanArchive => View == MailboxView.Inbox;
        public bool IsArchive => View == MailboxView.Archive;

        // Linklerin ve GET formunun gideceği action (MessageController'daki metot adı)
        public string ActionName => View.ToString();

        // Sidebar'da vurgulanacak menü (sidebar'daki data-page değerleri: inbox, sent, drafts, starred, trash)
        public string ActiveMenu => View.ToString().ToLowerInvariant();

        /* ---------- Linkler: mevcut parametreleri koruyup sadece istenen değeri değiştirir (7.4) ----------
           Varsayılan değerler adrese yazılmaz: /Message/Inbox?filter=All yerine /Message/Inbox.
           Filtre, sıralama veya kapsam değişince sayfa 1'e döner (eski sayfa yeni listede olmayabilir). */
        public Dictionary<string, string> RouteWith(
            MailboxFilter? filter = null,
            MailboxSort? sort = null,
            int? page = null,
            bool clearCategory = false,
            bool clearSearch = false)
        {
            var route = new Dictionary<string, string>();

            if (!clearSearch && !string.IsNullOrWhiteSpace(Query.Q))
                route["q"] = Query.Q.Trim();

            if (!clearCategory && Query.CategoryId.HasValue)
                route["categoryId"] = Query.CategoryId.Value.ToString();

            var f = filter ?? Query.Filter;
            if (f != MailboxFilter.All)
                route["filter"] = f.ToString();

            var s = sort ?? Query.Sort;
            if (s != MailboxSort.Newest)
                route["sort"] = s.ToString();

            int p = page ?? 1;
            if (p > 1)
                route["page"] = p.ToString();

            return route;
        }
    }
}