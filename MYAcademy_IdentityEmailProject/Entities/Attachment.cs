namespace MYAcademy_IdentityEmailProject.Entities
{
    // Bir mesajın eki (12.15). Dosyanın kendisi diskte (App_Data/attachments), burada sadece bilgileri.
    // Ek MESAJA aittir, kopyaya (MailboxItem) değil: 10 alıcıya giden dosya diskte bir kez durur.
    public class Attachment
    {
        public int AttachmentId { get; set; }

        public int MessageId { get; set; }
        public Message Message { get; set; } = null!;

        // Kullanıcının gördüğü ad. Diskte ASLA kullanılmaz (yol manipülasyonu, 12.14 Kavram 1)
        public string FileName { get; set; } = string.Empty;

        // Diskteki ad: Guid "N" biçimi (32 hex karakter), uzantısız. Sunucu üretir, kullanıcı girdisi içermez.
        public string StoredName { get; set; } = string.Empty;

        // İzin listesinden gelen sabit tür. Tarayıcının gönderdiği Content-Type'a güvenilmez.
        public string ContentType { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public DateTime CreatedDate { get; set; }
    }
}