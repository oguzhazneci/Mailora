using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Helpers;
using MYAcademy_IdentityEmailProject.Models.Messages;
using System.Net;

namespace MYAcademy_IdentityEmailProject.Services
{
    // Gönderimin sonucu (Kavram 3): controller toast'a, arka plan servisi uyarı işaretine çevirir.
    public sealed record SendOutcome(
        IReadOnlyList<string> InternalNames,   // Mailora alıcılarının görünen adları
        IReadOnlyList<string> External,        // dış alıcıların adresleri
        bool ExternalOk,                       // dış alıcı yoksa da true
        int RecipientCount);

    public interface IMessageSender
    {
        // Gönderenin TASLAĞINI gönderir (Kavram 1). Taslak, gönderenin Draft kopyasıyla birlikte bellekte olmalı:
        // yeni (NewDraft) ya da veritabanından yüklenmiş. Konu, gövde ve kategori çağıran tarafından doldurulmuş olmalı.
        Task<SendOutcome> SendAsync(Message draft, RecipientList recipients, DateTime now, CancellationToken ct = default);
    }

    // 18.2: MessageController.SendMessageAsync'in çekirdeği (12.10). Controller ve 18.5'teki arka plan servisi kullanır.
    public class MessageSender : IMessageSender
    {
        private readonly EmailContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly IMailService _mailService;
        private readonly IAttachmentStorage _attachmentStorage;
        private readonly ILogger<MessageSender> _logger;

        public MessageSender(EmailContext context, UserManager<AppUser> userManager, IMailService mailService,
                             IAttachmentStorage attachmentStorage, ILogger<MessageSender> logger)
        {
            _context = context;
            _userManager = userManager;
            _mailService = mailService;
            _attachmentStorage = attachmentStorage;
            _logger = logger;
        }

        public async Task<SendOutcome> SendAsync(Message draft, RecipientList recipients, DateTime now, CancellationToken ct = default)
        {
            // 1) Kayıtlı alıcılar TEK sorguda (N+1'e karşı, 12.10). Identity e-postaları NormalizedEmail'de büyük harfle tutar.
            var normalized = recipients.All.Select(r => _userManager.NormalizeEmail(r.Email)).ToList();
            var registered = await _userManager.Users
                .Where(u => normalized.Contains(u.NormalizedEmail!))
                .Select(u => new { u.Id, u.Email, u.NormalizedEmail, u.Name, u.Surname })
                .ToDictionaryAsync(u => u.NormalizedEmail!, ct);

            // 2) Taslak → gönderilmiş mesaj: Id aynı, gönderenin kopyası Draft'tan Sent'e geçer (9.6; Kavram 1)
            draft.MailboxItems.Single(i => i.UserId == draft.SenderId && i.Folder is MailFolder.Draft or MailFolder.Scheduled)
                .Folder = MailFolder.Sent;
            draft.Recipients.Clear();   // taslaktaki ham alıcı satırları; aşağıda eşleştirilmiş halleriyle yeniden yazılır
            draft.UpdatedDate = now;
            draft.SentDate = now;

            // 3) Her alıcı için bir alıcı satırı (türüyle); her KAYITLI alıcı için bir Gelen kopyası
            var external = new List<string>();
            var internalNames = new List<string>();

            foreach (var (email, type) in recipients.All)
            {
                if (registered.TryGetValue(_userManager.NormalizeEmail(email), out var user))
                {
                    draft.Recipients.Add(new MessageRecipient { Email = user.Email!, UserId = user.Id, RecipientType = type });
                    draft.MailboxItems.Add(new MailboxItem { UserId = user.Id, Folder = MailFolder.Inbox, IsRead = false });

                    string fullName = $"{user.Name} {user.Surname}".Trim();
                    internalNames.Add(fullName.Length > 0 ? fullName : user.Email!);
                }
                else
                {
                    draft.Recipients.Add(new MessageRecipient { Email = email, UserId = null, RecipientType = type });
                    external.Add(email);
                }
            }

            // Hepsi tek SaveChanges = tek transaction (5.4)
            await _context.SaveChangesAsync(ct);

            // 4) Dış alıcılar: kayıttan SONRA, tek SMTP mailiyle (8.1: geri alınamaz işlem en sonda)
            bool externalOk = external.Count == 0 || await TrySendExternalAsync(draft, recipients, external, ct);

            return new SendOutcome(internalNames, external, externalOk, recipients.Count);
        }

        // Dış alıcılara TEK SMTP maili (12.10). Başlıkta Kime/Bilgi listeleri TAM, Gizli YOK; zarfta sadece dış adresler.
        // Başarısızlık kaydı geri almaz; sadece false döner (8.1).
        private async Task<bool> TrySendExternalAsync(Message message, RecipientList recipients, List<string> externalEnvelope, CancellationToken ct)
        {
            // Gönderen: oturumdan değil veritabanından (Kavram 2)
            var sender = await _context.Users
                .Where(u => u.Id == message.SenderId)
                .Select(u => new { u.Name, u.Surname, u.Email, u.UserName })
                .SingleAsync(ct);
            string fullName = $"{sender.Name} {sender.Surname}".Trim();
            string senderName = fullName.Length > 0 ? fullName : (sender.UserName ?? "Bir Mailora kullanıcısı");

            // Ekler (12.18): satırı olup dosyası olmayan ek atlanır ve loglanır
            var attachments = new List<MailAttachment>();
            var rows = await _context.Attachments
                .Where(a => a.MessageId == message.MessageId)
                .OrderBy(a => a.AttachmentId)
                .Select(a => new { a.AttachmentId, a.FileName, a.ContentType, a.StoredName })
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                string? path = _attachmentStorage.GetPath(row.StoredName);
                if (path == null)
                {
                    _logger.LogWarning("SMTP eki diskte bulunamadı. AttachmentId: {AttachmentId}", row.AttachmentId);
                    continue;
                }
                attachments.Add(new MailAttachment(row.FileName, row.ContentType, path));
            }

            string html = message.Body +
                "<hr style=\"border:none;border-top:1px solid #ddd;margin:24px 0 12px\">" +
                "<p style=\"color:#888;font-size:12px\">Bu e-posta <b>" + WebUtility.HtmlEncode(senderName) +
                "</b> tarafından Mailora üzerinden gönderildi. Yanıtınız doğrudan göndericiye iletilir.</p>";

            try
            {
                await _mailService.SendToManyAsync(
                    headerTo: recipients.To,
                    headerCc: recipients.Cc,
                    envelopeRecipients: externalEnvelope,
                    subject: message.Subject,
                    body: html,
                    isHtml: true,
                    fromName: $"{senderName} (Mailora)",
                    replyToEmail: sender.Email,
                    attachments: attachments);
                return true;
            }
            catch (Exception ex)
            {
                // Loga kişisel veri (alıcı adresleri) değil, sadece Id ve sayı (8.4)
                _logger.LogError(ex, "Dış adreslere gönderim başarısız. MessageId: {MessageId}, Alıcı sayısı: {Count}",
                                 message.MessageId, externalEnvelope.Count);
                return false;
            }
        }
    }
}