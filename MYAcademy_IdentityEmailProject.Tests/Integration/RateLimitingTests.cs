using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// 17.5: İstek sınırlama ve doğrulama kodunun deneme hakkı.
// Düşük sınır gereken testler KENDİ uygulama örneklerini kurar (Kavram 1): diğer testlerin sayaçlarına dokunmazlar.
public class RateLimitingTests(MailoraAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%test\n");

    /* ---------- Middleware kuralları (17.2–17.4) ---------- */

    [Fact]
    public async Task SignIn_OverTheLimit_Returns429_WithRetryAfter()
    {
        await using var app = WithLimits(("Auth:PermitLimit", "3"));
        var http = NewAnonymousClient(app);
        string token = await TestSupport.ReadAntiforgeryTokenAsync(http, "/Login/SignIn");

        // Var olmayan kullanıcılar: kilitleme devreye girmez, sadece IP sınırı sayar (17.1 Kavram 1)
        for (int i = 0; i < 3; i++)
        {
            var allowed = await PostAsync(http, "/Login/SignIn", token, ("Username", $"yok_{i}"), ("Password", "Yanlis-Parola-1!"));
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);   // pozitif kontrol: sınırın altındayken form normal döner
        }

        var rejected = await PostAsync(http, "/Login/SignIn", token, ("Username", "yok_3"), ("Password", "Yanlis-Parola-1!"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }

    [Fact]
    public async Task CheckUserName_OverTheLimit_ReturnsJsonError()
    {
        await using var app = WithLimits(("Lookup:PermitLimit", "2"));
        var http = NewAnonymousClient(app);

        await http.GetAsync("/Register/CheckUserName?userName=a", Ct);
        await http.GetAsync("/Register/CheckUserName?userName=b", Ct);
        var rejected = await http.GetAsync("/Register/CheckUserName?userName=c", Ct);

        // AJAX ucu: HTML sayfası değil JSON (17.2 Kavram 3)
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var json = await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(string.IsNullOrEmpty(json.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Upload_IsLimitedPerUser_NotGlobally()
    {
        await using var app = WithLimits(("UserUpload:PermitLimit", "1"));
        var first = await app.SignInNewUserAsync("upl");
        var second = await app.SignInNewUserAsync("upl");

        Assert.Equal(HttpStatusCode.OK, (await first.PostAttachmentAsync("a.pdf", Pdf)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.PostAttachmentAsync("b.pdf", Pdf)).StatusCode);

        // Başka kullanıcı: kendi sayacı (17.1 Kavram 3). Sınır global olsaydı bu da 429 olurdu.
        Assert.Equal(HttpStatusCode.OK, (await second.PostAttachmentAsync("c.pdf", Pdf)).StatusCode);
    }

    /* ---------- Gönderme sınırı: action içinden (17.4, karar 7-B) ---------- */

    [Fact]
    public async Task Send_OverTheLimit_Returns429_AndKeepsTheForm()
    {
        await using var app = WithLimits(("UserSend:PermitLimit", "1"));
        var sender = await app.SignInNewUserAsync("send");
        var recipient = await app.CreateUserAsync("rcpt");
        await sender.SendMessageAsync("Ilk mesaj", recipient.Email);

        string subject = $"Sinir-{Guid.NewGuid():N}";
        var rejected = await sender.PostFormAsync("/Message/CreateMessage",
            ("To", recipient.Email), ("Cc", ""), ("Bcc", ""), ("CategoryId", "1"),
            ("Subject", subject), ("Body", "<p>Kaybolmamali</p>"), ("intent", "send"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains(subject, await rejected.Content.ReadAsStringAsync(Ct));   // form, yazılanlarla geri geldi
        Assert.False(await app.QueryDbAsync(db => db.Messages.AnyAsync(m => m.Subject == subject, Ct)));   // gönderilmedi
    }

    [Fact]
    public async Task Send_InvalidForm_DoesNotUseAPermit()
    {
        await using var app = WithLimits(("UserSend:PermitLimit", "1"));
        var sender = await app.SignInNewUserAsync("send");
        var recipient = await app.CreateUserAsync("rcpt");

        // Alıcısız form: doğrulama hatası, sınıra hiç bakılmaz (17.4 Kavram 3)
        var invalid = await sender.PostFormAsync("/Message/CreateMessage",
            ("To", ""), ("Cc", ""), ("Bcc", ""), ("CategoryId", "1"),
            ("Subject", "Eksik"), ("Body", "<p>x</p>"), ("intent", "send"));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);

        // Tek hak hâlâ duruyor: geçerli gönderim başarılı (yardımcı yönlendirmeyi kendisi doğrular)
        await sender.SendMessageAsync("Gecerli", recipient.Email);
    }

    /* ---------- Doğrulama kodunun deneme hakkı (17.3, karar 6-A) ---------- */

    [Fact]
    public async Task ConfirmEmail_FiveWrongCodes_BurnTheCode_EvenTheCorrectOneFails()
    {
        var (email, code) = await CreateUnconfirmedUserAsync();
        var http = NewAnonymousClient(factory);
        string token = await TestSupport.ReadAntiforgeryTokenAsync(http, $"/Register/ConfirmEmail?email={Uri.EscapeDataString(email)}");

        for (int i = 0; i < 5; i++)
        {
            await PostAsync(http, "/Register/ConfirmEmail", token, ("Email", email), ("Code", "000000"));
        }
        var correct = await PostAsync(http, "/Register/ConfirmEmail", token, ("Email", email), ("Code", code));

        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);   // form yeniden: "geçerli bir kod yok"
        var user = await factory.QueryDbAsync(db => db.Users.Where(u => u.Email == email)
            .Select(u => new { u.EmailConfirmed, u.ValidCode }).SingleAsync(Ct));
        Assert.False(user.EmailConfirmed);
        Assert.Null(user.ValidCode);   // kod yandı
    }

    [Fact]
    public async Task ConfirmEmail_CorrectCode_Confirms()
    {
        // Pozitif kontrol: üstteki test "doğru kod da reddediliyor" diyor; doğrulama hiç çalışmıyor olmasın
        var (email, code) = await CreateUnconfirmedUserAsync();
        var http = NewAnonymousClient(factory);
        string token = await TestSupport.ReadAntiforgeryTokenAsync(http, $"/Register/ConfirmEmail?email={Uri.EscapeDataString(email)}");

        var response = await PostAsync(http, "/Register/ConfirmEmail", token, ("Email", email), ("Code", code));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await factory.QueryDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.EmailConfirmed).SingleAsync(Ct)));
    }

    /* ---------- Hatalı ayar (17.2 Kavram 2) ---------- */

    [Fact]
    public async Task InvalidSettings_PreventStartup()
    {
        await using var app = WithLimits(("Auth:PermitLimit", "0"));

        var exception = Record.Exception(() => app.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("RateLimiting", exception.ToString());
    }

    /* ---------- Yardımcılar ---------- */

    // Ana fabrikanın her şeyini devralır, sadece verilen sınırları değiştirir (Kavram 1)
    private WebApplicationFactory<Program> WithLimits(params (string Key, string Value)[] limits) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(config =>
            config.AddInMemoryCollection(limits.ToDictionary(l => $"RateLimiting:{l.Key}", l => (string?)l.Value))));

    private static HttpClient NewAnonymousClient(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string url, string token,
                                                       params (string Key, string Value)[] fields)
    {
        var pairs = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
                          .Append(new("__RequestVerificationToken", token));
        return http.PostAsync(url, new FormUrlEncodedContent(pairs), Ct);
    }

    // Kayıt akışını atlayıp bilinen bir kodla doğrulanmamış kullanıcı (kayıt ucu AuthMail sınırına tabi ve mail gönderir)
    private async Task<(string Email, string Code)> CreateUnconfirmedUserAsync()
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        var user = new AppUser
        {
            UserName = $"unconf_{tag}",
            Email = $"unconf_{tag}@mailora.test",
            EmailConfirmed = false,
            ValidCode = 123456,
            Name = "unconf",
            Surname = "Test"
        };

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await users.CreateAsync(user, TestSupport.Password)).Succeeded);

        return (user.Email, "123456");
    }
}