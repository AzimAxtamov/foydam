using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.Core.Entities;
using SellerTools.Core.Reports;
using SellerTools.UzumApi;

namespace SellerTools.App.Services;

/// <summary>
/// Тянет товары магазина через IUzumApiClient, сохраняет ежедневные снапшоты
/// (ров данных) и строит отчёт «Сколько Uzum вам должен».
/// </summary>
public class LossReportService
{
    private readonly IUzumApiClient _api;
    private readonly AppDbContext _db;
    private readonly ILogger<LossReportService> _log;

    public LossReportService(IUzumApiClient api, AppDbContext db, ILogger<LossReportService> log)
    {
        _api = api;
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Синхронизирует снапшоты за сегодня и возвращает отчёт по магазину.
    /// </summary>
    public async Task<LossReport> BuildAsync(
        long shopId, string apiKey, string? shopTitle = null, CancellationToken ct = default)
    {
        var products = await _api.GetAllProductsAsync(shopId, apiKey, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Плоский список SKU по всем товарам.
        var skus = products.SelectMany(p => p.SkuList).ToList();

        await PersistSnapshotsAsync(shopId, today, skus, ct);

        var report = new LossReport
        {
            ShopId = shopId,
            ShopTitle = shopTitle,
            Date = today,
            TotalSkusScanned = skus.Count,
        };

        foreach (var s in skus)
        {
            if (s.QuantityMissing + s.QuantityDefected <= 0)
                continue;

            report.Rows.Add(new LossReportRow
            {
                SkuId = s.SkuId,
                SkuTitle = s.SkuTitle ?? "",
                ProductTitle = s.ProductTitle ?? "",
                Missing = s.QuantityMissing,
                Defected = s.QuantityDefected,
                Price = s.Price,
                PurchasePrice = s.PurchasePrice,
            });
        }

        // Сортируем по убыванию оценки (сверху — где больше всего денег).
        report.Rows = report.Rows
            .OrderByDescending(r => r.EstimateBySalePrice)
            .ToList();

        _log.LogInformation(
            "Отчёт по магазину {ShopId}: {Rows} проблемных SKU из {Total}",
            shopId, report.Rows.Count, skus.Count);

        return report;
    }

    /// <summary>
    /// Upsert снапшотов за дату по уникальному индексу (ShopId, Date, SkuId).
    /// </summary>
    private async Task PersistSnapshotsAsync(
        long shopId, DateOnly date, IReadOnlyList<UzumApi.Dto.SkuDto> skus, CancellationToken ct)
    {
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

        var saved = await _db.SaveChangesAsync(ct);
        _log.LogInformation("Снапшоты за {Date}: сохранено строк {Saved}", date, saved);
    }
}
