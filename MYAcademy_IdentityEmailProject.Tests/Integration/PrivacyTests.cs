using Microsoft.EntityFrameworkCore;
namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// Gizli (Bcc) alıcı hiçbir yerden sızmamalı (12.10, 12.11, 12.12).
// "Görünmüyor" = HTML'de YOK (Kavram 1).
public class PrivacyTests(MailoraAppFactory factory)
{
    // Senaryo: ada gönderiyor.  Kime: ued   Bilgi: kaan   Gizli: can, deniz
    private sealed record Scenario(SignedInClient Ada, SignedInClient Ued, SignedInClient Kaan,
                                   SignedInClient Can, SignedInClient Deniz, int MessageId, string Subject);

    private async Task<Scenario> SendGroupMessageAsync()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var ued = await factory.SignInNewUserAsync("ued");
        var kaan = await factory.SignInNewUserAsync("kaan");
        var can = await factory.SignInNewUserAsync("can");
        var deniz = await factory.SignInNewUserAsync("deniz");
        string subject = $"Grup {Guid.NewGuid():N}";

        int id = await ada.SendMessageAsync(subject, to: ued.User.Email, cc: kaan.User.Email,
                                            bcc: $"{can.User.Email}, {deniz.User.Email}");
        return new Scenario(ada, ued, kaan, can, deniz, id, subject);
    }

    /* ---------- Detay sayfası (12.11) ---------- */

    [Fact]
    public async Task Detail_ToRecipient_BccAddressesNotInHtml()
    {
        var s = await SendGroupMessageAsync();

        string html = await s.Ued.GetStringAsync($"/Message/Detail/{s.MessageId}");

        Assert.Contains(s.Kaan.User.Email, html);           // olumlu: Bilgi alıcısı görünür
        Assert.DoesNotContain(s.Can.User.Email, html);
        Assert.DoesNotContain(s.Deniz.User.Email, html);
    }

    [Fact]
    public async Task Detail_Sender_SeesBccAddresses()
    {
        var s = await SendGroupMessageAsync();

        string html = await s.Ada.GetStringAsync($"/Message/Detail/{s.MessageId}");

        Assert.Contains(s.Can.User.Email, html);
        Assert.Contains(s.Deniz.User.Email, html);
    }

    [Fact]
    public async Task Detail_BccRecipient_SeesHimselfButNotOtherBcc()
    {
        var s = await SendGroupMessageAsync();

        string html = await s.Can.GetStringAsync($"/Message/Detail/{s.MessageId}");

        Assert.Contains(s.Can.User.Email, html);            // "Gizli: … (siz)"
        Assert.DoesNotContain(s.Deniz.User.Email, html);    // diğer Gizli alıcı
    }

    /* ---------- İlet ve yanıt formları (12.11.c, 12.12) ---------- */

    [Fact]
    public async Task Forward_BySender_ForwardBlockDoesNotRevealBcc()
    {
        var s = await SendGroupMessageAsync();

        // Gönderen bile olsa: künyeyi İLETİLEN kişiler okuyacak (ForOutsider)
        string html = await s.Ada.GetStringAsync($"/Message/CreateMessage?forwardOf={s.MessageId}");

        Assert.Contains(s.Kaan.User.Email, html);           // künyede Bilgi satırı var
        Assert.DoesNotContain(s.Can.User.Email, html);
        Assert.DoesNotContain(s.Deniz.User.Email, html);
    }

    [Fact]
    public async Task ReplyAll_BySender_DoesNotIncludeBcc()
    {
        var s = await SendGroupMessageAsync();

        string html = await s.Ada.GetStringAsync($"/Message/CreateMessage?replyTo={s.MessageId}&replyAll=true");

        Assert.Contains(s.Ued.User.Email, html);
        Assert.Contains(s.Kaan.User.Email, html);
        Assert.DoesNotContain(s.Can.User.Email, html);
    }

    [Fact]
    public async Task ReplyAll_ByBccRecipient_FallsBackToSenderOnly()
    {
        var s = await SendGroupMessageAsync();

        // URL'ye replyAll=true elle yazılmış (12.12.b Deney 5)
        string html = await s.Can.GetStringAsync($"/Message/CreateMessage?replyTo={s.MessageId}&replyAll=true");

        Assert.Contains(s.Ada.User.Email, html);            // sadece gönderene
        Assert.DoesNotContain(s.Ued.User.Email, html);
        Assert.DoesNotContain(s.Kaan.User.Email, html);
    }

    /* ---------- Arama (12.11.c) ---------- */

    // Bir kullanıcının bu mesajdaki kopyasının numarası: liste satırı bunu data-id olarak taşır.
    // Konu, zil menüsünde de görünebildiği için KONUYA değil SATIRA bakıyoruz.
    private Task<int> ItemIdAsync(int messageId, string userId) =>
        factory.QueryDbAsync(db => db.MailboxItems
            .Where(i => i.MessageId == messageId && i.UserId == userId)
            .Select(i => i.MailboxItemId)
            .SingleAsync(TestContext.Current.CancellationToken));

    [Fact]
    public async Task Search_ToRecipientSearchingBccAddress_FindsNothing()
    {
        var s = await SendGroupMessageAsync();
        string row = $"data-id=\"{await ItemIdAsync(s.MessageId, s.Ued.User.Id)}\"";

        // Olumlu kontrol: aramasız listede satır VAR (işaretin doğru olduğunun kanıtı)
        Assert.Contains(row, await s.Ued.GetStringAsync("/Message/Inbox"));

        string html = await s.Ued.GetStringAsync($"/Message/Inbox?Q={Uri.EscapeDataString(s.Can.User.Email)}");
        Assert.DoesNotContain(row, html);
    }

    [Fact]
    public async Task Search_SenderSearchingBccAddress_FindsMessage()
    {
        var s = await SendGroupMessageAsync();
        string row = $"data-id=\"{await ItemIdAsync(s.MessageId, s.Ada.User.Id)}\"";

        string html = await s.Ada.GetStringAsync($"/Message/Sent?Q={Uri.EscapeDataString(s.Can.User.Email)}");

        Assert.Contains(row, html);
    }

    /* ---------- SMTP: başlık ve zarf (12.10, Kavram 3) ---------- */

    [Fact]
    public async Task Smtp_ExternalBcc_OnlyInEnvelopeNeverInHeaders()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var ued = await factory.SignInNewUserAsync("ued");
        string external = $"dis_{Guid.NewGuid():N}@example.com";

        await ada.SendMessageAsync($"Dış {Guid.NewGuid():N}", to: ued.User.Email, bcc: external);

        var mail = Assert.Single(factory.Mail.Sent, m => m.Envelope.Contains(external));
        Assert.Equal(new[] { external }, mail.Envelope);           // Mailora içi alıcı zarfta YOK: çift teslim olmaz
        Assert.Contains(ued.User.Email, mail.HeaderTo);             // ama başlıkta görünür
        Assert.DoesNotContain(external, mail.HeaderTo);
        Assert.DoesNotContain(external, mail.HeaderCc);
    }
}