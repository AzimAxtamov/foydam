using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SellerTools.App.Configuration;
using SellerTools.App.Data;
using SellerTools.App.Jobs;
using SellerTools.Core.Entities;

namespace SellerTools.App.Services;

/// <summary>Что делать с запросом отчёта/Excel после проверки состояния синка.</summary>
public enum ReportDecision
{
    /// <summary>Данные свежие — можно строить из БД прямо сейчас (inline).</summary>
    Ready,

    /// <summary>Первый раз (Never) — поставлен полный синк, отчёт придёт по завершении.</summary>
    FirstSync,

    /// <summary>Данные устарели — поставлен дельта-синк, отчёт придёт по завершении.</summary>
    Refreshing,

    /// <summary>Магазин помечен неактивным (403) — отчёт не строим.</summary>
    Inactive,
}

/// <summary>
/// Решает, можно ли строить отчёт из БД сейчас или нужен синк. Все синки идут в
/// очередь uzum-sync; доставка отчёта/Excel — continuation в default-очереди.
/// Обработчик кнопки НИКОГДА не ходит в Uzum напрямую.
/// </summary>
public class ReportCoordinator
{
    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly SyncOptions _sync;

    public ReportCoordinator(
        AppDbContext db, IBackgroundJobClient jobs, IOptions<SyncOptions> sync)
    {
        _db = db;
        _jobs = jobs;
        _sync = sync.Value;
    }

    /// <summary>
    /// Готовит доставку отчёта: если данные свежие — вернёт Ready (стройте inline);
    /// иначе поставит синк + continuation-доставку и вернёт FirstSync/Refreshing.
    /// </summary>
    public Task<ReportDecision> RequestReportAsync(long chatId, long shopId, string lang, CancellationToken ct)
        => RequestAsync(chatId, shopId, lang, DeliveryKind.Report, loss: false, ct);

    /// <summary>Аналогично для Excel-выгрузки (loss=true — потеряшки, иначе товары+ИКПУ).</summary>
    public Task<ReportDecision> RequestExcelAsync(
        long chatId, long shopId, string lang, bool loss, CancellationToken ct)
        => RequestAsync(chatId, shopId, lang, DeliveryKind.Excel, loss, ct);

    private enum DeliveryKind { Report, Excel }

    private async Task<ReportDecision> RequestAsync(
        long chatId, long shopId, string lang, DeliveryKind kind, bool loss, CancellationToken ct)
    {
        // Только читаем состояние (решение never/stale/fresh) — без трекинга.
        var state = await _db.ShopSyncStates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShopId == shopId, ct);

        if (state?.Inactive == true)
            return ReportDecision.Inactive;

        // Never (или состояния ещё нет) → полный первичный синк, отчёт по завершении.
        if (state is null || state.Status == SyncStatus.Never)
        {
            EnqueueSyncThenDeliver(chatId, shopId, lang, kind, loss);
            return ReportDecision.FirstSync;
        }

        // Данные устарели → быстрый дельта-синк, отчёт по завершении.
        if (IsStale(state))
        {
            EnqueueSyncThenDeliver(chatId, shopId, lang, kind, loss);
            return ReportDecision.Refreshing;
        }

        // Свежо — строим inline (вызывающий сам построит из БД).
        return ReportDecision.Ready;
    }

    /// <summary>Данные считаются устаревшими, если карточки не освежались дольше порога.</summary>
    private bool IsStale(ShopSyncState state)
    {
        var freshness = TimeSpan.FromHours(Math.Max(1, _sync.DeltaFreshnessHours));
        var last = state.LastCardsSyncAt ?? state.LastDeltaSyncAt;
        return last is null || DateTimeOffset.UtcNow - last.Value > freshness;
    }

    private void EnqueueSyncThenDeliver(
        long chatId, long shopId, string lang, DeliveryKind kind, bool loss)
    {
        // Синк — в очередь uzum-sync (3 воркера). Доставка — continuation после успеха.
        var syncJobId = _jobs.Enqueue<UzumSyncJob>(j => j.SyncShopAsync(shopId, CancellationToken.None));

        if (kind == DeliveryKind.Report)
            _jobs.ContinueJobWith<ReportDeliveryJob>(
                syncJobId, j => j.DeliverReportAsync(chatId, shopId, lang, CancellationToken.None));
        else
            _jobs.ContinueJobWith<ReportDeliveryJob>(
                syncJobId, j => j.DeliverExcelAsync(chatId, shopId, lang, loss, CancellationToken.None));
    }
}
