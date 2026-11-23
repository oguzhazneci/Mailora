namespace MYAcademy_IdentityEmailProject.Services
{
    public interface IMailService
    {
        // Tek alıcılı sistem mailleri (onay kodu, şifre sıfırlama, bildirim — Aşama 2, 4, 10.5)
        // fromName: görünen gönderen adı (adres her zaman SMTP hesabıdır, 8.4)
        // replyToEmail: "Yanıtla" denince yanıtın gideceği adres
        Task SendAsync(string toEmail, string subject, string body, bool isHtml = false,
                       string? fromName = null, string? replyToEmail = null);

        // Çok alıcılı mesaj (12.10): BAŞLIKLAR ile ZARF ayrı.
        // headerTo / headerCc: mailde GÖRÜNEN listeler (Mailora içi adresler dahil, gerçek bir e-postada olduğu gibi)
        // envelopeRecipients: maili GERÇEKTEN alacak adresler (sadece dış alıcılar; Gizli olanlar dahil)
        // Gizli (Bcc) listesi başlıklara HİÇ yazılmaz.
        Task SendToManyAsync(IEnumerable<string> headerTo, IEnumerable<string> headerCc,
                             IEnumerable<string> envelopeRecipients,
                             string subject, string body, bool isHtml,
                             string? fromName = null, string? replyToEmail = null);
    }
}