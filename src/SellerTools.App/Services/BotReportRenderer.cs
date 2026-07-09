using System.Text;
using SellerTools.App.Localization;
using SellerTools.Core.Reports;

namespace SellerTools.App.Services;

/// <summary>
/// Рендер отчёта для Telegram карточками на каждый SKU (без моноширинной таблицы).
/// Шапка — сколько Uzum должен двумя оценками, затем карточки топ-позиций.
/// Даты в формате дд.мм.гггг. Учитывает лимит Telegram 4096 символов.
/// </summary>
public static class BotReportRenderer
{
    private const int MaxCards = 10;       // показываем не более топ-10 позиций
    private const int NameMaxLen = 35;     // обрезка названия SKU
    private const int TelegramLimit = 4096;
    private const int SafeLimit = 3900;    // запас под шапку/сноску/хвост

    /// <summary>Готовый текст сообщения (Markdown).</summary>
    public static string Render(LossReport r, string lang = "ru")
    {
        var date = r.Date.ToString("dd.MM.yyyy");
        var shop = string.IsNullOrWhiteSpace(r.ShopTitle) ? $"{r.ShopId}" : r.ShopTitle!;

        // Нулевые потери — отдельный короткий случай.
        if (r.Rows.Count == 0)
            return string.Format(Loc.T("report.card.clean", lang), r.TotalSkusScanned);

        var sb = new StringBuilder();

        // Шапка: первая строка (💰 + текст) целиком жирная, затем строка магазина.
        sb.AppendLine($"*{string.Format(Loc.T("report.card.owed", lang), Fmt(r.TotalEstimateBySalePrice))}*");
        sb.AppendLine(string.Format(Loc.T("report.card.header", lang), shop, date, r.TotalSkusScanned));
        sb.AppendLine();

        // Rows уже отсортированы по убыванию EstimateBySalePrice → берём топ.
        var shown = 0;
        var limit = Math.Min(MaxCards, r.Rows.Count);
        for (var i = 0; i < limit; i++)
        {
            var card = RenderCard(i + 1, r.Rows[i], lang);

            // Защита от лимита Telegram: если следующая карточка не влезает — стоп.
            if (sb.Length + card.Length > SafeLimit)
                break;

            sb.Append(card);
            shown++;
        }

        // Хвост: сколько позиций осталось за кадром (топ-10 или из-за лимита).
        var hidden = r.Rows.Count - shown;
        if (hidden > 0)
        {
            sb.AppendLine();
            sb.AppendLine(string.Format(Loc.T("report.card.more", lang), hidden));
        }

        // Сноска про уже полученную компенсацию (вычесть вручную).
        sb.AppendLine();
        sb.AppendLine($"_{Loc.T("report.compensationNote", lang)}_");

        var text = sb.ToString();
        // Крайняя подстраховка — жёстко не превышаем лимит Telegram.
        return text.Length <= TelegramLimit ? text : text[..TelegramLimit];
    }

    private static string RenderCard(int number, LossReportRow row, string lang)
    {
        var name = Truncate(string.IsNullOrWhiteSpace(row.ProductTitle) ? row.SkuTitle : row.ProductTitle, NameMaxLen);

        var sb = new StringBuilder();
        sb.AppendLine($"{number}. *{Escape(name)}*");

        // Строка потерь: сколько потеряно по балансу + оценка к взысканию.
        var parts = new List<string>
        {
            string.Format(Loc.T("report.card.lost", lang), row.Lost),
            string.Format(Loc.T("report.card.sum", lang), Fmt(row.EstimateBySalePrice)),
        };
        sb.AppendLine(string.Join(" · ", parts));

        // Оценка по себестоимости второй строкой — только если COGS задан (Фаза 1).
        if (row.HasCogs)
            sb.AppendLine(string.Format(Loc.T("report.card.lineCogs", lang), Fmt(row.EstimateByCogs)));

        sb.AppendLine();
        return sb.ToString();
    }

    // Разряды через неразрывный пробел (без зависимости от культуры).
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

    private static string Truncate(string s, int len) =>
        s.Length <= len ? s : s[..(len - 1)] + "…";

    // Экранируем спецсимволы Markdown в названии, чтобы не ломать разметку.
    private static string Escape(string s) =>
        s.Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("[", "\\[");
}
