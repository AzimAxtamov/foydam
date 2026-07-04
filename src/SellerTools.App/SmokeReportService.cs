using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.App.Services;
using SellerTools.Core.Entities;

namespace SellerTools.App;

/// <summary>
/// Smoke-тест В1: поднимает схему БД, гарантирует мок-магазин, строит отчёт
/// «Сколько Uzum вам должен» и печатает его в консоль. Снапшоты за сегодня
/// оседают в Postgres.
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
        var reportSvc = scope.ServiceProvider.GetRequiredService<LossReportService>();

        // В1: EnsureCreated (миграции — позже).
        _log.LogInformation("Инициализация схемы БД…");
        await db.Database.EnsureCreatedAsync(ct);

        await EnsureMockShopAsync(db, ct);

        // Ключ мок игнорирует; в Real сюда пойдёт расшифрованный ключ из Shop.
        var report = await reportSvc.BuildAsync(
            MockShopId, apiKey: "mock-key", shopTitle: MockShopTitle, ct: ct);

        Console.WriteLine(ConsoleReportRenderer.Render(report, lang: "ru"));

        var snapCount = await db.SkuDailySnapshots
            .CountAsync(x => x.ShopId == MockShopId
                             && x.Date == DateOnly.FromDateTime(DateTime.UtcNow), ct);
        _log.LogInformation(
            "В Postgres строк снапшотов за сегодня по магазину {ShopId}: {Count}",
            MockShopId, snapCount);

        // Smoke-тест отработал — гасим хост, чтобы процесс завершился.
        _lifetime.StopApplication();
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static async Task EnsureMockShopAsync(AppDbContext db, CancellationToken ct)
    {
        if (!await db.Sellers.AnyAsync(x => x.Id == MockSellerId, ct))
            db.Sellers.Add(new Seller { Id = MockSellerId, Language = "ru" });

        if (!await db.Shops.AnyAsync(x => x.Id == MockShopId, ct))
            db.Shops.Add(new Shop
            {
                Id = MockShopId,
                SellerId = MockSellerId,
                Title = MockShopTitle,
            });

        await db.SaveChangesAsync(ct);
    }
}
