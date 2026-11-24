using Microsoft.EntityFrameworkCore;
using MYAcademy_IdentityEmailProject.Context;

namespace MYAcademy_IdentityEmailProject.Services
{
    // Sahipsiz ek dosyası süpürücüsü (12.19): diskte olup veritabanında satırı OLMAYAN dosyaları siler.
    // Kaynakları: başarısız SaveChanges, silinemeyen dosyalar, eşzamanlı silmeler (Kavram 1).
    public class OrphanAttachmentCleaner : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;   // DbContext scoped: her turda yeni scope (Kavram 3)
        private readonly IAttachmentStorage _storage;          // singleton: doğrudan alınabilir
        private readonly ILogger<OrphanAttachmentCleaner> _logger;

        private readonly TimeSpan _interval;      // turlar arası süre
        private readonly TimeSpan _gracePeriod;   // bundan YENİ dosyalara dokunulmaz (Kavram 2)

        public OrphanAttachmentCleaner(IServiceScopeFactory scopeFactory, IAttachmentStorage storage,
                                       ILogger<OrphanAttachmentCleaner> logger, IHostEnvironment env)
        {
            _scopeFactory = scopeFactory;
            _storage = storage;
            _logger = logger;

            // Development'ta kısa süreler: deneyleri saatlerce beklemeden yapabilmek için
            _interval = env.IsDevelopment() ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(6);
            _gracePeriod = env.IsDevelopment() ? TimeSpan.FromMinutes(2) : TimeSpan.FromHours(1);
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
                        await SweepAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Bir turun hatası servisi durdurmasın: logla, sonraki turda tekrar dene
                        _logger.LogError(ex, "Sahipsiz ek temizliği başarısız oldu.");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // Uygulama kapanıyor: normal çıkış
            }
        }

        private async Task SweepAsync(CancellationToken ct)
        {
            // 1) Bekleme süresinden eski dosyalar (Kavram 2). Zaman UTC: dosya sistemi de UTC tutar.
            var cutoff = DateTime.UtcNow - _gracePeriod;
            var candidates = _storage.ListFiles()
                .Where(f => f.LastWriteUtc < cutoff)
                .Select(f => f.StoredName)
                .ToList();

            if (candidates.Count == 0)
            {
                return;
            }

            // 2) Bunlardan hangilerinin veritabanında satırı var? Parçalar halinde: her sorgu sınırlı boyutta kalsın.
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<EmailContext>();

            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chunk in candidates.Chunk(500))
            {
                var names = chunk.ToList();
                known.UnionWith(await context.Attachments
                    .Where(a => names.Contains(a.StoredName))
                    .Select(a => a.StoredName)
                    .ToListAsync(ct));
            }

            // 3) Satırı olmayanlar sahipsiz: sil
            int deleted = 0;
            foreach (string name in candidates.Where(n => !known.Contains(n)))
            {
                _storage.Delete(name);
                deleted++;
            }

            if (deleted > 0)
            {
                // Sadece sayı: dosya adları kişisel veri değil ama gereksiz ayrıntı
                _logger.LogInformation("Sahipsiz ek temizliği: {Count} dosya silindi.", deleted);
            }
        }
    }
}