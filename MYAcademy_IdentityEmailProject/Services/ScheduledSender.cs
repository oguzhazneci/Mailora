namespace MYAcademy_IdentityEmailProject.Services
{
    // 18.5: Zamanı gelen mesajları belirli aralıklarla gönderir.
    // Kalıp TrashPurger ile aynı (15.4): kural ScheduledSendService'te, burada sadece zamanlama (Kavram 4).
    public class ScheduledSender : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;   // servis scoped (EmailContext): her turda yeni scope
        private readonly ILogger<ScheduledSender> _logger;
        private readonly TimeSpan _interval;

        public ScheduledSender(IServiceScopeFactory scopeFactory, ILogger<ScheduledSender> logger, IHostEnvironment env)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;

            // Mesaj seçilen dakikadan en fazla bu kadar sonra gider
            _interval = env.IsDevelopment() ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(30);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // İlk tur kısa bir gecikmeyle: yeniden başlatmada zamanı gelenler beklemesin (Kavram 4)
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

                using var timer = new PeriodicTimer(_interval);
                do
                {
                    try
                    {
                        await SendOnceAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Bir turun hatası servisi durdurmasın: logla, sonraki turda tekrar dene
                        _logger.LogError(ex, "Zamanlanmış gönderim turu başarısız oldu.");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // Uygulama kapanıyor: normal çıkış
            }
        }

        private async Task SendOnceAsync(CancellationToken ct)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IScheduledSendService>();

            int sent = await service.SendDueAsync(ct);
            if (sent > 0)
            {
                // Sadece sayı: kimin neyi kime gönderdiği loga yazılmaz (8.4)
                _logger.LogInformation("Zamanlanmış gönderim: {Count} mesaj gönderildi.", sent);
            }
        }
    }
}