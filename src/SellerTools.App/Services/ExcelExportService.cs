using ClosedXML.Excel;
using SellerTools.App.Localization;
using SellerTools.UzumApi.Dto;

namespace SellerTools.App.Services;

/// <summary>
/// Готовые Excel-файлы (.xlsx) для отправки в бот.
/// Обе выгрузки строятся из свежих SkuDto (нужны штрихкод/артикул/ИКПУ).
/// Числа — числовыми ячейками с форматом «# ##0», не строками; итоги — формулой SUM.
/// </summary>
public class ExcelExportService
{
    // Числовой формат с разделителем разрядов (неразрывный пробел).
    private const string NumFmt = "# ##0";

    /// <summary>Результат выгрузки: имя файла и байты.</summary>
    public record ExcelFile(string FileName, byte[] Bytes);

    /// <summary>
    /// Лист потеряшек: только SKU, где потеряно+брак &gt; 0. Колонки:
    /// SKU / Товар / Артикул / Штрихкод / Потеряно / Брак / Цена продажи /
    /// Закуп из ЛК / Сумма по продаже / Сумма по закупу. Строка итогов формулами.
    /// </summary>
    public ExcelFile BuildLossReport(
        long shopId, string? shopTitle, DateOnly date, IReadOnlyList<SkuDto> allSkus, string lang)
    {
        var rows = allSkus
            .Where(s => s.QuantityMissing + s.QuantityDefected > 0)
            .OrderByDescending(s => (long)(s.QuantityMissing + s.QuantityDefected) * s.Price)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Loc.T("xlsx.loss.sheet", lang));

        // Шапка: название магазина + дата снапшота.
        var shop = string.IsNullOrWhiteSpace(shopTitle) ? shopId.ToString() : $"{shopTitle} ({shopId})";
        ws.Cell(1, 1).Value = string.Format(Loc.T("xlsx.loss.title", lang), shop, date.ToString("dd.MM.yyyy"));
        ws.Range(1, 1, 1, 10).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;

        // Заголовки колонок (строка 3).
        const int headerRow = 3;
        string[] headers =
        {
            Loc.T("xlsx.col.sku", lang), Loc.T("xlsx.col.product", lang),
            Loc.T("xlsx.col.article", lang), Loc.T("xlsx.col.barcode", lang),
            Loc.T("xlsx.col.missing", lang), Loc.T("xlsx.col.defected", lang),
            Loc.T("xlsx.col.price", lang), Loc.T("xlsx.col.purchase", lang),
            Loc.T("xlsx.col.sumSale", lang), Loc.T("xlsx.col.sumPurchase", lang),
        };
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(headerRow, c + 1).Value = headers[c];
        var headerRange = ws.Range(headerRow, 1, headerRow, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5F2EF");
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        var firstDataRow = headerRow + 1;
        var r = firstDataRow;
        foreach (var s in rows)
        {
            var units = s.QuantityMissing + s.QuantityDefected;
            ws.Cell(r, 1).Value = s.SkuId;
            ws.Cell(r, 2).Value = s.ProductTitle ?? s.SkuTitle ?? "";
            ws.Cell(r, 3).Value = s.Article ?? s.SellerItemCode ?? "";
            // Штрихкод — как текст (может быть длинным числом, ведущие нули важны).
            ws.Cell(r, 4).Value = s.Barcode == 0 ? "" : s.Barcode.ToString();
            ws.Cell(r, 4).Style.NumberFormat.Format = "@";
            ws.Cell(r, 5).Value = s.QuantityMissing;
            ws.Cell(r, 6).Value = s.QuantityDefected;
            ws.Cell(r, 7).Value = s.Price;
            ws.Cell(r, 8).Value = s.PurchasePrice;
            // Суммы — формулами Excel (перемножение считает сам лист).
            ws.Cell(r, 9).FormulaA1 = $"=({FCol(5)}{r}+{FCol(6)}{r})*{FCol(7)}{r}";
            ws.Cell(r, 10).FormulaA1 =
                $"=IF({FCol(8)}{r}>0,({FCol(5)}{r}+{FCol(6)}{r})*{FCol(8)}{r},0)";
            r++;
        }

        var lastDataRow = r - 1;

        // Строка итогов формулами SUM (даже если строк нет — SUM пустого = 0).
        var totalRow = r;
        ws.Cell(totalRow, 1).Value = Loc.T("xlsx.loss.total", lang);
        ws.Cell(totalRow, 5).FormulaA1 = SumFormula(5, firstDataRow, lastDataRow);
        ws.Cell(totalRow, 6).FormulaA1 = SumFormula(6, firstDataRow, lastDataRow);
        ws.Cell(totalRow, 9).FormulaA1 = SumFormula(9, firstDataRow, lastDataRow);
        ws.Cell(totalRow, 10).FormulaA1 = SumFormula(10, firstDataRow, lastDataRow);
        ws.Range(totalRow, 1, totalRow, 10).Style.Font.Bold = true;
        ws.Range(totalRow, 1, totalRow, 10).Style.Border.TopBorder = XLBorderStyleValues.Medium;

        // Числовые форматы (кол-во — целые; деньги — с разделителем).
        ApplyNumberFormats(ws, firstDataRow, totalRow);

        // Сноска про закуп.
        ws.Cell(totalRow + 2, 1).Value = Loc.T("xlsx.loss.note", lang);
        ws.Range(totalRow + 2, 1, totalRow + 2, 10).Merge();
        ws.Cell(totalRow + 2, 1).Style.Font.Italic = true;
        ws.Cell(totalRow + 2, 1).Style.Font.FontColor = XLColor.FromHtml("#666666");

        ws.Columns().AdjustToContents();
        ws.Column(2).Width = Math.Min(ws.Column(2).Width, 40);

        return new ExcelFile($"foydam-poteryashki-{shopId}-{date:yyyy-MM-dd}.xlsx", Save(wb));
    }

