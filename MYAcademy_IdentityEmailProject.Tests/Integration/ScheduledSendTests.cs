using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// 18.7: Zamanlanmış gönderim. Saat donmuş (15.6): zamanlama factory.Clock'a göre, "zamanı geldi" ise VERİNİN tarihiyle.
// SendDueAsync BÜTÜN kullanıcıların mesajlarına bakar ve testler paralel: sayıya değil kendi mesajımızın durumuna bakılır (15.6.b).
public class ScheduledSendTests(MailoraAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private DateTime Now => factory.Clock.GetLocalNow().DateTime;

    /* ---------- Zamanlama (18.3–18.4) ---------- */

    [Fact]
    public async Task Schedule_WaitsInScheduledFolder_WithoutRecipientCopies()
    {
        var sender = await factory.SignInNewUserAsync("sch");
        var recipient = await factory.CreateUserAsync("rcpt");

        int id = await ScheduleAsync(sender, recipient.Email, Now.AddDays(1));

        Assert.Equal(MailFolder.Scheduled, await SenderFolderAsync(id, sender.User));
        Assert.Equal(0, await InboxCopiesAsync(id));   // alıcı henüz hiçbir şey görmüyor
        Assert.Null(await SentDateAsync(id));
    }

    [Fact]
    public async Task Schedule_IsListedUnderScheduled_NotDrafts()
    {
        var sender = await factory.SignInNewUserAsync("sch");
        var recipient = await factory.CreateUserAsync("rcpt");
        int id = await ScheduleAsync(sender, recipient.Email, Now.AddDays(1));
        int itemId = await SenderItemIdAsync(id, sender.User);

        Assert.Contains($"data-id=\"{itemId}\"", await sender.GetStringAsync("/Message/Scheduled"));
        Assert.DoesNotContain($"data-id=\"{itemId}\"", await sender.GetStringAsync("/Message/Drafts"));
    }

    [Fact]
    public async Task Schedule_InThePast_IsRejected()
    {
        var sender = await factory.SignInNewUserAsync("sch");
        var recipient = await factory.CreateUserAsync("rcpt");
        string subject = $"Gecmis-{Guid.NewGuid():N}";

        var response = await PostScheduleAsync(sender, recipient.Email, subject, Now.AddHours(-1));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // form, hata mesajıyla
        Assert.False(await factory.QueryDbAsync(db => db.Messages.AnyAsync(m => m.Subject == subject, Ct)));
    }

    /* ---------- Gönderim (18.5) ---------- */

    [Fact]
    public async Task SendDue_SendsMessagesWhoseTimeHasCome()
    {
        var sender = await factory.SignInNewUserAsync("sch");
        var recipient = await factory.CreateUserAsync("rcpt");
        int id = await ScheduleAsync(sender, recipient.Email, Now.AddDays(1));
        await MakeDueAsync(id);

        await SendDueAsync();

        Assert.Equal(MailFolder.Sent, await SenderFolderAsync(id, sender.User));
        Assert.Equal(1, await InboxCopiesAsync(id));
        Assert.NotNull(await SentDateAsync(id));
    }

    [Fact]
    public async Task SendDue_LeavesFutureMessagesAlone()
    {
        var sender = await factory.SignInNewUserAsync("sch");
        var recipient = await factory.CreateUserAsync("rcpt");
        int id = await ScheduleAsync(sender, recipient.Email, Now.AddDays(1));

        await SendDueAsync();

        Assert.Equal(MailFolder.Scheduled, await SenderFolderAsync(id, sender.User));
        Assert.Equal(0, await InboxCopiesAsync(id));
    }

    [Fact]
    public async Task SendDue_SkipsTrashedScheduledMessages()
    {
        var sender = await factory.SignInNewUserAsync("sch");
        var recipient = await factory.CreateUserAsync("rcpt");
        int id = await ScheduleAsync(sender, recipient.Email, Now.AddDays(1));
        await sender.PostFormAsync("/Message/BulkMoveToTrash", ("ids", (await SenderItemIdAsync(id, sender.User)).ToString()));
        await MakeDueAsync(id);

        await SendDueAsync();

        Assert.Equal(0, await InboxCopiesAsync(id));   // çöpteki zamanlanmış mesaj gönderilmez
    }

    [Fact]
    public async Task SendDue_RunTwiceAtOnce_DeliversOnlyOnce()
    {
        // Bekçi test (Kavram 2): iki tur gerçekten çakışmasa da geçer; ama sonuç ASLA iki kopya olmamalı
        var sender = await factory.SignInNewUserAsync("sch");
        var recipient = await factory.CreateUserAsync("rcpt");
        int id = await ScheduleAsync(sender, recipient.Email, Now.AddDays(1));
        await MakeDueAsync(id);

        await Task.WhenAll(SendDueAsync(), SendDueAsync());

        Assert.Equal(1, await InboxCopiesAsync(id));
        Assert.Equal(1, await factory.QueryDbAsync(db => db.MessageRecipients.CountAsync(r => r.MessageId == id, Ct)));
    }

    [Fact]
    public async Task SendDue_ExternalFailure_MarksMessage_AndWarnsOnlyTheSender()
    {
        var sender = await factory.SignInNewUserAsync("sch");
        var recipientUser = await factory.CreateUserAsync("rcpt");
        string to = $"{recipientUser.Email}, disari{FakeMailService.FailingDomain}";
        int id = await ScheduleAsync(sender, to, Now.AddDays(1));
        await MakeDueAsync(id);

        await SendDueAsync();

        // Mailora içindeki alıcıya teslim edildi, dış gönderim başarısız: işaret (karar 4-A)
        Assert.Equal(1, await InboxCopiesAsync(id));
        Assert.True(await factory.QueryDbAsync(db => db.Messages.Where(m => m.MessageId == id).Select(m => m.ExternalDeliveryFailed).SingleAsync(Ct)));

        // Uyarı gönderene görünür, alıcıya görünmez (18.6.b)
        Assert.Contains("pv-banner warn", await sender.GetStringAsync($"/Message/Detail/{id}"));
        var recipient = await factory.SignInAsync(recipientUser);
        Assert.DoesNotContain("pv-banner warn", await recipient.GetStringAsync($"/Message/Detail/{id}"));
    }

    /* ---------- İptal (18.6) ---------- */

    [Fact]
    public async Task Cancel_ReturnsToDraft_AndIsNeverSent()
    {
        var sender = await factory.SignInNewUserAsync("sch");
        var recipient = await factory.CreateUserAsync("rcpt");
        int id = await ScheduleAsync(sender, recipient.Email, Now.AddDays(1));

        var response = await sender.PostFormAsync($"/Message/CancelSchedule/{id}");
        Assert.StartsWith("/Message/CreateMessage", response.Headers.Location!.OriginalString);   // düzenleme ekranı

        await MakeDueAsync(id);   // tarih geçmişe çekilse bile: belirleyici olan klasör (18.6 Kavram 3)
        await SendDueAsync();

        Assert.Equal(MailFolder.Draft, await SenderFolderAsync(id, sender.User));
        Assert.Equal(0, await InboxCopiesAsync(id));
    }

    [Fact]
    public async Task Cancel_AfterItWasSent_IsRefused()
    {
        // Sahiplenmenin deterministik kanıtı (Kavram 2): önce servis, sonra kullanıcı
        var sender = await factory.SignInNewUserAsync("sch");
        var recipient = await factory.CreateUserAsync("rcpt");
        int id = await ScheduleAsync(sender, recipient.Email, Now.AddDays(1));
        await MakeDueAsync(id);
        await SendDueAsync();

        var response = await sender.PostFormAsync($"/Message/CancelSchedule/{id}");

        Assert.StartsWith("/Message/Scheduled", response.Headers.Location!.OriginalString);   // "artık iptal edilemiyor"
        Assert.Equal(MailFolder.Sent, await SenderFolderAsync(id, sender.User));
    }

    /* ---------- Yardımcılar ---------- */

    private static string Format(DateTime time) => time.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    private static Task<HttpResponseMessage> PostScheduleAsync(SignedInClient client, string to, string subject, DateTime when) =>
        client.PostFormAsync("/Message/CreateMessage",
            ("To", to), ("Cc", ""), ("Bcc", ""), ("CategoryId", "1"),
            ("Subject", subject), ("Body", "<p>Zamanlanmis mesaj</p>"),
            ("ScheduledFor", Format(when)), ("intent", "schedule"));

    // Uygulamanın kendi ucuyla zamanlar (18.4) ve mesajın Id'sini döndürür
    private async Task<int> ScheduleAsync(SignedInClient client, string to, DateTime when)
    {
        string subject = $"Zamanli-{Guid.NewGuid():N}";
        var response = await PostScheduleAsync(client, to, subject, when);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Message/Scheduled", response.Headers.Location!.OriginalString);

        return await factory.QueryDbAsync(db => db.Messages.Where(m => m.Subject == subject).Select(m => m.MessageId).SingleAsync(Ct));
    }

    // "Zamanı geldi": saati değil verinin tarihini değiştir (15.6 Kavram 2)
    private Task MakeDueAsync(int messageId)
    {
        DateTime? past = Now.AddMinutes(-1);
        return factory.QueryDbAsync(db => db.Messages
            .Where(m => m.MessageId == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ScheduledFor, past), Ct));
    }

    // Kuralı doğrudan çağırır: ScheduledSender'ın zamanlayıcısı testlerde kapalı (18.5 Kavram 4)
    private async Task SendDueAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IScheduledSendService>().SendDueAsync(Ct);
    }

    private Task<MailFolder> SenderFolderAsync(int messageId, TestUser sender) =>
        factory.QueryDbAsync(db => db.MailboxItems
            .Where(i => i.MessageId == messageId && i.UserId == sender.Id)
            .Select(i => i.Folder)
            .SingleAsync(Ct));

    private Task<int> SenderItemIdAsync(int messageId, TestUser sender) =>
        factory.QueryDbAsync(db => db.MailboxItems
            .Where(i => i.MessageId == messageId && i.UserId == sender.Id)
            .Select(i => i.MailboxItemId)
            .SingleAsync(Ct));

    private Task<int> InboxCopiesAsync(int messageId) =>
        factory.QueryDbAsync(db => db.MailboxItems.CountAsync(i => i.MessageId == messageId && i.Folder == MailFolder.Inbox, Ct));

    private Task<DateTime?> SentDateAsync(int messageId) =>
        factory.QueryDbAsync(db => db.Messages.Where(m => m.MessageId == messageId).Select(m => m.SentDate).SingleAsync(Ct));
}