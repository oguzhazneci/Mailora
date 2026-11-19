using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Models
{
    public class InboxViewModel
    {
        public InboxQuery Query { get; set; } = new();
        public List<MailboxItem> Items { get; set; } = new();
        public List<Category> Categories { get; set; } = new();

        // Çip sayıları: kategori ve arama uygulanmış, durum filtresi uygulanmamış kapsam
        public int AllCount { get; set; }
        public int UnreadCount { get; set; }
        public int StarredCount { get; set; }

        // Sayfalama
        public int Page { get; set; }
        public int TotalPages { get; set; }
        public int TotalItems { get; set; }
        public int PageSize { get; set; }

        // Mevcut parametreleri koruyarak, sadece verileni değiştiren bir adres üretir.
        // - Varsayılan değerler (All, Newest, sayfa 1) adrese yazılmaz.
        // - Sayfa verilmezse 1. sayfaya dönülür (filtre/sıralama değişince).
        public Dictionary<string, string> RouteWith(
            InboxFilter? filter = null,
            InboxSort? sort = null,
            int? page = null,
            bool clearCategory = false,
            bool clearSearch = false)
        {
            var values = new Dictionary<string, string>();

            if (!clearSearch && !string.IsNullOrWhiteSpace(Query.Q))
                values["q"] = Query.Q;

            if (!clearCategory && Query.CategoryId.HasValue)
                values["categoryId"] = Query.CategoryId.Value.ToString();

            var f = filter ?? Query.Filter;
            if (f != InboxFilter.All)
                values["filter"] = f.ToString();

            var s = sort ?? Query.Sort;
            if (s != InboxSort.Newest)
                values["sort"] = s.ToString();

            int p = page ?? 1;
            if (p > 1)
                values["page"] = p.ToString();

            return values;
        }
    }
}