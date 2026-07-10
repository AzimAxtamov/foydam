using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.App.Localization;
using SellerTools.App.Services;
using Telegram.Bot.Types.ReplyMarkups;

namespace SellerTools.App.Jobs;

/// <summary>
/// Доставка отчёта/Excel пользователю ПОСЛЕ завершения синка (Hangfire-continuation).
/// Строит из БД (LossReportService/ExcelExportService — ноль живых запросов к Uzum) и
/// шлёт через BotNotifier. Идёт в default-очередь (не uzum-sync — тут нет запросов к API).
/// </summary>
public class ReportDeliveryJob
{
    private readonly AppDbContext _db;
    private readonly LossReportService _report;
    private readonly ExcelExportService _excel;
    private readonly IBotNotifier _notifier;
    private readonly ILogger<ReportDeliveryJob> _log;

    public ReportDeliveryJob(
        AppDbContext db,
        LossReportService report,
        ExcelExportService excel,
        IBotNotifier notifier,
        ILogger<ReportDeliveryJob> log)
    {
        _db = db;
        _report = report;
        _excel = excel;
        _notifier = notifier;
        _log = log;
    }

    /// <summary>Карточный отчёт из БД → пользователю.</summary>
    public async Task DeliverReportAsync(long chatId, long shopId, string lang, CancellationToken ct = default)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, ct);
        if (shop is null)
            return;

        // Если синк только что пометил магазин неактивным — сообщаем дружелюбно, отчёт не строим.
        if (await IsInactiveAsync(shopId, ct))
        {
            await _notifier.SendTextAsync(chatId, Loc.T("bot.report.shopInactive", lang), ct: ct);
            return;
        }

        var report = await _report.BuildAsync(shop, ct);
        var text = BotReportRenderer.Render(report, lang);
        // Инлайн-кнопка «Поделиться» и на фоновой доставке отчёта (тот же callback share:report).
        var share = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithCallbackData(Loc.T("share.button", lang), "share:report"));
        await _notifier.SendTextAsync(chatId, text, share, markdown: true, ct: ct);
    }

    /// <summary>Excel-выгрузка из БД → пользователю. loss=true — потеряшки, иначе товары+ИКПУ.</summary>
    public async Task DeliverExcelAsync(long chatId, long shopId, string lang, bool loss, CancellationToken ct = default)
    {
        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, ct);
        if (shop is null)
            return;

        if (await IsInactiveAsync(shopId, ct))
        {
            await _notifier.SendTextAsync(chatId, Loc.T("bot.report.shopInactive", lang), ct: ct);
            return;
        }

        ExcelExportService.ExcelFile file;
        string caption;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (loss)
        {
            var report = await _report.BuildAsync(shop, ct);
            file = _excel.BuildLossReport(report, lang);
            caption = report.Rows.Count > 0
                ? string.Format(Loc.T("bot.excel.lossCaption", lang), report.Rows.Count)
                : Loc.T("bot.excel.lossEmptyCaption", lang);
        }
        else
        {
            var skus = await _report.GetSkusAsync(shopId, ct);
            file = _excel.BuildIkpuList(shopId, shop.Title, today, skus, lang);
            caption = string.Format(Loc.T("bot.excel.ikpuCaption", lang), skus.Count);
        }

        await _notifier.SendDocumentAsync(chatId, file.FileName, file.Bytes, caption, ct);
    }

    private async Task<bool> IsInactiveAsync(long shopId, CancellationToken ct)
    {
        var st = await _db.ShopSyncStates.AsNoTracking().FirstOrDefaultAsync(x => x.ShopId == shopId, ct);
        return st?.Inactive == true;
    }
}
