using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Services;

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// Test altyapısı gerçekten izole mi? Bunlar kırmızıysa diğer entegrasyon testlerine güvenilemez.
public class InfrastructureTests(MailoraAppFactory factory)
{
    [Fact]
    public void Database_IsTheIsolatedTestDatabase()
    {
        using var scope = factory.Services.CreateScope();
        string database = scope.ServiceProvider.GetRequiredService<EmailContext>().Database.GetDbConnection().Database;

        Assert.Equal(factory.DatabaseName, database);
        Assert.NotEqual("IdentityEmailDb", database);   // asla gerçek veritabanı
    }

    [Fact]
    public void MailService_IsTheFake()
    {
        using var scope = factory.Services.CreateScope();

        Assert.IsType<FakeMailService>(scope.ServiceProvider.GetRequiredService<IMailService>());
    }

    [Fact]
    public void BackgroundCleaner_IsNotRegistered()
    {
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), s => s is OrphanAttachmentCleaner);
    }

    [Fact]
    public async Task Migrations_SeedTenCategories()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmailContext>();

        Assert.Equal(10, await db.Categories.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnonymousRequest_ToInbox_Returns401()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Message/Inbox", TestContext.Current.CancellationToken);

        // Global AuthorizeFilter (3.2) + OnRedirectToLogin → 401 (yönlendirme değil)
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}