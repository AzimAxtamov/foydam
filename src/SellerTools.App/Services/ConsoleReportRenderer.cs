using System.Text;
using SellerTools.App.Localization;
using SellerTools.Core.Reports;

namespace SellerTools.App.Services;

/// <summary>
/// Рисует отчёт таблицей в консоль (deliverable В1).
/// </summary>
public static class ConsoleReportRenderer
{
    public static string Render(LossReport r, string lang = "ru")
    {
        var sb = new StringBuilder();
        var line = new string('═', 72);

        sb.AppendLine();
        sb.AppendLine(line);
        sb.AppendLine($"  {Loc.T("report.title", lang).ToUpperInvariant()}");
        sb.Append($"  {Loc.T("report.shop", lang)}: {r.ShopId}");
        if (!string.IsNullOrWhiteSpace(r.ShopTitle)) sb.Append($" ({r.ShopTitle})");
        sb.AppendLine();
        sb.AppendLine($"  {Loc.T("report.date", lang)}: {r.Date:yyyy-MM-dd}   " +
                      $"{Loc.T("report.scanned", lang)}: {r.TotalSkusScanned}");
        sb.AppendLine(line);

        if (r.Rows.Count == 0)
        {
            sb.AppendLine($"  {Loc.T("report.noLosses", lang)}");
            sb.AppendLine(line);
            return sb.ToString();
        }

        // Заголовок таблицы.
        sb.AppendLine(string.Format(
            "  {0,-38} {1,6} {2,16} {3,16}",
            Loc.T("report.col.sku", lang),
            Loc.T("report.col.lost", lang),
            Loc.T("report.col.bySale", lang),
            Loc.T("report.col.byCogs", lang)));
        sb.AppendLine("  " + new string('─', 78));

        foreach (var row in r.Rows)
        {
            var name = Truncate($"{row.ProductTitle} · {row.SkuTitle}", 38);
            var byCogs = row.HasCogs
                ? Fmt(row.EstimateByCogs)
                : "—";
            sb.AppendLine(string.Format(
                "  {0,-38} {1,6} {2,16} {3,16}",
                name, row.Lost,
                Fmt(row.EstimateBySalePrice), byCogs));
        }

        sb.AppendLine("  " + new string('─', 78));
        sb.AppendLine(string.Format(
            "  {0,-38} {1,6} {2,16} {3,16}",
            Loc.T("report.total", lang),
            r.TotalLost,
            Fmt(r.TotalEstimateBySalePrice), Fmt(r.TotalEstimateByCogs)));
        sb.AppendLine(line);
        sb.AppendLine($"  {Loc.T("report.totalBySale", lang)}: {Fmt(r.TotalEstimateBySalePrice)} сум");
        if (r.HasSkusWithoutCogs)
            sb.AppendLine($"  {Loc.T("report.cogsNote", lang)}");
        else
            sb.AppendLine($"  {Loc.T("report.totalByCogs", lang)}: {Fmt(r.TotalEstimateByCogs)} сум");

        sb.AppendLine($"  {Loc.T("report.compensationNote", lang)}");

        sb.AppendLine(line);
        return sb.ToString();
    }

    // Разряды через неразрывный пробел (без зависимости от культуры —
    // работает в InvariantGlobalization).
    private static string Fmt(long v)
    {
        var s = Math.Abs(v).ToString();
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && (s.Length - i) % 3 == 0) sb.Append(' ');
            sb.Append(s[i]);
        }
        return (v < 0 ? "-" : "") + sb;
    }

    private static string Truncate(string s, int len) =>
        s.Length <= len ? s : s.Substring(0, len - 1) + "…";
}
