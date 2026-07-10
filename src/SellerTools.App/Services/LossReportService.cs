using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.Core.Entities;
using SellerTools.Core.Reports;

namespace SellerTools.App.Services;

/// <summary>
/// Строит отчёт «Сколько Uzum вам должен» балансовой реконсиляцией на 1 SKU
/// ТОЛЬКО ИЗ POSTGRES (данные кладёт синк — UzumSyncService). Ноль живых запросов
/// к Uzum: обработчик кнопки читает БД.
///   lost = accepted − removed − sold − defected − stock
/// где:
///   accepted — принято складом (InvoiceItems только по накладным IsTerminal=ACCEPTED),
///   removed  — физвывоз (ReturnItems, legacy-правило зафиксировано в ReturnRecord.UseAmount),
///   sold     — QuantitySold карточки (уже НЕТТО),
///   defected — QuantityDefected карточки,
///   stock    — QuantityActive карточки.
/// Матч SKU между источниками — по НОРМАЛИЗОВАННОМУ ключу SkuKeyNorm (SkuKey.Normalize;
/// пишется при синке из card.skuFullTitle и invoice/return.skuTitle).
///
/// ⚠️ quantityMissing НЕ используется (deprecated, всегда 0). quantityReturned —
/// КЛИЕНТСКИЕ возвраты, в формуле НЕ участвует.
/// </summary>
public class LossReportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<LossReportService> _log;

    public LossReportService(AppDbContext db, ILogger<LossReportService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>Отчёт по записи магазина (данные берутся из БД, ключ не нужен).</summary>
    public Task<LossReport> BuildAsync(Shop shop, CancellationToken ct = default)
        => BuildFromDbAsync(shop.Id, shop.Title, ct);

    /// <summary>Отчёт по shopId — читает Postgres, ничего не тянет из Uzum.</summary>
    public async Task<LossReport> BuildFromDbAsync(
        long shopId, string? shopTitle = null, CancellationToken ct = default)
    {
        var skus = await _db.Skus
            .Where(x => x.ShopId == shopId)
            .AsNoTracking()
            .ToListAsync(ct);

        // accepted по ключу: сумма QuantityAccepted строк накладных, у которых
        // родительская накладная IsTerminal (== ACCEPTED). Черновики исключены.
        var acceptedByKey = await _db.InvoiceItems
            .Where(i => i.Invoice!.ShopId == shopId && i.Invoice.IsTerminal && i.SkuKeyNorm != "")
            .GroupBy(i => i.SkuKeyNorm)
            .Select(g => new { Key = g.Key, Qty = g.Sum(x => x.QuantityAccepted) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        // removed по ключу: по legacy-правилу родителя (UseAmount) берём Amount либо
        // PackedAmount. Считаем в памяти после выборки строк с флагом родителя.
        var returnRows = await _db.ReturnItems
            .Where(r => r.Return!.ShopId == shopId && r.SkuKeyNorm != "")
            .Select(r => new { r.SkuKeyNorm, r.Amount, r.PackedAmount, r.Return!.UseAmount })
            .ToListAsync(ct);

        var removedByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in returnRows)
        {
            var qty = r.UseAmount ? r.Amount : r.PackedAmount;
            removedByKey[r.SkuKeyNorm] = removedByKey.GetValueOrDefault(r.SkuKeyNorm, 0) + qty;
        }

        var dataAsOf = await GetDataAsOfAsync(shopId, ct);

        var report = new LossReport
        {
            ShopId = shopId,
            ShopTitle = shopTitle,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            DataAsOf = dataAsOf,
            TotalSkusScanned = skus.Count,
        };

        foreach (var s in skus)
        {
            var key = s.SkuKeyNorm;
            var accepted = string.IsNullOrEmpty(key) ? 0 : acceptedByKey.GetValueOrDefault(key, 0);
            var removed = string.IsNullOrEmpty(key) ? 0 : removedByKey.GetValueOrDefault(key, 0);

            // Защита от «немого нуля»: было движение (sold/defected>0), но accepted==0 —
            // почти наверняка ключ не сматчился (рассинхрон join'а). Логируем оба ключа.
            if (accepted == 0 && (s.QuantitySold > 0 || s.QuantityDefected > 0))
                _log.LogWarning(
                    "Подозрение на несматченный SKU: skuId={SkuId} skuFullTitle='{Full}' → " +
                    "ключ='{Key}', но accepted=0 при sold={Sold}/defected={Def}. " +
                    "Проверить соответствие ключей invoice/return.",
                    s.SkuId, s.SkuFullTitle, key, s.QuantitySold, s.QuantityDefected);

            var row = BuildRow(s, accepted, removed);
            if (row.Lost > 0)
                report.Rows.Add(row);
        }

        report.Rows = report.Rows
            .OrderByDescending(r => r.EstimateBySalePrice)
            .ToList();

        _log.LogInformation(
            "Отчёт по магазину {ShopId} (из БД): {Rows} SKU с потерями из {Total}, данные на {AsOf:o}",
            shopId, report.Rows.Count, skus.Count, dataAsOf);

        return report;
    }

    /// <summary>Карточки SKU магазина из БД (для Excel «Товары + ИКПУ»).</summary>
    public async Task<IReadOnlyList<SkuRecord>> GetSkusAsync(long shopId, CancellationToken ct = default)
        => await _db.Skus.Where(x => x.ShopId == shopId).AsNoTracking().ToListAsync(ct);

    /// <summary>
    /// Момент актуальности данных: самый свежий UpdatedAt среди карточек магазина
    /// (карточки освежаются при каждом синке, дают самую позднюю точку). null — нет данных.
    /// </summary>
    private async Task<DateTimeOffset?> GetDataAsOfAsync(long shopId, CancellationToken ct)
    {
        var times = await _db.Skus
            .Where(x => x.ShopId == shopId)
            .Select(x => x.UpdatedAt)
            .ToListAsync(ct);
        return times.Count == 0 ? null : times.Max();
    }

    /// <summary>
    /// Балансовая строка по одному SKU. Отрицательный lost → 0 + warning
    /// (рассинхрон, а не потеря; селлеру отрицательное не показываем).
    /// </summary>
    private LossReportRow BuildRow(SkuRecord s, int accepted, int removed)
    {
        var sold = s.QuantitySold;       // уже НЕТТО
        var defected = s.QuantityDefected;
        var stock = s.QuantityActive;

        var rawLost = accepted - removed - sold - defected - stock;
        var lost = rawLost;
        if (lost < 0)
        {
            _log.LogWarning(
                "SKU {SkuId} «{Title}»: отрицательный баланс lost={Raw} " +
                "(accepted={Acc} removed={Rem} sold={Sold} defected={Def} stock={Stock}) — клампим в 0",
                s.SkuId, s.SkuTitle, rawLost, accepted, removed, sold, defected, stock);
            lost = 0;
        }

        return new LossReportRow
        {
            SkuId = s.SkuId,
            SkuTitle = s.SkuTitle ?? "",
            ProductTitle = s.ProductTitle ?? "",
            Article = s.Article ?? s.SellerItemCode,
            Barcode = s.Barcode,
            Lost = lost,
            Accepted = accepted,
            Removed = removed,
            Sold = sold,
            Defected = defected,
            Stock = stock,
            Price = s.Price,
            // Фаза 0: ввода себестоимости нет → COGS всегда 0. purchasePrice из API
            // НЕ подставляем (грязный). Появится ввод в Фазе 1 — прокинем сюда.
            Cogs = 0,
        };
    }
}
