using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.Core.Entities;
using SellerTools.Core.Reports;
using SellerTools.UzumApi;
using SellerTools.UzumApi.Dto;

namespace SellerTools.App.Services;

/// <summary>
/// Строит отчёт «Сколько Uzum вам должен» балансовой реконсиляцией на 1 SKU:
///   lost = accepted − removed − sold − defected − stock
/// где:
///   accepted — принято складом (/v1/invoice, только накладные ACCEPTED),
///   removed  — физвывоз со склада (/v1/return, legacy-правило),
///   sold     — quantitySold карточки (уже НЕТТО, ничего не вычитать),
///   defected — quantityDefected карточки,
///   stock    — quantityActive карточки.
/// Матч SKU между источниками — ТОЛЬКО по skuTitle (id в накладных/возвратах —
/// внутренние, не skuId). Отрицательный lost клампится в 0 (рассинхрон снапшота).
///
/// ⚠️ quantityMissing из карточки БОЛЬШЕ НЕ ИСПОЛЬЗУЕТСЯ: поле deprecated и всегда
/// возвращает 0. quantityReturned — это КЛИЕНТСКИЕ возвраты, в формуле НЕ участвует.
///
/// Снапшоты (SkuDailySnapshot) продолжаем копить для истории количеств, но lost из
/// снапшота не считаем — accepted/removed там нет. Поэтому отчёт строится по API.
/// </summary>
public class LossReportService
{
    private readonly IUzumApiClient _api;
    private readonly AppDbContext _db;
    private readonly ICryptoService _crypto;
    private readonly ILogger<LossReportService> _log;

    public LossReportService(
        IUzumApiClient api, AppDbContext db, ICryptoService crypto, ILogger<LossReportService> log)
    {
        _api = api;
        _db = db;
        _crypto = crypto;
        _log = log;
    }

    /// <summary>
    /// Отчёт по записи магазина: расшифровывает ключ и строит через API.
    /// Бросает при ошибке (протухший ключ, 403 и т.п.) — вызывающий обрабатывает.
    /// </summary>
    public Task<LossReport> BuildAsync(Shop shop, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(shop.ApiKeyEncrypted))
            throw new InvalidOperationException("нет сохранённого ключа");

        var apiKey = _crypto.Decrypt(shop.ApiKeyEncrypted);
        return BuildAsync(shop.Id, apiKey, shop.Title, ct);
    }

    /// <summary>
    /// Тянет товары + накладные + возвраты, сохраняет снапшот количеств за сегодня
    /// и строит отчёт балансовой реконсиляцией.
    /// </summary>
    public async Task<LossReport> BuildAsync(
        long shopId, string apiKey, string? shopTitle = null, CancellationToken ct = default)
    {
        var products = await _api.GetAllProductsAsync(shopId, apiKey, ct);
        var invoices = await _api.GetAllInvoicesAsync(shopId, apiKey, ct);
        var returns = await _api.GetAllReturnsAsync(shopId, apiKey, ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var skus = products.SelectMany(p => p.SkuList).ToList();

        await PersistSnapshotsAsync(shopId, today, skus, ct);

        // accepted по skuTitle: суммируем quantityAccepted ТОЛЬКО по ACCEPTED-накладным.
        var acceptedByTitle = AggregateAccepted(invoices);
        // removed по skuTitle: физвывоз по legacy-правилу.
        var removedByTitle = AggregateRemoved(returns);

        var report = new LossReport
        {
            ShopId = shopId,
            ShopTitle = shopTitle,
            Date = today,
            TotalSkusScanned = skus.Count,
        };

        foreach (var s in skus)
        {
            var title = s.SkuTitle ?? "";
            var accepted = acceptedByTitle.GetValueOrDefault(title, 0);
            var removed = removedByTitle.GetValueOrDefault(title, 0);

            var row = BuildRow(s, accepted, removed);
            if (row.Lost > 0)
                report.Rows.Add(row);
        }

        report.Rows = report.Rows
            .OrderByDescending(r => r.EstimateBySalePrice)
            .ToList();

        _log.LogInformation(
            "Отчёт по магазину {ShopId}: {Rows} SKU с потерями из {Total} (накладных {Inv}, возвратов {Ret})",
            shopId, report.Rows.Count, skus.Count, invoices.Count, returns.Count);

        return report;
    }

    /// <summary>
    /// Балансовая строка по одному SKU. Отрицательный lost → 0 + warning
    /// (это рассинхрон снапшота, а не потеря; селлеру отрицательное не показываем).
    /// </summary>
    private LossReportRow BuildRow(SkuDto s, int accepted, int removed)
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

    /// <summary>
    /// accepted по skuTitle: сумма quantityAccepted по всем строкам SKU в накладных,
    /// у которых invoiceStatus.value == "ACCEPTED". Черновики (CREATED) игнорируем.
    /// </summary>
    private static Dictionary<string, int> AggregateAccepted(IReadOnlyList<InvoiceDto> invoices)
    {
        var acc = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var inv in invoices)
        {
            if (!string.Equals(inv.InvoiceStatus?.Value, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var product in inv.Products)
            foreach (var sku in product.Skus)
            {
                var title = sku.SkuTitle;
                if (string.IsNullOrEmpty(title))
                    continue;
                acc[title] = acc.GetValueOrDefault(title, 0) + sku.QuantityAccepted;
            }
        }
        return acc;
    }

    /// <summary>
    /// removed по skuTitle: физвывоз со склада. Legacy-правило на РОДИТЕЛЬСКОМ возврате:
    /// если totalPackedAmount==0 И assembledDate==null → берём amount строки; иначе
    /// packedAmount. Матч по skuTitle.
    /// </summary>
    private static Dictionary<string, int> AggregateRemoved(IReadOnlyList<SellerReturnDto> returns)
    {
        var rem = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var ret in returns)
        {
            var useAmount = ret.UseAmount;
            foreach (var item in ret.ReturnItems)
            {
                var title = item.SkuTitle;
                if (string.IsNullOrEmpty(title))
                    continue;
                var qty = useAmount ? item.Amount : item.PackedAmount;
                rem[title] = rem.GetValueOrDefault(title, 0) + qty;
            }
        }
        return rem;
    }

    /// <summary>Дата последнего снапшота магазина (или null, если снапшотов ещё нет).</summary>
    public async Task<DateOnly?> GetLatestSnapshotDateAsync(long shopId, CancellationToken ct = default)
    {
        var dates = await _db.SkuDailySnapshots
            .Where(x => x.ShopId == shopId)
            .Select(x => x.Date)
            .ToListAsync(ct);
        return dates.Count == 0 ? null : dates.Max();
    }

    /// <summary>
    /// Upsert снапшотов количеств за дату по уникальному индексу (ShopId, Date, SkuId).
    /// </summary>
    private async Task PersistSnapshotsAsync(
        long shopId, DateOnly date, IReadOnlyList<SkuDto> skus, CancellationToken ct)
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
            snap.QuantityMissing = s.QuantityMissing;   // deprecated, всегда 0 — копим для истории
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
