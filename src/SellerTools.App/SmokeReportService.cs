using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.App.Services;
using SellerTools.Core.Entities;

namespace SellerTools.App;

/// <summary>
/// Smoke-тест: применяет миграции, гарантирует мок-магазин, СИНКАЕТ его в Postgres
/// (UzumSyncService), затем строит отчёт «Сколько Uzum вам должен» ИЗ БД и печатает
/// в консоль. Модель «синк → Postgres → отчёт из БД» в миниатюре.
/// </summary>
public class SmokeReportService : IHostedService
{
    private const long MockShopId = 777001;
    private const long MockSellerId = 111;
    private const string MockShopTitle = "Демо-магазин";

    private readonly IServiceProvider _sp;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<SmokeReportService> _log;

    public SmokeReportService(
        IServiceProvider sp, IHostApplicationLifetime lifetime, ILogger<SmokeReportService> log)
    {
        _sp = sp;
        _lifetime = lifetime;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<ICryptoService>();
        var sync = scope.ServiceProvider.GetRequiredService<UzumSyncService>();
        var reportSvc = scope.ServiceProvider.GetRequiredService<LossReportService>();

        _log.LogInformation("Инициализация схемы БД (миграции)…");
        await MigrationBaseline.EnsureAsync(db, _log);
        await db.Database.MigrateAsync(ct);

        var shop = await EnsureMockShopAsync(db, crypto, ct);

        // Синк мок-магазина в Postgres (накладные + возвраты + карточки).
        var syncResult = await sync.SyncShopAsync(shop, ct);
        _log.LogInformation("Синк мок-магазина: ok={Ok} error={Err}", syncResult.Ok, syncResult.Error);

        // Отчёт строится ТОЛЬКО из БД.
        var report = await reportSvc.BuildAsync(shop, ct);
        Console.WriteLine(ConsoleReportRenderer.Render(report, lang: "ru"));

        var snapCount = await db.SkuDailySnapshots
            .CountAsync(x => x.ShopId == MockShopId
                             && x.Date == DateOnly.FromDateTime(DateTime.UtcNow), ct);
        _log.LogInformation(
            "В Postgres строк снапшотов за сегодня по магазину {ShopId}: {Count}",
            MockShopId, snapCount);

        _lifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task<Shop> EnsureMockShopAsync(
        AppDbContext db, ICryptoService crypto, CancellationToken ct)
    {
        if (!await db.Sellers.AnyAsync(x => x.Id == MockSellerId, ct))
            db.Sellers.Add(new Seller { Id = MockSellerId, Language = "ru" });

        var shop = await db.Shops.FirstOrDefaultAsync(x => x.Id == MockShopId, ct);
        if (shop is null)
        {
            shop = new Shop { Id = MockShopId, SellerId = MockSellerId, Title = MockShopTitle };
            db.Shops.Add(shop);
        }
        // Ключ мок игнорирует, но SyncShopAsync требует непустой шифртекст.
        shop.ApiKeyEncrypted = crypto.Encrypt("mock-key");

        await db.SaveChangesAsync(ct);
        return shop;
    }
}
