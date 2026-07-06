using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SellerTools.App.Data;
using SellerTools.Core.Entities;
using SellerTools.UzumApi;
using SellerTools.UzumApi.Dto;

namespace SellerTools.App.Services;

/// <summary>Итог синка одного магазина.</summary>
public record ShopSyncResult(long ShopId, int SkusSynced, int Pages, bool Ok, string? Error);

/// <summary>
/// Синхронизация одного магазина: расшифровать ключ, постранично забрать товары,
/// записать SkuDailySnapshot за сегодня (upsert по ShopId+Date+SkuId — идемпотентно).
/// Между страницами следит за остатком дневного лимита и паузится при низком.
/// 429/5xx уже ретраятся в клиенте (RealUzumApiClient), здесь не дублируем.
/// </summary>
public class ShopSyncService
{
    private readonly IUzumApiClient _api;
    private readonly AppDbContext _db;
    private readonly ICryptoService _crypto;
    private readonly IAdminAlertService _alerts;
    private readonly UzumApiOptions _options;
    private readonly ILogger<ShopSyncService> _log;

    public ShopSyncService(
        IUzumApiClient api,
        AppDbContext db,
        ICryptoService crypto,
        IAdminAlertService alerts,
        IOptions<UzumApiOptions> options,
        ILogger<ShopSyncService> log)
    {
        _api = api;
        _db = db;
        _crypto = crypto;
        _alerts = alerts;
        _options = options.Value;
        _log = log;
    }

    /// <summary>
    /// Синкает магазин по его записи (ключ берётся зашифрованным из Shop).
    /// Исключения НЕ пробрасываются — возвращается ShopSyncResult с ошибкой,
    /// чтобы падение одного магазина не роняло всю джобу.
    /// </summary>
    public async Task<ShopSyncResult> SyncShopAsync(Shop shop, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(shop.ApiKeyEncrypted))
                return new ShopSyncResult(shop.Id, 0, 0, false, "нет сохранённого ключа");

            var apiKey = _crypto.Decrypt(shop.ApiKeyEncrypted);
            return await SyncAsync(shop.Id, apiKey, ct);
        }
        catch (Exception ex)
        {
            // Протухший ключ, 403, битый шифртекст и т.п. — изолируем.
            _log.LogWarning(ex, "Синк магазина {ShopId} завершился ошибкой: {Msg}", shop.Id, ex.Message);
            return new ShopSyncResult(shop.Id, 0, 0, false, ex.Message);
        }
    }

    /// <summary>
    /// Свежие SKU магазина из API (для Excel-выгрузок: нужны штрихкод/артикул/ИКПУ).
    /// Расшифровывает ключ. Бросает при ошибке — вызывающий обрабатывает.
    /// </summary>
    public async Task<IReadOnlyList<SkuDto>> GetFreshSkusAsync(Shop shop, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(shop.ApiKeyEncrypted))
            throw new InvalidOperationException("нет сохранённого ключа");

        var apiKey = _crypto.Decrypt(shop.ApiKeyEncrypted);
        var products = await _api.GetAllProductsAsync(shop.Id, apiKey, ct);
        return products.SelectMany(p => p.SkuList).ToList();
    }

    /// <summary>Момент последнего синка магазина (max CapturedAt снапшотов) или null.</summary>
    public async Task<DateTimeOffset?> GetLastSyncAtAsync(long shopId, CancellationToken ct = default)
    {
        var times = await _db.SkuDailySnapshots
            .Where(x => x.ShopId == shopId)
            .Select(x => x.CapturedAt)
            .ToListAsync(ct);
        return times.Count == 0 ? null : times.Max();
    }

    /// <summary>
    /// Синк по shopId + открытому ключу (для разового синка из бота, когда ключ
    /// уже расшифрован). Тоже не бросает.
    /// </summary>
    public async Task<ShopSyncResult> SyncAsync(
        long shopId, string apiKey, CancellationToken ct = default)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var totalSkus = 0;
            var page = 0;

            while (true)
            {
                var result = await _api.GetProductsPageAsync(
                    shopId, apiKey, page, _options.PageSize, ct);

                var skus = result.Products.SelectMany(p => p.SkuList).ToList();
                await UpsertSnapshotsAsync(shopId, today, skus, ct);
                totalSkus += skus.Count;

                if (result.IsLast)
                {
                    page++; // для счётчика страниц
                    break;
                }

                await RespectRateLimitAsync(shopId, result, ct);
                page++;
            }

            _log.LogInformation(
                "Синк магазина {ShopId}: {Skus} SKU за {Pages} стр. → снапшоты за {Date}",
                shopId, totalSkus, page, today);
            return new ShopSyncResult(shopId, totalSkus, page, true, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Синк магазина {ShopId} завершился ошибкой: {Msg}", shopId, ex.Message);
            return new ShopSyncResult(shopId, 0, 0, false, ex.Message);
        }
    }

    /// <summary>Пауза, если остаток дневного лимита ниже порога.</summary>
    private async Task RespectRateLimitAsync(long shopId, ProductsPage page, CancellationToken ct)
    {
        if (page.RemainingPerDay is not { } remaining || page.LimitPerDay is not { } limit || limit <= 0)
            return;

        var ratio = (double)remaining / limit;
        if (ratio < _options.RateLimitPauseThreshold)
        {
            _log.LogWarning(
                "Магазин {ShopId}: остаток дневного лимита {Remaining}/{Limit} ({Pct:P0}) " +
                "ниже порога {Threshold:P0} — пауза {Pause}мс.",
                shopId, remaining, limit, ratio, _options.RateLimitPauseThreshold, _options.RateLimitPauseMs);
            await _alerts.AlertAsync(AlertKind.RateLimitLow,
                $"Магазин {shopId}: остаток дневного лимита {remaining}/{limit} ({ratio:P0}) " +
                $"ниже порога {_options.RateLimitPauseThreshold:P0}. Синк поставлен на паузу.", ct);
            await Task.Delay(_options.RateLimitPauseMs, ct);
        }
    }

    /// <summary>
    /// Upsert снапшотов за дату по уникальному индексу (ShopId, Date, SkuId).
    /// Повторный синк за тот же день перезаписывает строки, а не дублирует.
    /// </summary>
    private async Task UpsertSnapshotsAsync(
        long shopId, DateOnly date, IReadOnlyList<SkuDto> skus, CancellationToken ct)
    {
        if (skus.Count == 0)
            return;

        var skuIds = skus.Select(s => s.SkuId).ToList();
        var existing = await _db.SkuDailySnapshots
            .Where(x => x.ShopId == shopId && x.Date == date && skuIds.Contains(x.SkuId))
            .ToDictionaryAsync(x => x.SkuId, ct);

        foreach (var s in skus)
        {
            if (!existing.TryGetValue(s.SkuId, out var snap))
            {
                snap = new SkuDailySnapshot { ShopId = shopId, Date = date, SkuId = s.SkuId };
                _db.SkuDailySnapshots.Add(snap);
            }

            snap.SkuTitle = s.SkuTitle;
            snap.ProductTitle = s.ProductTitle;
            snap.QuantityActive = s.QuantityActive;
            snap.QuantitySold = s.QuantitySold;
            snap.QuantityReturned = s.QuantityReturned;
            snap.QuantityMissing = s.QuantityMissing;
            snap.QuantityDefected = s.QuantityDefected;
            snap.Price = s.Price;
            snap.PurchasePrice = s.PurchasePrice;
            snap.Ikpu = s.Ikpu;
            snap.SellerItemCode = s.SellerItemCode;
            snap.CapturedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}
