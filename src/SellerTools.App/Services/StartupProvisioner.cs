using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.App.Jobs;
using SellerTools.Core.Entities;

namespace SellerTools.App.Services;

/// <summary>
/// Приёмка при старте: для каждого привязанного магазина БЕЗ строки ShopSyncState
/// (т.е. существовавшего до перехода на «синк → БД») создаёт состояние Never и ставит
/// первичный полный синк с ИНТЕРВАЛОМ (чтобы не завалить Uzum всеми магазинами сразу).
/// Идемпотентно: магазины, у которых состояние уже есть, не трогаются.
/// </summary>
public static class StartupProvisioner
{
    // Шаг между первичными синками разных магазинов (страховка от пиковой нагрузки).
    private static readonly TimeSpan Spacing = TimeSpan.FromMinutes(2);

    public static async Task ProvisionAsync(
        AppDbContext db, IBackgroundJobClient jobs, ILogger logger, CancellationToken ct = default)
    {
        var shopIds = await db.Shops.Select(s => s.Id).ToListAsync(ct);
        var haveState = await db.ShopSyncStates.Select(x => x.ShopId).ToListAsync(ct);
        var needState = shopIds.Except(haveState).ToList();

        if (needState.Count == 0)
        {
            logger.LogInformation("Приёмка: все магазины уже имеют состояние синка.");
            return;
        }

        logger.LogWarning(
            "Приёмка: {Count} магазин(ов) без состояния синка — помечаю Never и ставлю " +
            "первичные синки с интервалом {Spacing}.", needState.Count, Spacing);

        foreach (var id in needState)
        {
            db.ShopSyncStates.Add(new ShopSyncState { ShopId = id, Status = SyncStatus.Never });
        }
        await db.SaveChangesAsync(ct);

        // Первичные синки — с нарастающей задержкой (0, 2, 4, … минут).
        var delay = TimeSpan.Zero;
        foreach (var id in needState)
        {
            var shopId = id;
            jobs.Schedule<UzumSyncJob>(j => j.SyncShopAsync(shopId, CancellationToken.None), delay);
            delay += Spacing;
        }
    }
}
