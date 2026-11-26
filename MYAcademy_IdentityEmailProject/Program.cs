using Ganss.Xss;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Identity;
using MYAcademy_IdentityEmailProject.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedEmail = true;
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
})
    .AddEntityFrameworkStores<EmailContext>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<CustomIdentityValidator>()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

// Şifre sıfırlama (ve e-posta değiştirme) bağlantıları 1 saat geçerli.
// Varsayılan süre 1 gündür.
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(1);
});

builder.Services.AddControllersWithViews(options =>
{
    // Varsayılan kural: her controller ve action için giriş zorunlu.
    // Açık olması gerekenler [AllowAnonymous] ile işaretlenir.
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
    // 13.1: CSRF koruması varsayılan olarak açık. POST/PUT/PATCH/DELETE token ister, GET istemez.
    // Action'lardaki [ValidateAntiForgeryToken] öznitelikleri kalabilir: aynı kontrol, zararsız tekrar.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddDbContext<EmailContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
// 13.3: Development'ta SMTP şifresi yoksa (ya da Mail:DevLogOnly açıksa) mailler GÖNDERİLMEZ, loga yazılır.
// Production'da HER ZAMAN gerçek MailService (Kavram 1).
bool useDevMailLog = builder.Environment.IsDevelopment() &&
    (builder.Configuration.GetValue<bool>("Mail:DevLogOnly") ||
     string.IsNullOrEmpty(builder.Configuration["SmtpSettings:Password"]));

if (useDevMailLog)
{
    builder.Services.AddScoped<IMailService, DevelopmentLogMailService>();
}
else
{
    builder.Services.AddScoped<IMailService, MailService>();
}
builder.Services.AddScoped<IMailboxCounter, MailboxCounter>();
builder.Services.AddScoped<IDashboardQueries, DashboardQueries>();
builder.Services.AddScoped<INotificationFeed, NotificationFeed>();
builder.Services.AddSingleton<IAttachmentStorage, AttachmentStorage>();
builder.Services.AddHostedService<OrphanAttachmentCleaner>();   // 12.19: sahipsiz ek dosyası süpürücüsü
builder.Services.AddHostedService<TrashPurger>();             // 15.4: 30 günü dolan çöpü kalıcı siler
builder.Services.AddSingleton(TimeProvider.System);           // 15.4: gerçek saat; testte değiştirilir (15.6)
builder.Services.AddScoped<ITrashService, TrashService>();   // 15.3: kalıcı silme (controller + 15.4 arka plan servisi)
builder.Services.AddMailoraRateLimiting(builder.Configuration);   // 17.2: istek sınırlama kuralları
builder.Services.AddSingleton<ISendRateLimiter, SendRateLimiter>();   // 17.4: gönderme sınırı (action içinden, karar 7-B)

// HTML temizleyici: izin listesindeki etiketleri bırakır, script ve olay özelliklerini siler.
// Singleton: yapılandırması değiştirilmediği sürece bir örneğin paylaşılması güvenli.
builder.Services.AddSingleton<IHtmlSanitizer>(_ => new HtmlSanitizer());

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Login/SignIn";
    options.AccessDeniedPath = "/ErrorPages/Page403";
    options.ExpireTimeSpan = TimeSpan.FromDays(7);

    // Yönlendirme yerine boş gövdeli 401/403 dön;
    // UseStatusCodePagesWithReExecute bunları Page401/Page403'e bağlar.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Boş gövdeli hata yanıtlarında (404, 403, 401) kendi hata sayfamızı göster.
// ReExecute: adres çubuğu ve durum kodu korunur.
app.UseStatusCodePagesWithReExecute("/ErrorPages/Page{0}");

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();   // 17.2: UseRouting'den SONRA (belge), UseAuthentication'dan SONRA (kullanıcı başına kurallar, Kavram 4)

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();


// Entegrasyon testleri (14.6) WebApplicationFactory<Program> ile uygulamayı başlatabilsin diye
public partial class Program { }