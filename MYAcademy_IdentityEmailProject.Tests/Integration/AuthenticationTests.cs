using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

public class AuthenticationTests(MailoraAppFactory factory)
{
    [Fact]
    public async Task SignIn_ValidUser_CanOpenInbox()
    {
        var ada = await factory.SignInNewUserAsync("ada");

        var response = await ada.GetAsync("/Message/Inbox");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_WrongPassword_StaysOnLoginForm()
    {
        var user = await factory.CreateUserAsync("ada");
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        string token = await TestSupport.ReadAntiforgeryTokenAsync(http, "/Login/SignIn");

        var response = await http.PostAsync("/Login/SignIn", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = user.UserName,
            ["Password"] = "Yanlis-Parola-999!",
            ["__RequestVerificationToken"] = token
        }), TestContext.Current.CancellationToken);

        // Yönlendirme YOK: form hata mesajıyla geri gelir
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendMessage_ToRegisteredUser_AppearsInRecipientsInbox()
    {
        var ada = await factory.SignInNewUserAsync("ada");
        var ued = await factory.SignInNewUserAsync("ued");
        string subject = $"Merhaba {Guid.NewGuid():N}";      // benzersiz: başka testin mesajıyla karışmasın

        await ada.SendMessageAsync(subject, to: ued.User.Email);

        string inbox = await ued.GetStringAsync("/Message/Inbox");
        Assert.Contains(subject, inbox);
    }
}