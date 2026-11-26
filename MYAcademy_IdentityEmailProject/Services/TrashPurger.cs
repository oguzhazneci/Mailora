namespace MYAcademy_IdentityEmailProject.Services
{
    // 15.4: Çöpte süresi dolan kopyaları belirli aralıklarla kalıcı siler.
    // Kalıp OrphanAttachmentCleaner ile aynı (12.19); silme mantığı TrashService'te, burada sadece zamanlama (Kavram 1).
    public class TrashPurger : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;   // TrashService scoped (EmailContext kullanıyor): her turda yeni scope
        private readonly ILogger<TrashPurger> _logger;
        private readonly TimeSpan _interval;

        public TrashPurger(IServiceScopeFactory scopeFactory, ILogger<TrashPurger> logger, IHostEnvironment env)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            // Development'ta kısa: deneyleri saatlerce beklemeden yapabilmek için (Kavram 3)
            _interval = env.IsDevelopment() ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(6);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // İlk tur: uygulama açıldıktan kısa süre sonra (açılışı yavaşlatmasın)
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                using var timer = new PeriodicTimer(_interval);
                do
                {
                    try
                    {
                        await PurgeOnceAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Bir turun hatası servisi durdurmasın: logla, sonraki turda tekrar dene
                        _logger.LogError(ex, "Çöp kutusu temizliği başarısız oldu.");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // Uygulama kapanıyor: normal çıkış
            }
        }

        private async Task PurgeOnceAsync(CancellationToken ct)
        {
            // CreateAsyncScope: scope kapanırken DbContext asenkron olarak kapatılır
            await using var scope = _scopeFactory.CreateAsyncScope();
            var trash = scope.ServiceProvider.GetRequiredService<ITrashService>();

            int count = await trash.PurgeExpiredAsync(ct);
            if (count > 0)
            {
                // Sadece sayı: kimin hangi mesajı olduğu loga yazılmaz (8.4)
                _logger.LogInformation("Çöp kutusu temizliği: {Count} kopya kalıcı olarak silindi.", count);
            }
        }
    }
}