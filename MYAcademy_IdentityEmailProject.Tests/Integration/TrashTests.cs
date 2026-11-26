using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// 15.6.b: Çöpün 30 gün sonra otomatik boşaltılması.
// Saat donmuş (factory.Clock): testler saati değil VERİNİN tarihini ayarlar (15.6 Kavram 2).
// Purge BÜTÜN kullanıcılarda çalışır ve testler paralel: dönen SAYIYA değil, kendi verimizin DURUMUNA bakılır.
public partial class TrashTests(MailoraAppFactory factory)
{
    private DateTime Now => factory.Clock.GetLocalNow().DateTime;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Purge_DeletesCopiesOlderThanRetention_AndKeepsNewerOnes()
    {
        var sender = await factory.SignInNewUserAsync("sender");
        var owner = await factory.CreateUserAsync("owner");
        int oldItem = await InboxCopyAsync(owner, await sender.SendMessageAsync("Eski çöp", owner.Email));
        int newItem = await InboxCopyAsync(owner, await sender.SendMessageAsync("Yeni çöp", owner.Email));
        await SetDeletedAsync(oldItem, Now.AddDays(-31));
        await SetDeletedAsync(newItem, Now.AddDays(-29));

        await PurgeAsync();

        Assert.False(await ItemExistsAsync(oldItem));
        Assert.True(await ItemExistsAsync(newItem));   // pozitif kontrol: purge her şeyi silmiyor
    }

    [Theory]
    [InlineData(1, true)]     // 30 gün + 1 dakika önce çöpe atılmış: süresi dolmuş
    [InlineData(-1, false)]   // 30 gün − 1 dakika önce: henüz değil
    public async Task Purge_RespectsTheExactRetentionBoundary(int minutesPastRetention, bool shouldBeDeleted)
    {
        var sender = await factory.SignInNewUserAsync("sender");
        var owner = await factory.CreateUserAsync("owner");
        int item = await InboxCopyAsync(owner, await sender.SendMessageAsync("Sınır", owner.Email));

        // Saat donmuş olduğu için "1 dakika" kesin: test çalışırken zaman akıp sınırı kaydıramaz
        await SetDeletedAsync(item, Now.AddDays(-TrashRules.RetentionDays).AddMinutes(-minutesPastRetention));

        await PurgeAsync();

        Assert.Equal(!shouldBeDeleted, await ItemExistsAsync(item));
    }

    [Fact]
    public async Task Purge_KeepsOldMessages_ThatAreNotInTrash()
    {
        var sender = await factory.SignInNewUserAsync("sender");
        var owner = await factory.CreateUserAsync("owner");
        int messageId = await sender.SendMessageAsync("Eski ama çöpte değil", owner.Email);
        int item = await InboxCopyAsync(owner, messageId);

        // Mesajın kendisi 400 günlük: kural "eski mesajları sil" DEĞİL, "çöpte uzun kalanları sil"
        DateTime? longAgo = Now.AddDays(-400);
        await factory.QueryDbAsync(db => db.Messages
            .Where(m => m.MessageId == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.SentDate, longAgo), Ct));

        await PurgeAsync();

