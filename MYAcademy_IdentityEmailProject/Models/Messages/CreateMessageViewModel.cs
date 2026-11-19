namespace MYAcademy_IdentityEmailProject.Models.Messages
{
    // KÖPRÜ (5.6): Form artık entity'ye değil bu modele bağlanıyor (overposting'e karşı).
    // Doğrulama kuralları ve SenderId ataması 8. aşamada eklenecek.
    public class CreateMessageViewModel
    {
        public string? ReceiverEmail { get; set; }
        public int? CategoryId { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
    }
}