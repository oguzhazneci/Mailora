using System.Collections.Concurrent;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// Gönderilen bir mail. Envelope: maili GERÇEKTEN alanlar. HeaderTo/HeaderCc: mailde GÖRÜNENLER (12.10, Kavram 3).
public sealed record SentMail(IReadOnlyList<string> Envelope, IReadOnlyList<string> HeaderTo, IReadOnlyList<string> HeaderCc,
                              string Subject, string Body, int AttachmentCount);

// Gerçek SMTP yerine: mailleri GÖNDERMEZ, bellekte tutar (14.6.a Kavram 2).
// Bütün testler paylaşır; her test kendi benzersiz adresine giden mailleri süzerek bakar.
public sealed class FakeMailService : IMailService
{
    private readonly ConcurrentQueue<SentMail> _sent = new();   // testler paralel çalışır: iş parçacığı güvenli kuyruk

    public IReadOnlyCollection<SentMail> Sent => _sent.ToArray();

    public Task SendAsync(string toEmail, string subject, string body, bool isHtml = false,
                          string? fromName = null, string? replyToEmail = null)
    {
        _sent.Enqueue(new SentMail([toEmail], [toEmail], [], subject, body, 0));
        return Task.CompletedTask;
    }

    public Task SendToManyAsync(IEnumerable<string> headerTo, IEnumerable<string> headerCc,
                                IEnumerable<string> envelopeRecipients,
                                string subject, string body, bool isHtml,
                                string? fromName = null, string? replyToEmail = null,
                                IReadOnlyList<MailAttachment>? attachments = null)
    {
        // Gerçek servisle aynı davranış: zarf boşsa gönderilecek bir şey yok (12.10)
        var envelope = envelopeRecipients.ToList();
        if (envelope.Count > 0)
        {
            _sent.Enqueue(new SentMail(envelope, headerTo.ToList(), headerCc.ToList(), subject, body, attachments?.Count ?? 0));
        }
        return Task.CompletedTask;
    }
}