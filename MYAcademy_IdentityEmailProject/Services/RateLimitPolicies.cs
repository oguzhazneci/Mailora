using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace MYAcademy_IdentityEmailProject.Services
{
    // 17.2: İstek sınırlama kuralları. Action'lar [EnableRateLimiting(RateLimitPolicies.Auth)] ile bağlanır (17.3, 17.4).
    public static class RateLimitPolicies
    {
        public const string Auth = "auth";               // IP: giriş, kod adımları, doğrulama, şifre sıfırlama
        public const string AuthMail = "auth-mail";      // IP: girilen adrese mail gönderen uçlar (mail bombing)
        public const string Lookup = "lookup";           // IP: kullanıcı adı kontrolü (numaralandırma) — JSON
        public const string UserUpload = "user-upload";  // kullanıcı: ek yükleme — JSON

        // Bu kurallar AJAX uçlarında: reddedilince HTML sayfası değil JSON (Kavram 3)
        private static readonly HashSet<string> JsonPolicies = [Lookup, UserUpload];

        // Bekleme süresini 429 sayfasına taşır (aynı HttpContext, Kavram 3)
        public const string RetryAfterItemKey = "RateLimit.RetryAfterSeconds";

        public static IServiceCollection AddMailoraRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            // Hatalı ayarla uygulama AÇILMAZ (Kavram 2)
            services.AddOptions<RateLimitSettings>()
                .Bind(configuration.GetSection(RateLimitSettings.SectionName))
                .Validate(s => s.IsValid, "RateLimiting ayarlarında PermitLimit ve WindowSeconds 0'dan büyük olmalı.")
                .ValidateOnStart();

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;   // açıkça: varsayılana güvenmiyoruz

                options.AddPolicy(Auth, http => FixedWindow(http, ByIp(http), s => s.Auth));
                options.AddPolicy(AuthMail, http => FixedWindow(http, ByIp(http), s => s.AuthMail));
                options.AddPolicy(Lookup, http => FixedWindow(http, ByIp(http), s => s.Lookup));
                options.AddPolicy(UserUpload, http => FixedWindow(http, ByUser(http), s => s.UserUpload));

                options.OnRejected = OnRejectedAsync;
            });

            return services;
        }

        // Giriş yapmamış istemcinin tek kimliği IP (17.1 Kavram 3; karar 1-A). Test sunucusunda IP yok → "unknown".
        private static string ByIp(HttpContext http) =>
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Giriş yapmış kullanıcı: Id. Aynı ofisteki herkes aynı IP'yi paylaşabilir, aynı Id'yi değil.
        private static string ByUser(HttpContext http) =>
            http.User.FindFirstValue(ClaimTypes.NameIdentifier) is string id ? $"user:{id}" : $"ip:{ByIp(http)}";

        // Sabit pencere (karar 2-A). Değerler istek anında ayarlardan (Kavram 1); kuyruk yok: aşan istek hemen reddedilir.
        private static RateLimitPartition<string> FixedWindow(HttpContext http, string key, Func<RateLimitSettings, RateLimitRule> pick)
        {
            var rule = pick(http.RequestServices.GetRequiredService<IOptions<RateLimitSettings>>().Value);

            return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rule.PermitLimit,
                Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                QueueLimit = 0
            });
        }

        private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
        {
            var http = context.HttpContext;

            // Sabit pencere, bir sonraki pencerenin ne zaman açılacağını bilir (17.1 Kavram 4)
            int? retrySeconds = null;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            {
                retrySeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                http.Response.Headers.RetryAfter = retrySeconds.Value.ToString(CultureInfo.InvariantCulture);
                http.Items[RetryAfterItemKey] = retrySeconds.Value;
            }

            string? policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

            // Kural ve yol: hangi ucun baskı altında olduğunu görmek için. IP ve kullanıcı loga yazılmaz (8.4)
            http.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Mailora.RateLimiting")
                .LogWarning("İstek sınırı aşıldı. Kural: {Policy}, Yol: {Path}", policy, http.Request.Path);

            if (policy != null && JsonPolicies.Contains(policy))
            {
                await http.Response.WriteAsJsonAsync(new { error = Message(retrySeconds) }, cancellationToken);
            }
            // Diğerleri: gövde BOŞ → UseStatusCodePagesWithReExecute → /ErrorPages/Page429 (Kavram 3)
        }

        public static string Message(int? retrySeconds) =>
            $"Çok fazla istek gönderdiniz. Lütfen {FormatWait(retrySeconds)} sonra tekrar deneyin.";

        public static string FormatWait(int? seconds) => seconds switch
        {
            null or <= 0 => "biraz",
            < 60 => $"{seconds} saniye",
            _ => $"{(int)Math.Ceiling(seconds.Value / 60.0)} dakika"
        };
    }
}