        Assert.True(await ItemExistsAsync(item));
    }

    [Fact]
    public async Task Purge_DeletesOrphanedMessage_AndItsAttachmentFile()
    {
        // Sadece yazarında kopyası olan bir taslak + ek (12.16'daki yükleme ucu)
        var author = await factory.SignInNewUserAsync("author");
        var (draftId, attachmentId) = await author.UploadAttachmentAsync("rapor.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\n%test\n"));

        var storage = factory.Services.GetRequiredService<IAttachmentStorage>();
        string storedName = await factory.QueryDbAsync(db => db.Attachments
            .Where(a => a.AttachmentId == attachmentId)
            .Select(a => a.StoredName)
            .SingleAsync(Ct));
        Assert.NotNull(storage.GetPath(storedName));   // pozitif kontrol: dosya gerçekten diskte

        await SetDeletedAsync(await CopyAsync(author.User, draftId, MailFolder.Draft), Now.AddDays(-31));

        await PurgeAsync();

        // Tek kopya gitti → mesaj sahipsiz kaldı → mesaj, ek satırı ve ek DOSYASI da gider (15.3'e taşınan mantık)
        Assert.False(await factory.QueryDbAsync(db => db.Messages.AnyAsync(m => m.MessageId == draftId, Ct)));
        Assert.Null(storage.GetPath(storedName));
    }

    [Fact]
    public async Task Purge_KeepsMessage_WhileAnotherUserStillHasACopy()
    {
        var sender = await factory.SignInNewUserAsync("sender");
        var owner = await factory.CreateUserAsync("owner");
        int messageId = await sender.SendMessageAsync("Paylaşılan", owner.Email);
        int ownerItem = await InboxCopyAsync(owner, messageId);
        int senderItem = await CopyAsync(sender.User, messageId, MailFolder.Sent);
        await SetDeletedAsync(ownerItem, Now.AddDays(-31));

        await PurgeAsync();

        Assert.False(await ItemExistsAsync(ownerItem));   // alıcının kopyası gitti
        Assert.True(await ItemExistsAsync(senderItem));   // gönderenin kopyası duruyor
        Assert.True(await factory.QueryDbAsync(db => db.Messages.AnyAsync(m => m.MessageId == messageId, Ct)));
    }

    [Fact]
    public async Task TrashPage_ShowsBadge_OnlyInTheLastDays()
    {
        var sender = await factory.SignInNewUserAsync("sender");
        var ownerUser = await factory.CreateUserAsync("owner");
        int soon = await InboxCopyAsync(ownerUser, await sender.SendMessageAsync("Yakında", ownerUser.Email));
        int later = await InboxCopyAsync(ownerUser, await sender.SendMessageAsync("Sonra", ownerUser.Email));
        await SetDeletedAsync(soon, Now.AddDays(-28));    // 2 gün kaldı → rozet
        await SetDeletedAsync(later, Now.AddDays(-26));   // 4 gün kaldı → rozet yok (WarningDays = 3)

        var owner = await factory.SignInAsync(ownerUser);
        string html = await owner.GetStringAsync("/Message/Trash");

        // Pozitif kontrol: iki satır da sayfada (rozetin yokluğu "satır yok" yüzünden olmasın)
        Assert.Contains($"data-id=\"{soon}\"", html);
        Assert.Contains($"data-id=\"{later}\"", html);

        // Sayfadaki TEK rozet, 2 gün diyor
        var badges = PurgeBadge().Matches(html).Select(m => m.Groups[1].Value);
        Assert.Equal(["2"], badges);
    }

    [Fact]
    public async Task BulkDeleteForever_IgnoresCopiesThatAreNotInTrash()
    {
        var sender = await factory.SignInNewUserAsync("sender");
        var ownerUser = await factory.CreateUserAsync("owner");
        int item = await InboxCopyAsync(ownerUser, await sender.SendMessageAsync("Gelen kutusunda", ownerUser.Email));
        var owner = await factory.SignInAsync(ownerUser);

        // Çöpte olmayan kopyanın Id'si doğrudan gönderiliyor: servis çöp koşulunu kendisi eklediği için silinmez (15.3)
        Assert.Equal(0, await DeleteForeverAsync(owner, item));
        Assert.True(await ItemExistsAsync(item));

        // Pozitif kontrol: aynı kopya çöpe atılınca aynı istek siler
        await SetDeletedAsync(item, Now);
        Assert.Equal(1, await DeleteForeverAsync(owner, item));
        Assert.False(await ItemExistsAsync(item));
    }

    /* ---------- Yardımcılar ---------- */

    private Task<int> CopyAsync(TestUser user, int messageId, MailFolder folder) =>
        factory.QueryDbAsync(db => db.MailboxItems
            .Where(i => i.UserId == user.Id && i.MessageId == messageId && i.Folder == folder)
            .Select(i => i.MailboxItemId)
            .SingleAsync(Ct));

    private Task<int> InboxCopyAsync(TestUser user, int messageId) => CopyAsync(user, messageId, MailFolder.Inbox);

    private Task SetDeletedAsync(int itemId, DateTime? deletedDate) =>
        factory.QueryDbAsync(db => db.MailboxItems
            .Where(i => i.MailboxItemId == itemId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.DeletedDate, deletedDate), Ct));

    private Task<bool> ItemExistsAsync(int itemId) =>
        factory.QueryDbAsync(db => db.MailboxItems.AnyAsync(i => i.MailboxItemId == itemId, Ct));

    // Kuralı doğrudan çağırır: TrashPurger'ın zamanlayıcısını beklemeye gerek yok (15.4 Kavram 1)
    private async Task PurgeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ITrashService>().PurgeExpiredAsync(Ct);
    }

    // Controller'ın yanıtındaki sayı burada güvenli: sadece BU kullanıcının, verilen Id'lerdeki kopyaları sayılır
    private static async Task<int> DeleteForeverAsync(SignedInClient client, int itemId)
    {
        var response = await client.PostFormAsync("/Message/BulkDeleteForever", ("ids", itemId.ToString()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return json.GetProperty("count").GetInt32();
    }

    [GeneratedRegex("data-purge-days=\"(\\d+)\"")]
    private static partial Regex PurgeBadge();
}