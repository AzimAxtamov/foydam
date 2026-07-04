using System.Text;
using SellerTools.App.Localization;
using SellerTools.Core.Reports;

namespace SellerTools.App.Services;

/// <summary>
/// Рендер отчёта для Telegram: итоговые суммы двумя оценками в первых двух строках,
/// затем таблица (моноширинная, в блоке кода), сноска про незаполненный закуп.
/// Заголовки колонок — полными словами («Потеряно», «Брак»), без сокращений.
/// </summary>
public static class BotReportRenderer
{
    /// <summary>Возвращает готовый текст сообщения (Markdown, таблица в ```-блоке).</summary>
    public static string Render(LossReport r, string lang = "ru")
    {
        var sb = new StringBuilder();

        var shop = string.IsNullOrWhiteSpace(r.ShopTitle) ? $"{r.ShopId}" : $"{r.ShopTitle} ({r.ShopId})";

        // Первые две строки — итоговые суммы двумя оценками.
        sb.AppendLine($"💰 {Loc.T("report.totalBySale", lang)}: *{Fmt(r.TotalEstimateBySalePrice)} сум*");
        sb.AppendLine($"📦 {Loc.T("report.totalByPurchase", lang)}: *{Fmt(r.TotalEstimateByPurchasePrice)} сум*");
        sb.AppendLine();
        sb.AppendLine($"🏪 {Loc.T("report.shop", lang)}: {shop}");
        sb.AppendLine($"📅 {Loc.T("report.date", lang)}: {r.Date:yyyy-MM-dd}   " +
                      $"{Loc.T("report.scanned", lang)}: {r.TotalSkusScanned}");

        if (r.Rows.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine($"✅ {Loc.T("report.noLosses", lang)}");
            return sb.ToString();
        }

        // Таблица в моноширинном блоке — полные заголовки.
        sb.AppendLine();
        sb.AppendLine("```");
        var missing = Loc.T("report.col.missing", lang); // «Потеряно»
        var defected = Loc.T("report.col.defected", lang); // «Брак»
        sb.AppendLine(string.Format(
            "{0,-26} {1,8} {2,6} {3,14} {4,14}",
            Loc.T("report.col.sku", lang), missing, defected,
            Loc.T("report.col.bySale", lang), Loc.T("report.col.byPurchase", lang)));
        sb.AppendLine(new string('-', 72));

        foreach (var row in r.Rows)
        {
            var name = Truncate($"{row.ProductTitle} · {row.SkuTitle}", 26);
            var byPurchase = row.HasPurchasePrice ? Fmt(row.EstimateByPurchasePrice) : "—";
            sb.AppendLine(string.Format(
                "{0,-26} {1,8} {2,6} {3,14} {4,14}",
                name, row.Missing, row.Defected, Fmt(row.EstimateBySalePrice), byPurchase));
        }

        sb.AppendLine(new string('-', 72));
        sb.AppendLine(string.Format(
            "{0,-26} {1,8} {2,6} {3,14} {4,14}",
            Loc.T("report.total", lang), r.TotalMissing, r.TotalDefected,
            Fmt(r.TotalEstimateBySalePrice), Fmt(r.TotalEstimateByPurchasePrice)));
        sb.AppendLine("```");

        if (r.HasSkusWithoutPurchasePrice)
            sb.AppendLine($"_{Loc.T("report.purchaseNote", lang)}_");

        return sb.ToString();
    }

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
        s.Length <= len ? s : s.Substring(0, len - 1) + "…";
}
