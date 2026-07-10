using System.Net;
using System.Text;
using SellerTools.App.Localization;
using SellerTools.Core.Reports;
using Telegram.Bot.Types.ReplyMarkups;

namespace SellerTools.App.Services;

/// <summary>
/// Собирает share-карточку (viral V1): пересылаемое сообщение бота, которое читается
/// как личный пост селлера, а не реклама (см. §2 спеки — запрет императивов/«бесплатно»/
/// «бот»/ссылок в теле/@-упоминаний/маркетинговых эмодзи).
///
/// Носитель виральности — сам форвард: кликабельный заголовок «Переслано от Foydam»
/// (HTML-ссылка на дип-линк) + одна inline URL-кнопка «Как это посчитано». И заголовок,
/// и кнопка переживают пересылку и работают у получателя.
///
/// parse_mode = HTML; ВСЕ подставляемые значения — через HTML-escape (§7).
/// Числа — с пробелами-разделителями тысяч, без копеек.
/// </summary>
public static class ShareCardBuilder
{
    public readonly record struct ShareCard(string Text, InlineKeyboardMarkup Markup);

    /// <summary>
    /// Строит карточку по отчёту. <paramref name="deepLink"/> — <c>https://t.me/{bot}?start=ref_{code}</c>.
    /// Вариант текста выбирается по данным: есть потери → «потери», иначе → «чисто».
    /// </summary>
    public static ShareCard Build(LossReport report, string lang, string deepLink)
    {
        var hasLoss = report.Rows.Count > 0 && report.TotalLost > 0;

        var body = hasLoss
            ? BuildLossBody(report, lang)
            : Loc.T("share.card.clean", lang);

        // Заголовок-ссылка на дип-линк (переживает форвард; ведёт на бота).
        var title = $"<a href=\"{WebUtility.HtmlEncode(deepLink)}\"><b>{WebUtility.HtmlEncode(Loc.T("share.card.title", lang))}</b></a>";

        var text = $"{title}\n\n{body}";

        var markup = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithUrl(Loc.T("share.calc.button", lang), deepLink));

        return new ShareCard(text, markup);
    }

    private static string BuildLossBody(LossReport report, string lang)
    {
        // Фрагмент про брак — только если он есть. Вставляется в {0} шаблона share.card.loss.
        var defect = report.TotalDefected;
        var defectFragment = defect > 0
            ? string.Format(Loc.T("share.card.lossDefect", lang), defect)
            : "";

        // Шаблон: {0}=lostQty, {1}=defectFragment, {2}=sumMax.
        var raw = string.Format(
            Loc.T("share.card.loss", lang),
            Fmt(report.TotalLost),
            defectFragment,
            Fmt(report.ShareSumMax));

        return WebUtility.HtmlEncode(raw);
    }

    // Разряды через обычный пробел (§7: «1 250 000»), без копеек. Дубль форматтера из
    // BotReportRenderer намеренно — тот приватный, а тянуть зависимость Core←App нельзя.
    private static string Fmt(long v)
    {
        var s = Math.Abs(v).ToString();
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && (s.Length - i) % 3 == 0) sb.Append(' ');
            sb.Append(s[i]);
        }
        return (v < 0 ? "-" : "") + sb;
    }
}
