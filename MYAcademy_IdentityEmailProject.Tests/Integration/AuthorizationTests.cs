using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// Yetki (IDOR, 12.17.a) ve CSRF (13.1): başkasının numarasıyla istek → erişim yok.
public class AuthorizationTests(MailoraAppFactory factory)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%Mailora test\n");

    /* ---------- Mesaj detayı ---------- */

    [Fact]
    public async Task Detail_OthersMessage_Returns403()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var ued = await factory.SignInNewUserAsync("ued");
        var kaan = await factory.SignInNewUserAsync("kaan");
        int messageId = await ada.SendMessageAsync($"Gizli konu {Guid.NewGuid():N}", to: ued.User.Email);

        Assert.Equal(HttpStatusCode.OK, (await ued.GetAsync($"/Message/Detail/{messageId}")).StatusCode);      // olumlu (Kavram 1)
        Assert.Equal(HttpStatusCode.Forbidden, (await kaan.GetAsync($"/Message/Detail/{messageId}")).StatusCode);
    }

    [Fact]
    public async Task Detail_NonexistentMessage_Returns404()
    {
        var ada = await factory.SignInNewUserAsync("ada");

        Assert.Equal(HttpStatusCode.NotFound, (await ada.GetAsync("/Message/Detail/999999999")).StatusCode);
    }

    /* ---------- Ekler ---------- */

    [Fact]
    public async Task DownloadAttachment_Owner_GetsFileWithSecurityHeaders()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var (_, attachmentId) = await ada.UploadAttachmentAsync("Rapor.pdf", Pdf);

        var response = await ada.GetAsync($"/Message/DownloadAttachment/{attachmentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);   // gösterme, indir
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("sandbox", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAttachment_OtherUser_Returns404()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var kaan = await factory.SignInNewUserAsync("kaan");
        var (_, attachmentId) = await ada.UploadAttachmentAsync("Rapor.pdf", Pdf);

        // 403 değil 404: "var ama senin değil" bilgisi sızmasın (12.17.a Kavram 2)
        Assert.Equal(HttpStatusCode.NotFound, (await kaan.GetAsync($"/Message/DownloadAttachment/{attachmentId}")).StatusCode);
    }

    [Fact]
    public async Task RemoveAttachment_OtherUsersDraft_Returns404AndKeepsFile()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var kaan = await factory.SignInNewUserAsync("kaan");
        var (_, attachmentId) = await ada.UploadAttachmentAsync("Rapor.pdf", Pdf);

        var response = await kaan.PostFormAsync("/Message/RemoveAttachment", ("id", attachmentId.ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ada.GetAsync($"/Message/DownloadAttachment/{attachmentId}")).StatusCode);
    }

    /* ---------- Taslaklar ---------- */

    [Fact]
    public async Task EditDraft_OtherUsersDraft_RedirectsToOwnDrafts()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var kaan = await factory.SignInNewUserAsync("kaan");
        var (draftId, _) = await ada.UploadAttachmentAsync("Rapor.pdf", Pdf);   // yükleme bir taslak oluşturur (12.16.a)

        var response = await kaan.GetAsync($"/Message/CreateMessage?draftId={draftId}");

        // FindMyDraftAsync bulamaz → DraftGone → kaan'ın KENDİ Taslaklar'ı (9.6)
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Message/Drafts", response.Headers.Location?.OriginalString);
    }

    /* ---------- Toplu işlemler ---------- */

    [Fact]
    public async Task BulkMoveToTrash_OthersItem_ChangesNothing()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var ued = await factory.SignInNewUserAsync("ued");
        int messageId = await ada.SendMessageAsync($"Konu {Guid.NewGuid():N}", to: ued.User.Email);

        // ued'in kopyasının Id'si: ekranda yok, veritabanından
        int uedItemId = await factory.QueryDbAsync(db => db.MailboxItems
            .Where(i => i.MessageId == messageId && i.UserId == ued.User.Id)
            .Select(i => i.MailboxItemId)
            .SingleAsync(TestContext.Current.CancellationToken));

        // ada, ued'in kopyasını çöpe atmaya çalışıyor
        var response = await ada.PostFormAsync("/Message/BulkMoveToTrash", ("ids", uedItemId.ToString()));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(0, json.GetProperty("count").GetInt32());   // WHERE'deki UserId koşulu sessizce eledi (7.7)
        bool deleted = await factory.QueryDbAsync(db => db.MailboxItems
            .Where(i => i.MailboxItemId == uedItemId)
            .Select(i => i.DeletedDate != null)
            .SingleAsync(TestContext.Current.CancellationToken));
        Assert.False(deleted);
    }

    /* ---------- CSRF (13.1) ---------- */

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_Returns400()
    {
        var ada = await factory.SignInNewUserAsync("ada");

        // Token'sız: SignedInClient'ın yardımcısını değil, çıplak HttpClient'ı kullanıyoruz
        var response = await ada.Http.PostAsync("/Message/BulkSetRead",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["ids"] = "1", ["isRead"] = "true" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithAnotherUsersToken_Returns400()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var kaan = await factory.SignInNewUserAsync("kaan");
        string adasToken = await TestSupport.ReadAntiforgeryTokenAsync(ada.Http, "/Message/CreateMessage");

        // kaan'ın oturumunda ada'nın token'ı: token kimliğe bağlı (14.6.c Kavram 2)
        var response = await kaan.Http.PostAsync("/Message/BulkSetRead",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ids"] = "1",
                ["isRead"] = "true",
                ["__RequestVerificationToken"] = adasToken
            }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}