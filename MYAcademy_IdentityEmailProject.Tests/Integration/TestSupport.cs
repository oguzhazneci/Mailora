using MYAcademy_IdentityEmailProject.Context;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using MYAcademy_IdentityEmailProject.Entities;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// Testte oluşturulan bir kullanıcı. Şifre sadece testlerde kullanılan sabit bir değer.
public sealed record TestUser(string Id, string UserName, string Email, string Password);

public static partial class TestSupport
{
    // Program.cs'teki Identity kurallarına uyar: en az 8 karakter, büyük ve küçük harf, rakam, sembol
    public const string Password = "Test-Parola-123!";

    // Benzersiz adlı, e-postası DOĞRULANMIŞ bir kullanıcı (RequireConfirmedEmail = true).
    // Her test kendi kullanıcılarını oluşturur; başka testin verisine dokunmaz (karar 4-A).
    public static async Task<TestUser> CreateUserAsync(this MailoraAppFactory factory, string prefix = "user")
    {
        string tag = Guid.NewGuid().ToString("N")[..8];
        var user = new AppUser
        {
            UserName = $"{prefix}_{tag}",
            Email = $"{prefix}_{tag}@mailora.test",
            EmailConfirmed = true,
            Name = prefix,
            Surname = "Test"
        };

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var result = await userManager.CreateAsync(user, Password);

        Assert.True(result.Succeeded, "Test kullanıcısı oluşturulamadı: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        return new TestUser(user.Id, user.UserName, user.Email, Password);
    }

    // Kullanıcı oluştur ve GERÇEK giriş akışından geçirerek oturum aç (Kavram 1)
    public static async Task<SignedInClient> SignInNewUserAsync(this MailoraAppFactory factory, string prefix = "user")
        => await factory.SignInAsync(await factory.CreateUserAsync(prefix));

    public static async Task<SignedInClient> SignInAsync(this MailoraAppFactory factory, TestUser user)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false          // yönlendirmenin kendisini görmek istiyoruz (PRG)
        });

        // 1) Giriş sayfası: anonim token (Kavram 2)
        string loginToken = await ReadAntiforgeryTokenAsync(http, "/Login/SignIn");

        // 2) Giriş formu: alan adları UserLoginViewModel ile aynı
        var response = await http.PostAsync("/Login/SignIn", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = user.UserName,
            ["Password"] = user.Password,
            ["RememberMe"] = "false",
            ["__RequestVerificationToken"] = loginToken
        }), TestContext.Current.CancellationToken);

        // Başarılı giriş → yönlendirme (PRG). Başarısızsa 200 + form yeniden gelir.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // 3) Giriş yapmış kullanıcı için YENİ token: eski token anonim kimliğe aitti (Kavram 2)
        string token = await ReadAntiforgeryTokenAsync(http, "/Message/CreateMessage");
        return new SignedInClient(http, user, token);
    }

    // Testin veritabanına doğrudan bakması gerektiğinde (bir kopyanın Id'si, bir alanın son hali)
    public static async Task<T> QueryDbAsync<T>(this MailoraAppFactory factory, Func<EmailContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<EmailContext>());
    }

    // Sayfadaki formun gizli alanından token'ı okur: gerçek tarayıcının yaptığı gibi
    public static async Task<string> ReadAntiforgeryTokenAsync(HttpClient http, string url)
    {
        string html = await http.GetStringAsync(url, TestContext.Current.CancellationToken);
        var match = TokenField().Match(html);

        Assert.True(match.Success, $"{url} sayfasında antiforgery token bulunamadı.");
        return match.Groups[1].Value;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenField();
}