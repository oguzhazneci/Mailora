namespace MYAcademy_IdentityEmailProject.Entities
{
    public class Category
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        // Rozet, sidebar etiketi ve dashboard grafiği için koyu ton (#RRGGBB).
        // Açık arka plan view'da bu renkten türetilir.
        public string ColorCode { get; set; } = "#85899B";

        public List<Message> Messages { get; set; } = new();
    }
}