    /// <summary>
    /// Лист «Товары + ИКПУ»: все SKU магазина. Колонки:
    /// SKU / Товар / Артикул / Штрихкод / ИКПУ / Цена. Для ЭТТН.
    /// </summary>
    public ExcelFile BuildIkpuList(
        long shopId, string? shopTitle, DateOnly date, IReadOnlyList<SkuDto> allSkus, string lang)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(Loc.T("xlsx.ikpu.sheet", lang));

        var shop = string.IsNullOrWhiteSpace(shopTitle) ? shopId.ToString() : $"{shopTitle} ({shopId})";
        ws.Cell(1, 1).Value = string.Format(Loc.T("xlsx.ikpu.title", lang), shop, date.ToString("dd.MM.yyyy"));
        ws.Range(1, 1, 1, 6).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;

        const int headerRow = 3;
        string[] headers =
        {
            Loc.T("xlsx.col.sku", lang), Loc.T("xlsx.col.product", lang),
            Loc.T("xlsx.col.article", lang), Loc.T("xlsx.col.barcode", lang),
            Loc.T("xlsx.col.ikpu", lang), Loc.T("xlsx.col.price", lang),
        };
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(headerRow, c + 1).Value = headers[c];
        var headerRange = ws.Range(headerRow, 1, headerRow, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E5F2EF");
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

        var r = headerRow + 1;
        foreach (var s in allSkus.OrderBy(s => s.ProductTitle))
        {
            ws.Cell(r, 1).Value = s.SkuId;
            ws.Cell(r, 2).Value = s.ProductTitle ?? s.SkuTitle ?? "";
            ws.Cell(r, 3).Value = s.Article ?? s.SellerItemCode ?? "";
            ws.Cell(r, 4).Value = s.Barcode == 0 ? "" : s.Barcode.ToString();
            ws.Cell(r, 4).Style.NumberFormat.Format = "@";
            // ИКПУ — текст (длинный код, ведущие нули значимы).
            ws.Cell(r, 5).Value = s.Ikpu ?? "";
            ws.Cell(r, 5).Style.NumberFormat.Format = "@";
            ws.Cell(r, 6).Value = s.Price;
            ws.Cell(r, 6).Style.NumberFormat.Format = NumFmt;
            r++;
        }

        ws.Columns().AdjustToContents();
        ws.Column(2).Width = Math.Min(ws.Column(2).Width, 40);

        return new ExcelFile($"foydam-tovary-ikpu-{shopId}-{date:yyyy-MM-dd}.xlsx", Save(wb));
    }

    private static void ApplyNumberFormats(IXLWorksheet ws, int firstDataRow, int totalRow)
    {
        // Кол-во (5,6) — целые; деньги (7..10) — с разделителем разрядов.
        for (var row = firstDataRow; row <= totalRow; row++)
        {
            ws.Cell(row, 5).Style.NumberFormat.Format = "0";
            ws.Cell(row, 6).Style.NumberFormat.Format = "0";
            for (var col = 7; col <= 10; col++)
                ws.Cell(row, col).Style.NumberFormat.Format = NumFmt;
        }
    }

    private static string SumFormula(int col, int firstRow, int lastRow)
    {
        if (lastRow < firstRow)
            return "=0"; // строк данных нет
        var letter = FCol(col);
        return $"=SUM({letter}{firstRow}:{letter}{lastRow})";
    }

    // Номер колонки → буква (1→A, 2→B, …). Хватает A..Z для наших листов.
    private static string FCol(int col) => ((char)('A' + col - 1)).ToString();

    private static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
