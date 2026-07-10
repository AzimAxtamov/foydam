using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SellerTools.App.Configuration;
using SellerTools.App.Data;
using SellerTools.App.Services;
using SellerTools.Core.Entities;

namespace SellerTools.App.Jobs;

/// <summary>
/// Hangfire-обёртка синхронизации. ВСЕ методы ставятся в очередь uzum-sync (сервер с
/// ровно 3 воркерами — глобальный лимит параллелизма к Uzum). Внутри — строго
/// последовательные запросы через UzumSyncService.
/// </summary>
[Queue(Queue)]
public class UzumSyncJob
{
    public const string Queue = "uzum-sync";

    private readonly AppDbContext _db;
    private readonly UzumSyncService _sync;
    private readonly SyncOptions _options;
    private readonly ILogger<UzumSyncJob> _log;

    public UzumSyncJob(
        AppDbContext db, UzumSyncService sync, IOptions<SyncOptions> options, ILogger<UzumSyncJob> log)
    {
        _db = db;
        _sync = sync;
        _options = options.Value;
        _log = log;
    }

    /// <summary>Синк одного магазина (полный при state=Never, иначе дельта). Для кнопки/приёмки.</summary>
    public async Task SyncShopAsync(long shopId, CancellationToken ct = default)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, ct);
        if (shop is null)
        {
            _log.LogWarning("Синк: магазин {ShopId} не найден — пропуск.", shopId);
            return;
        }

        var result = await _sync.SyncShopAsync(shop, ct);
        if (!result.Ok && !result.Inactive)
            _log.LogWarning("Синк магазина {ShopId} не удался: {Error}", shopId, result.Error);
    }

    /// <summary>
    /// Ночной синк всех магазинов + запись sku_daily_snapshots. Пропускает неактивные.
    /// Ошибка одного не роняет остальные (UzumSyncService не пробрасывает исключения).
    /// Между магазинами — СЛУЧАЙНЫЙ сдвиг (джиттер), чтобы не бить в Uzum синхронно.
    /// </summary>
    public async Task NightlyAllAsync(CancellationToken ct = default)
    {
        var shops = await _db.Shops.AsNoTracking().ToListAsync(ct);
        _log.LogInformation("Ночной синк: магазинов {Count}", shops.Count);

        var jitterMax = Math.Max(0, _options.NightlyJitterSeconds);
        var first = true;
        foreach (var shop in shops)
        {
            ct.ThrowIfCancellationRequested();

            // Случайный сдвиг перед каждым магазином (кроме первого): 0..NightlyJitterSeconds.
            if (!first && jitterMax > 0)
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(0, jitterMax)), ct);
            first = false;

            var result = await _sync.SyncShopAsync(shop, ct);
            _log.LogInformation(
                "Ночной синк {ShopId}: ok={Ok} inactive={Inactive} {Error}",
                shop.Id, result.Ok, result.Inactive, result.Error);
        }
    }
}
