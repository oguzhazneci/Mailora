using Ganss.Xss;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Identity;
using MYAcademy_IdentityEmailProject.Context;
using MYAcademy_IdentityEmailProject.Entities;
using MYAcademy_IdentityEmailProject.Services;
using MYAcademy_IdentityEmailProject.Identity;

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
});

builder.Services.AddDbContext<EmailContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<IMailService, MailService>();
builder.Services.AddScoped<IMailboxCounter, MailboxCounter>();
builder.Services.AddScoped<IDashboardQueries, DashboardQueries>();

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

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();