using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SellerTools.App.Configuration;
using SellerTools.App.Data;
using SellerTools.Core.Entities;
using SellerTools.UzumApi;
using SellerTools.UzumApi.Dto;

namespace SellerTools.App.Services;

/// <summary>Итог синка магазина.</summary>
public record UzumSyncResult(long ShopId, bool Ok, string? Error, bool Inactive = false)
{
    public int Invoices { get; init; }
    public int Returns { get; init; }
    public int Skus { get; init; }
    public int Pages { get; init; }
}

/// <summary>
/// Синхронизация «синк → Postgres → отчёт из БД». Один запрос за раз (СТРОГО
/// последовательно, никаких Parallel/Task.WhenAll к API — глобальный лимит
/// параллелизма к Uzum держит очередь uzum-sync на 3 воркера, см. Program.cs).
///
/// Полный первичный синк: все страницы /v1/invoice, /v1/return, карточек → БД.
/// Дельта: записи приходят СВЕРХУ свежими; идём с page=0, останавливаемся, когда
/// страница целиком из уже сохранённых ТЕРМИНАЛЬНЫХ записей. Pending (накладные не
/// ACCEPTED, возвраты без completed/canceled) — пагинируем глубже до dateCreated
/// самого старого pending, чтобы поймать смену статуса.
///
/// ЗАЩИТА (при полном синке): сравниваем dateCreated page=0 и page=1; если порядок
/// ВОСХОДЯЩИЙ — бросаем исключение (иначе дельта «сверху» была бы неверной).
/// </summary>
public class UzumSyncService
{
    private readonly IUzumApiClient _api;
    private readonly AppDbContext _db;
    private readonly SyncStorage _storage;
    private readonly ICryptoService _crypto;
    private readonly IAdminAlertService _alerts;
    private readonly UzumApiOptions _apiOptions;
    private readonly SyncOptions _sync;
    private readonly ILogger<UzumSyncService> _log;

    // Размер страницы /v1/invoice и /v1/return — хард-лимит спеки (см. UzumConstants).
    private const int PageSize = UzumConstants.InvoiceReturnPageSize;

    public UzumSyncService(
        IUzumApiClient api,
        AppDbContext db,
        SyncStorage storage,
        ICryptoService crypto,
        IAdminAlertService alerts,
        IOptions<UzumApiOptions> apiOptions,
        IOptions<SyncOptions> sync,
        ILogger<UzumSyncService> log)
    {
        _api = api;
        _db = db;
        _storage = storage;
        _crypto = crypto;
        _alerts = alerts;
        _apiOptions = apiOptions.Value;
        _sync = sync.Value;
        _log = log;
    }

