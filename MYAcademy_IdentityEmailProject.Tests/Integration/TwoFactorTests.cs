using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// 16.7.b: İki adımlı doğrulama, uçtan uca. Kodları telefon yerine Totp yardımcısı üretir (16.7.a).
// Identity TOTP'de TimeProvider'ı değil gerçek UTC saati kullanır (16.1 Kavram 4): kodlar DateTimeOffset.UtcNow ile.
public partial class TwoFactorTests(MailoraAppFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /* ---------- Kurulum (16.2–16.3) ---------- */

    [Fact]
    public async Task Setup_WithCorrectCode_EnablesTwoFactor_ShowsTenCodes_AndNotifies()
    {
        var client = await factory.SignInNewUserAsync("tfa");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync("/TwoFactor/Start")).StatusCode);

        // Telefonun QR'dan okuduğu anahtar
        string? key = await AuthenticatorKeyAsync(client.User);
        Assert.NotNull(key);   // Start anahtarı üretti; derleyici de bundan sonra key'in null olmadığını biliyor
        var response = await client.PostFormAsync("/TwoFactor/Setup", ("Code", CurrentCode(key)));

        // Yönlendirme yok: kodlar bu yanıtta, bir kez (16.3 Kavram 2)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(10, RecoveryCodePattern().Matches(html).Count);

        Assert.True(await TwoFactorEnabledAsync(client.User));
        Assert.Contains(factory.Mail.Sent, m => m.Envelope.Contains(client.User.Email) && m.Subject.Contains("açıldı"));
    }

    [Fact]
    public async Task Setup_WithWrongCode_DoesNotEnable()
    {
        var client = await factory.SignInNewUserAsync("tfa");
        await client.PostFormAsync("/TwoFactor/Start");

        var response = await client.PostFormAsync("/TwoFactor/Setup", ("Code", "000000"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // aynı sayfa, hata mesajıyla
        Assert.False(await TwoFactorEnabledAsync(client.User));
    }

    /* ---------- Giriş (16.4) ---------- */

    [Fact]
    public async Task SignIn_WithTwoFactor_RequiresCodeBeforeAccess()
    {
        var user = await factory.CreateUserAsync("tfa");
        var (key, _) = await EnableDirectlyAsync(user);
        var http = NewAnonymousClient();

        var passwordStep = await PasswordStepAsync(http, user);
        Assert.Equal(HttpStatusCode.Redirect, passwordStep.StatusCode);
        string codePage = passwordStep.Headers.Location!.OriginalString;
        Assert.StartsWith("/Login/LoginWith2fa", codePage);

        // Şifre doğru ama henüz giriş YOK: sadece 5 dakikalık ara çerez var (16.4 Kavram 1)
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/Message/Inbox", Ct)).StatusCode);

        string token = await TestSupport.ReadAntiforgeryTokenAsync(http, codePage);
        var codeStep = await PostAsync(http, "/Login/LoginWith2fa", token, ("Code", CurrentCode(key)));

        Assert.Equal(HttpStatusCode.Redirect, codeStep.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/Message/Inbox", Ct)).StatusCode);
    }

    [Fact]
    public async Task SignIn_FiveWrongCodes_LockOut_EvenTheCorrectCode()
    {
        var user = await factory.CreateUserAsync("tfa");
        var (key, _) = await EnableDirectlyAsync(user);
        var http = NewAnonymousClient();
        var passwordStep = await PasswordStepAsync(http, user);
        string token = await TestSupport.ReadAntiforgeryTokenAsync(http, passwordStep.Headers.Location!.OriginalString);

        // Yanlış kodlar şifre denemeleriyle AYNI sayaca eklenir: 5 yanlışta kilit (16.4 Kavram 3)
        for (int i = 0; i < 5; i++)
        {
            await PostAsync(http, "/Login/LoginWith2fa", token, ("Code", "000000"));
        }

        // Kaynakta kilit kontrolü kod doğrulamasından ÖNCE: doğru kod da reddedilir
        var correct = await PostAsync(http, "/Login/LoginWith2fa", token, ("Code", CurrentCode(key)));

        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);   // form, kilit mesajıyla
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/Message/Inbox", Ct)).StatusCode);
        Assert.NotNull(await factory.QueryDbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.LockoutEnd).SingleAsync(Ct)));
    }

    [Fact]
    public async Task RecoveryCode_WorksOnlyOnce()
    {
        var user = await factory.CreateUserAsync("tfa");
        var (_, codes) = await EnableDirectlyAsync(user);

        var first = NewAnonymousClient();
        Assert.Equal(HttpStatusCode.Redirect, (await RecoveryStepAsync(first, user, codes[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/Message/Inbox", Ct)).StatusCode);

        // Aynı kod ikinci kez: kullanıldığında silinmişti
        var second = NewAnonymousClient();
        Assert.Equal(HttpStatusCode.OK, (await RecoveryStepAsync(second, user, codes[0])).StatusCode);   // form, hata mesajıyla
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/Message/Inbox", Ct)).StatusCode);
    }

    [Fact]
    public async Task RecoveryCode_IsAccepted_InLowercase()
    {
        var user = await factory.CreateUserAsync("tfa");
        var (_, codes) = await EnableDirectlyAsync(user);
        var http = NewAnonymousClient();

        var response = await RecoveryStepAsync(http, user, codes[0].ToLowerInvariant());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task CodePage_WithoutPasswordStep_RedirectsToSignIn()
    {
        var response = await NewAnonymousClient().GetAsync("/Login/LoginWith2fa", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Login/SignIn", response.Headers.Location!.OriginalString);
    }

    /* ---------- Kapatma (16.5) ---------- */

    [Fact]
    public async Task Disable_RequiresPassword_ChangesKey_AndNotifies()
    {
        var user = await factory.CreateUserAsync("tfa");
        var (oldKey, _) = await EnableDirectlyAsync(user);
        var client = await SignInWithTwoFactorAsync(user, oldKey);

        // Yanlış şifre: hiçbir şey değişmez (16.5 Kavram 1)
        await client.PostFormAsync("/TwoFactor/Disable", ("password", "Yanlis-Parola-1!"));
        Assert.True(await TwoFactorEnabledAsync(user));

        await client.PostFormAsync("/TwoFactor/Disable", ("password", user.Password));

        Assert.False(await TwoFactorEnabledAsync(user));
        Assert.NotEqual(oldKey, await AuthenticatorKeyAsync(user));   // eski telefonun kodları artık işe yaramaz
        Assert.Contains(factory.Mail.Sent, m => m.Envelope.Contains(user.Email) && m.Subject.Contains("kapatıldı"));
    }

    /* ---------- Yardımcılar ---------- */

    private static string CurrentCode(string key) => Totp.Compute(key, DateTimeOffset.UtcNow);

    // Hazırlık kısayolu: kurulumun kendisi Setup_ testlerinde uçlarla sınanıyor; burada sadece "2FA açık bir hesap" lazım
    private async Task<(string Key, List<string> RecoveryCodes)> EnableDirectlyAsync(TestUser testUser)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByIdAsync(testUser.Id))!;

        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToList();

        return ((await users.GetAuthenticatorKeyAsync(user))!, codes);
    }

    private async Task<string?> AuthenticatorKeyAsync(TestUser testUser)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await users.GetAuthenticatorKeyAsync((await users.FindByIdAsync(testUser.Id))!);
    }

    private Task<bool> TwoFactorEnabledAsync(TestUser user) =>
        factory.QueryDbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.TwoFactorEnabled).SingleAsync(Ct));

    private HttpClient NewAnonymousClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false
    });

    private static async Task<HttpResponseMessage> PasswordStepAsync(HttpClient http, TestUser user)
    {
        string token = await TestSupport.ReadAntiforgeryTokenAsync(http, "/Login/SignIn");
        return await PostAsync(http, "/Login/SignIn", token,
            ("Username", user.UserName), ("Password", user.Password), ("RememberMe", "false"));
    }

    private static async Task<HttpResponseMessage> RecoveryStepAsync(HttpClient http, TestUser user, string recoveryCode)
    {
        await PasswordStepAsync(http, user);
        string token = await TestSupport.ReadAntiforgeryTokenAsync(http, "/Login/LoginWithRecoveryCode");
        return await PostAsync(http, "/Login/LoginWithRecoveryCode", token, ("RecoveryCode", recoveryCode));
    }

    // TestSupport.SignInAsync 2FA'lı hesapta çalışmaz (şifreden sonra kod ekranı gelir): iki adımlı sürüm
    private async Task<SignedInClient> SignInWithTwoFactorAsync(TestUser user, string key)
    {
        var http = NewAnonymousClient();
        var passwordStep = await PasswordStepAsync(http, user);
        string codeToken = await TestSupport.ReadAntiforgeryTokenAsync(http, passwordStep.Headers.Location!.OriginalString);

        var codeStep = await PostAsync(http, "/Login/LoginWith2fa", codeToken, ("Code", CurrentCode(key)));
        Assert.Equal(HttpStatusCode.Redirect, codeStep.StatusCode);

        // Giriş yapmış kimlik için yeni token (14.6.b'deki gibi)
        string token = await TestSupport.ReadAntiforgeryTokenAsync(http, "/Message/CreateMessage");
        return new SignedInClient(http, user, token);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string url, string token,
                                                       params (string Key, string Value)[] fields)
    {
        var pairs = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))
                          .Append(new("__RequestVerificationToken", token));
        return http.PostAsync(url, new FormUrlEncodedContent(pairs), Ct);
    }

    // Identity'nin kurtarma kodu biçimi: 5 karakter - 5 karakter, sadece bu küme (kaynaktaki AllowedChars)
    [GeneratedRegex("[23456789BCDFGHJKMNPQRTVWXY]{5}-[23456789BCDFGHJKMNPQRTVWXY]{5}")]
    private static partial Regex RecoveryCodePattern();
}