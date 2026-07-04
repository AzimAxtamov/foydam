using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.App.Services;

namespace SellerTools.App.Jobs;

/// <summary>
/// Ночная джоба синка: по каждому привязанному магазину пишет снапшоты за сегодня.
/// Ошибка одного магазина (протухший ключ, 403) НЕ роняет джобу — остальные
/// продолжают, сбойный магазин помечается в логе. Вызывается Hangfire по крону
/// (03:00 Asia/Tashkent) и вручную из дашборда.
/// </summary>
public class NightlySyncJob
{
    private readonly AppDbContext _db;
    private readonly ShopSyncService _sync;
    private readonly ILogger<NightlySyncJob> _log;

    public NightlySyncJob(AppDbContext db, ShopSyncService sync, ILogger<NightlySyncJob> log)
    {
        _db = db;
        _sync = sync;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var shops = await _db.Shops.AsNoTracking().ToListAsync(ct);
        _log.LogInformation("Ночной синк: магазинов к обработке {Count}", shops.Count);

        var ok = 0;
        var failed = 0;

        foreach (var shop in shops)
        {
            ct.ThrowIfCancellationRequested();

            var result = await _sync.SyncShopAsync(shop, ct);
            if (result.Ok)
            {
                ok++;
                _log.LogInformation(
                    "✓ Магазин {ShopId}: {Skus} SKU за {Pages} стр.",
                    result.ShopId, result.SkusSynced, result.Pages);
            }
            else
            {
                failed++;
                // Изоляция: логируем и идём дальше.
                _log.LogWarning(
                    "✗ Магазин {ShopId}: синк не удался — {Error}", result.ShopId, result.Error);
            }
        }

        _log.LogInformation(
            "Ночной синк завершён: успешно {Ok}, с ошибкой {Failed}, всего {Total}",
            ok, failed, shops.Count);
    }
}
