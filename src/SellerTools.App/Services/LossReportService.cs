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

        // Ключ матча — НОРМАЛИЗОВАННЫЙ полный код SKU. ⚠️ В карточке полный код лежит
        // в skuFullTitle (skuTitle там короткий, напр. «11193»/«СЕРЫЙ»), а в накладных
        // и возвратах полный код — в ИХ skuTitle. Нормализуем обе стороны (trim, UPPER,
        // схлопывание пробелов), иначе join не сматчится и accepted/removed = 0.
        var acceptedByKey = AggregateAccepted(invoices);
        var removedByKey = AggregateRemoved(returns);

        var report = new LossReport
        {
            ShopId = shopId,
            ShopTitle = shopTitle,
            Date = today,
            TotalSkusScanned = skus.Count,
        };

        foreach (var s in skus)
        {
            var key = NormalizeSkuKey(s.SkuFullTitle);
            var accepted = acceptedByKey.GetValueOrDefault(key, 0);
            var removed = removedByKey.GetValueOrDefault(key, 0);

            // Защита от «немого нуля»: если по SKU было движение (sold/defected > 0),
            // но accepted == 0 — почти наверняка ключ не сматчился (рассинхрон join'а).
            // Логируем, чтобы баг больше не выглядел как честный «нет потерь».
            if (accepted == 0 && (s.QuantitySold > 0 || s.QuantityDefected > 0))
                _log.LogWarning(
                    "Подозрение на несматченный SKU: skuId={SkuId} skuTitle='{Title}' " +
                    "skuFullTitle='{Full}' → ключ карточки='{Key}', но accepted=0 при " +
                    "sold={Sold}/defected={Def}. Проверить соответствие ключей invoice/return.",
                    s.SkuId, s.SkuTitle, s.SkuFullTitle, key, s.QuantitySold, s.QuantityDefected);

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
    /// Единый ключ матча SKU между карточкой (skuFullTitle) и накладными/возвратами
    /// (их skuTitle). Устойчив к «грязным» строкам Uzum: trim, верхний регистр,
    /// схлопывание повторных пробелов. Null/пусто → пустая строка (не сматчится).
    /// </summary>
    private static string NormalizeSkuKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var collapsed = System.Text.RegularExpressions.Regex.Replace(raw.Trim(), @"\s+", " ");
        return collapsed.ToUpperInvariant();
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
    /// accepted по НОРМАЛИЗОВАННОМУ ключу (skuTitle накладной = полный код SKU):
    /// сумма quantityAccepted по строкам SKU накладных со статусом ACCEPTED.
    /// Черновики (CREATED) игнорируем. Ключ = NormalizeSkuKey(sku.SkuTitle).
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
                var key = NormalizeSkuKey(sku.SkuTitle);
                if (key.Length == 0)
                    continue;
                acc[key] = acc.GetValueOrDefault(key, 0) + sku.QuantityAccepted;
            }
        }
        return acc;
    }

    /// <summary>
    /// removed по НОРМАЛИЗОВАННОМУ ключу (skuTitle возврата = полный код SKU): физвывоз
    /// со склада. Legacy-правило на РОДИТЕЛЬСКОМ возврате: если totalPackedAmount==0 И
    /// assembledDate отсутствует → берём amount строки; иначе packedAmount.
    /// Ключ = NormalizeSkuKey(item.SkuTitle).
    /// </summary>
    private static Dictionary<string, int> AggregateRemoved(IReadOnlyList<SellerReturnDto> returns)
    {
        var rem = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var ret in returns)
        {
            var useAmount = ret.UseAmount;
            foreach (var item in ret.ReturnItems)
            {
                var key = NormalizeSkuKey(item.SkuTitle);
                if (key.Length == 0)
                    continue;
                var qty = useAmount ? item.Amount : item.PackedAmount;
                rem[key] = rem.GetValueOrDefault(key, 0) + qty;
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
