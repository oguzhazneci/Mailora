using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Services;

// Bütün test çalıştırması için TEK fabrika (Kavram 3)
[assembly: AssemblyFixture(typeof(MYAcademy_IdentityEmailProject.Tests.Integration.MailoraAppFactory))]

namespace MYAcademy_IdentityEmailProject.Tests.Integration;

// Uygulamayı test için kurar: ayrı veritabanı, sahte mail, geçici ek klasörü, arka plan servisleri kapalı,
// donmuş saat (14.6.a Kavram 2; 15.6)
public sealed class MailoraAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; } = $"MailoraTest_{Guid.NewGuid():N}";
    public string AttachmentFolder { get; } = Path.Combine(Path.GetTempPath(), "MailoraTest", Guid.NewGuid().ToString("N"));
    public FakeMailService Mail { get; } = new();

    // 15.6: Uygulamanın saati. Kurulduğu anda DONAR ve testler onu değiştirmez (Kavram 2):
    // tarihleri saate göre ayarlayan VERİDİR. Yerel saat dilimi UTC (Kavram 3).
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    // CI'da MAILORA_TEST_SQL ortam değişkeni (SQL Server konteyneri), yerelde LocalDB (karar 3-A).
    // Sunucu ondan gelir, veritabanı adı her çalıştırmada benzersizdir.
    private string ConnectionString
    {
        get
        {
            string server = Environment.GetEnvironmentVariable("MAILORA_TEST_SQL")
                ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
            return new SqlConnectionStringBuilder(server) { InitialCatalog = DatabaseName }.ConnectionString;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development DEĞİL: User Secrets yüklenmez, gerçek SMTP'ye giden yol kapanır (14.6.a Kavram 3)
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["Attachments:Folder"] = AttachmentFolder
        }));

        // Program.cs'teki kayıtlardan SONRA çalışır: kaldırıp yerine koyabiliriz (Kavram 2)
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMailService>();
            services.AddSingleton<IMailService>(Mail);

            // Sadece bizim arka plan servislerimiz: web sunucusunun kendisi de bir IHostedService, hepsini silemeyiz.
            // Kendi zamanlamasıyla çalışan bir şey test sonucunu zamanlamaya bağlar (14.9, soru 7).
            var cleaner = services.Single(d => d.ImplementationType == typeof(OrphanAttachmentCleaner));
            services.Remove(cleaner);

            var purger = services.Single(d => d.ImplementationType == typeof(TrashPurger));   // 15.6
            services.Remove(purger);

            // 15.6: gerçek saat yerine donmuş sahte saat. Servis (PurgeExpiredAsync) ve görünüm (rozet) ikisi de buradan okur.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    // Bütün testlerden ÖNCE bir kez: veritabanını migration'larla oluştur (kategoriler HasData ile gelir)
    public async ValueTask InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmailContext>();
        await db.Database.MigrateAsync();
    }

    // Bütün testlerden SONRA bir kez: veritabanını ve geçici ek klasörünü sil
    public override async ValueTask DisposeAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<EmailContext>().Database.EnsureDeletedAsync();
        }
        finally
        {
            if (Directory.Exists(AttachmentFolder))
            {
                Directory.Delete(AttachmentFolder, recursive: true);
            }
            await base.DisposeAsync();
        }
    }
}