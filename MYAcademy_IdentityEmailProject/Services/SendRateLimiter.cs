using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace MYAcademy_IdentityEmailProject.Services
{
    // 17.4 (karar 7-B): Mesaj gönderme sınırı action'ın İÇİNDEN uygulanır (Kavram 1).
    // Reddedilirse form, yazılanlar korunarak geri döner: mesaj kaybolmaz.
    public interface ISendRateLimiter
    {
        // İzin varsa null; yoksa kullanıcıya gösterilecek mesaj
        ValueTask<string?> TryAcquireAsync(string userId, CancellationToken cancellationToken = default);
    }

    // Singleton: sayaçlar uygulama boyunca yaşamalı (Kavram 2). Ayar: RateLimiting:UserSend.
    public sealed class SendRateLimiter : ISendRateLimiter, IDisposable
    {
        private readonly PartitionedRateLimiter<string> _limiter;

        public SendRateLimiter(IOptions<RateLimitSettings> settings)
        {
            var rule = settings.Value.UserSend;

            // Middleware'in içindeki mekanizmanın aynısı: her kullanıcının kendi sabit penceresi
            _limiter = PartitionedRateLimiter.Create<string, string>(userId =>
                RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rule.PermitLimit,
                    Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                    QueueLimit = 0
                }));
        }

        public async ValueTask<string?> TryAcquireAsync(string userId, CancellationToken cancellationToken = default)
        {
            using RateLimitLease lease = await _limiter.AcquireAsync(userId, 1, cancellationToken);
            if (lease.IsAcquired)
            {
                return null;
            }

            int? seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter)
                ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                : null;

            return $"Kısa sürede çok fazla mesaj gönderdiniz. Mesajınız kaybolmadı; lütfen {RateLimitPolicies.FormatWait(seconds)} sonra tekrar gönderin.";
        }

        public void Dispose() => _limiter.Dispose();
    }
}