    /// <summary>
    /// Синк магазина по его записи. Выбирает полный (state=Never) или дельту.
    /// Исключения НЕ пробрасываются — возвращается результат с ошибкой (изоляция магазина).
    /// </summary>
    public async Task<UzumSyncResult> SyncShopAsync(Shop shop, CancellationToken ct = default)
    {
        var state = await GetOrCreateStateAsync(shop.Id, ct);

        if (state.Inactive)
        {
            _log.LogInformation("Магазин {ShopId} помечен неактивным — синк пропущен.", shop.Id);
            return new UzumSyncResult(shop.Id, Ok: false, Error: "магазин неактивен", Inactive: true);
        }

        if (string.IsNullOrEmpty(shop.ApiKeyEncrypted))
            return await FailAsync(state, "нет сохранённого ключа", ct);

        string apiKey;
        try
        {
            apiKey = _crypto.Decrypt(shop.ApiKeyEncrypted);
        }
        catch (Exception ex)
        {
            return await FailAsync(state, $"не удалось расшифровать ключ: {ex.Message}", ct);
        }

        var full = state.Status == SyncStatus.Never;
        state.Status = SyncStatus.Syncing;
        state.LastError = null;
        await _db.SaveChangesAsync(ct);

        try
        {
            var invPages = await SyncInvoicesAsync(shop.Id, apiKey, full, ct);
            var retPages = await SyncReturnsAsync(shop.Id, apiKey, full, ct);
            var skus = await SyncCardsAsync(shop.Id, apiKey, ct);

            var now = DateTimeOffset.UtcNow;
            state.Status = SyncStatus.Ready;
            state.LastDeltaSyncAt = now;
            state.LastCardsSyncAt = now;
            if (full)
                state.LastFullSyncAt = now;
            state.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);

            _log.LogInformation(
                "Синк магазина {ShopId} ({Mode}): накладных-стр {InvP}, возвратов-стр {RetP}, SKU {Skus}",
                shop.Id, full ? "полный" : "дельта", invPages, retPages, skus);

            return new UzumSyncResult(shop.Id, Ok: true, Error: null)
            {
                Skus = skus,
                Pages = invPages + retPages,
            };
        }
        catch (ShopUnavailableException)
        {
            // 403 «Shop is not available» — магазин выключен/недоступен ключом.
            state.Status = SyncStatus.Error;
            state.Inactive = true;
            state.LastError = "Shop is not available (403)";
            state.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            _log.LogWarning("Магазин {ShopId}: 403 Shop is not available — помечен неактивным.", shop.Id);
            return new UzumSyncResult(shop.Id, Ok: false, Error: "магазин недоступен", Inactive: true);
        }
        catch (Exception ex)
        {
            // Полный стек в лог (ex.Message терял место падения — на проде это скрывало NRE).
            _log.LogError(ex, "Синк магазина {ShopId} упал с исключением", shop.Id);
            return await FailAsync(state, $"{ex.GetType().Name}: {ex.Message}", ct, alert: true);
        }
    }

    // ---- Накладные ----

    private Task<int> SyncInvoicesAsync(long shopId, string apiKey, bool full, CancellationToken ct)
        => full
            ? FullSyncInvoicesAsync(shopId, apiKey, ct)
            : DeltaSyncInvoicesAsync(shopId, apiKey, ct);

    private async Task<int> FullSyncInvoicesAsync(long shopId, string apiKey, CancellationToken ct)
    {
        DateTimeOffset? firstPageFirstDate = null;
        var page = 0;
        while (true)
        {
            var pg = await FetchInvoicesPageAsync(shopId, apiKey, page, ct);
            await _storage.UpsertInvoicesAsync(shopId, pg.Items, ct);

            // Guard восходящего порядка: сравниваем первую запись page0 и page1.
            GuardAscendingOrder(
                page, pg.Items.Select(i => i.DateCreated).ToList(), ref firstPageFirstDate, "invoice");

            await RespectRateLimitAsync(shopId, pg.RemainingPerDay, ct);
            if (pg.IsLast || ReachedPageCap(page))
                return page + 1;
            page++;
        }
    }

    private async Task<int> DeltaSyncInvoicesAsync(long shopId, string apiKey, CancellationToken ct)
    {
        // Известные ТЕРМИНАЛЬНЫЕ id (ACCEPTED) — их можно считать «остановкой».
        var knownTerminal = await _db.Invoices
            .Where(x => x.ShopId == shopId && x.IsTerminal)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var knownSet = knownTerminal.ToHashSet();

        // Самый старый pending — до его dateCreated пагинируем глубже (ловим смену статуса).
        var oldestPending = await _db.Invoices
            .Where(x => x.ShopId == shopId && !x.IsTerminal && x.DateCreated != null)
            .OrderBy(x => x.DateCreated)
            .Select(x => x.DateCreated)
            .FirstOrDefaultAsync(ct);

        var page = 0;
        var overscan = 0;
        while (true)
        {
            var pg = await FetchInvoicesPageAsync(shopId, apiKey, page, ct);
            await _storage.UpsertInvoicesAsync(shopId, pg.Items, ct);
            await RespectRateLimitAsync(shopId, pg.RemainingPerDay, ct);

            if (pg.IsLast || ReachedPageCap(page))
                return page + 1;

            // Останов: вся страница — уже известные терминальные записи.
            var allKnownTerminal = pg.Items.Count > 0
                && pg.Items.All(i => i.IsTerminal && knownSet.Contains(i.Id));

            if (allKnownTerminal && !NeedDeeper(pg, oldestPending, ref overscan))
                return page + 1;

            page++;
        }
    }

    // ---- Возвраты ----

    private Task<int> SyncReturnsAsync(long shopId, string apiKey, bool full, CancellationToken ct)
        => full
            ? FullSyncReturnsAsync(shopId, apiKey, ct)
            : DeltaSyncReturnsAsync(shopId, apiKey, ct);

    private async Task<int> FullSyncReturnsAsync(long shopId, string apiKey, CancellationToken ct)
    {
        DateTimeOffset? firstPageFirstDate = null;
        var page = 0;
        while (true)
        {
            var pg = await FetchReturnsPageAsync(shopId, apiKey, page, ct);
            await _storage.UpsertReturnsAsync(shopId, pg.Items, ct);

            GuardAscendingOrder(
                page, pg.Items.Select(r => r.DateCreated).ToList(), ref firstPageFirstDate, "return");

            await RespectRateLimitAsync(shopId, pg.RemainingPerDay, ct);
            if (pg.IsLast || ReachedPageCap(page))
                return page + 1;
            page++;
        }
    }

    private async Task<int> DeltaSyncReturnsAsync(long shopId, string apiKey, CancellationToken ct)
    {
        var knownTerminal = await _db.Returns
            .Where(x => x.ShopId == shopId && x.IsTerminal)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var knownSet = knownTerminal.ToHashSet();

        var oldestPending = await _db.Returns
            .Where(x => x.ShopId == shopId && !x.IsTerminal && x.DateCreated != null)
            .OrderBy(x => x.DateCreated)
            .Select(x => x.DateCreated)
            .FirstOrDefaultAsync(ct);

        var page = 0;
        var overscan = 0;
        while (true)
        {
            var pg = await FetchReturnsPageAsync(shopId, apiKey, page, ct);
            await _storage.UpsertReturnsAsync(shopId, pg.Items, ct);
            await RespectRateLimitAsync(shopId, pg.RemainingPerDay, ct);

            if (pg.IsLast || ReachedPageCap(page))
                return page + 1;

            var allKnownTerminal = pg.Items.Count > 0
                && pg.Items.All(r => r.IsTerminal && knownSet.Contains(r.Id));

            if (allKnownTerminal && !NeedDeeperReturns(pg, oldestPending, ref overscan))
                return page + 1;

            page++;
        }
    }

    // ---- Карточки ----

    private async Task<int> SyncCardsAsync(long shopId, string apiKey, CancellationToken ct)
    {
        // Карточки: /v1/product постранично. shopId нужен в пути → идём через GetProductsPageAsync.
        var total = 0;
        var page = 0;
        while (true)
        {
            ProductsPage pg;
            try
            {
                pg = await _api.GetProductsPageAsync(shopId, apiKey, page, _apiOptions.PageSize, ct);
            }
            catch (HttpRequestException ex) when (IsShopUnavailable(ex))
            {
                throw new ShopUnavailableException();
            }

            var skus = pg.Products.SelectMany(p => p.SkuList).ToList();
            var productIdBySku = pg.Products
                .SelectMany(p => p.SkuList.Select(s => (s.SkuId, p.ProductId)))
                .GroupBy(x => x.SkuId)
                .ToDictionary(g => g.Key, g => g.First().ProductId);

            await _storage.UpsertSkusAsync(shopId, skus, productIdBySku, ct);
            await PersistSnapshotsAsync(shopId, skus, ct);
            total += skus.Count;

            await RespectRateLimitAsync(shopId, pg.RemainingPerDay, ct);
            if (pg.IsLast || ReachedPageCap(page))
                break;
            page++;
        }
        return total;
    }

    // ---- Вспомогательное ----

    private async Task<ApiPage<InvoiceDto>> FetchInvoicesPageAsync(
        long shopId, string apiKey, int page, CancellationToken ct)
    {
        try
        {
            return await _api.GetInvoicesPageAsync(shopId, apiKey, page, PageSize, ct);
        }
        catch (HttpRequestException ex) when (IsShopUnavailable(ex))
        {
            throw new ShopUnavailableException();
        }
    }

    private async Task<ApiPage<SellerReturnDto>> FetchReturnsPageAsync(
        long shopId, string apiKey, int page, CancellationToken ct)
    {
        try
        {
            return await _api.GetReturnsPageAsync(shopId, apiKey, page, PageSize, ct);
        }
        catch (HttpRequestException ex) when (IsShopUnavailable(ex))
        {
            throw new ShopUnavailableException();
        }
    }

    /// <summary>
    /// Guard: при полном синке фиксируем dateCreated первой записи page0; на page1
    /// проверяем, что её первая запись НЕ НОВЕЕ (порядок должен быть убывающим).
    /// Если page1 новее page0 — API отдаёт по возрастанию, дельта «сверху» неверна →
    /// бросаем понятное исключение, не синкаем молча.
    /// </summary>
    private void GuardAscendingOrder(
        int page, List<DateTimeOffset?> pageDates, ref DateTimeOffset? firstPageFirstDate, string kind)
    {
        var firstOnPage = pageDates.FirstOrDefault(d => d != null);
        if (page == 0)
        {
            firstPageFirstDate = firstOnPage;
            return;
        }
        if (page == 1 && firstPageFirstDate is { } p0 && firstOnPage is { } p1)
        {
            if (p1 > p0)
                throw new InvalidOperationException(
                    $"Неожиданный порядок страниц {kind}: page1 ({p1:o}) НОВЕЕ page0 ({p0:o}) — " +
                    "API отдаёт по возрастанию, а дельта-синк рассчитан на «новые сверху». " +
                    "Синк остановлен во избежание пропуска записей.");
        }
    }

    /// <summary>
    /// Нужна ли ещё страница после точки останова (накладные). Пагинируем глубже, пока
    /// самая свежая dateCreated на странице ≥ dateCreated старейшего pending, плюс
    /// overscan-страховка. oldestPending==null (нет pending) → только overscan.
    /// </summary>
    private bool NeedDeeper(ApiPage<InvoiceDto> pg, DateTimeOffset? oldestPending, ref int overscan)
    {
        if (oldestPending is { } op)
        {
            var minOnPage = pg.Items
                .Select(i => i.DateCreated).Where(d => d != null).Select(d => d!.Value)
                .DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            if (minOnPage >= op)
                return true; // ещё можем встретить старый pending → идём глубже
        }
        return overscan++ < _sync.DeltaOverscanPages;
    }

    private bool NeedDeeperReturns(ApiPage<SellerReturnDto> pg, DateTimeOffset? oldestPending, ref int overscan)
    {
        if (oldestPending is { } op)
        {
            var minOnPage = pg.Items
                .Select(r => r.DateCreated).Where(d => d != null).Select(d => d!.Value)
                .DefaultIfEmpty(DateTimeOffset.MaxValue).Min();
            if (minOnPage >= op)
                return true;
        }
        return overscan++ < _sync.DeltaOverscanPages;
    }

    private bool ReachedPageCap(int page) =>
        _sync.MaxPagesPerSync > 0 && page + 1 >= _sync.MaxPagesPerSync;

    /// <summary>
    /// Пауза несрочных синков, если остаток ДНЕВНОГО лимита ниже абсолютного порога
    /// (RateLimitDailyFloor). Плюс админ-алерт (троттлится в самом AdminAlertService).
    /// </summary>
    private async Task RespectRateLimitAsync(long shopId, long? remainingPerDay, CancellationToken ct)
    {
        if (remainingPerDay is not { } remaining || remaining >= _sync.RateLimitDailyFloor)
            return;

        _log.LogWarning(
            "Магазин {ShopId}: остаток дневного лимита {Remaining} ниже порога {Floor} — пауза {Pause}мс.",
            shopId, remaining, _sync.RateLimitDailyFloor, _sync.RateLimitPauseMs);
        await _alerts.AlertAsync(AlertKind.RateLimitLow,
            $"Магазин {shopId}: остаток дневного лимита {remaining} ниже порога " +
            $"{_sync.RateLimitDailyFloor}. Синк паузится.", ct);
        await Task.Delay(_sync.RateLimitPauseMs, ct);
    }

    private async Task<ShopSyncState> GetOrCreateStateAsync(long shopId, CancellationToken ct)
    {
        var state = await _db.ShopSyncStates.FirstOrDefaultAsync(x => x.ShopId == shopId, ct);
        if (state is null)
        {
            state = new ShopSyncState { ShopId = shopId, Status = SyncStatus.Never };
            _db.ShopSyncStates.Add(state);
            await _db.SaveChangesAsync(ct);
        }
        return state;
    }

    private async Task<UzumSyncResult> FailAsync(
        ShopSyncState state, string error, CancellationToken ct, bool alert = false)
    {
        state.Status = SyncStatus.Error;
        state.LastError = error;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _log.LogWarning("Синк магазина {ShopId} упал: {Error}", state.ShopId, error);
        if (alert)
            await _alerts.AlertAsync(AlertKind.SyncFailed, $"Синк магазина {state.ShopId}: {error}", ct);
        return new UzumSyncResult(state.ShopId, Ok: false, Error: error);
    }

    // 403 с текстом «Shop is not available» / «forbidden» — магазин недоступен ключом.
    private static bool IsShopUnavailable(HttpRequestException ex) =>
        ex.StatusCode == HttpStatusCode.Forbidden;

    /// <summary>
    /// Копим SkuDailySnapshot за сегодня (data moat) — upsert по (ShopId, Date, SkuId).
    /// Отдельно от SkuRecord: снапшоты — история по дням, SkuRecord — последний срез.
    /// </summary>
    private async Task PersistSnapshotsAsync(
        long shopId, IReadOnlyList<SkuDto> skus, CancellationToken ct)
    {
        if (skus.Count == 0)
            return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var skuIds = skus.Select(s => s.SkuId).ToList();
        var existing = await _db.SkuDailySnapshots
            .Where(x => x.ShopId == shopId && x.Date == today && skuIds.Contains(x.SkuId))
            .ToDictionaryAsync(x => x.SkuId, ct);

        foreach (var s in skus)
        {
            if (!existing.TryGetValue(s.SkuId, out var snap))
            {
                snap = new SkuDailySnapshot { ShopId = shopId, Date = today, SkuId = s.SkuId };
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

/// <summary>Внутренний маркер: 403 «Shop is not available» — обрабатываем отдельно.</summary>
public class ShopUnavailableException : Exception
{
    public ShopUnavailableException() : base("Shop is not available") { }
}
