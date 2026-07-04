namespace SellerTools.Core.Reports;

/// <summary>
/// Строка отчёта «Сколько Uzum вам должен» по одному SKU
/// (только те, где missing + defected &gt; 0).
/// </summary>
public class LossReportRow
{
    public long SkuId { get; set; }
    public string SkuTitle { get; set; } = "";
    public string ProductTitle { get; set; } = "";

    public int Missing { get; set; }
    public int Defected { get; set; }

    /// <summary>Всего проблемных единиц = Missing + Defected.</summary>
    public int TotalUnits => Missing + Defected;

    public long Price { get; set; }
    public long PurchasePrice { get; set; }

    /// <summary>Оценка по цене продажи: TotalUnits × Price.</summary>
    public long EstimateBySalePrice => TotalUnits * Price;

    /// <summary>
    /// Оценка по закупу: TotalUnits × PurchasePrice, только если закуп &gt; 0.
    /// Иначе 0 (закуп в ЛК может быть не заполнен).
    /// </summary>
    public long EstimateByPurchasePrice => PurchasePrice > 0 ? TotalUnits * PurchasePrice : 0;

    /// <summary>Заполнен ли закуп в ЛК для этого SKU.</summary>
    public bool HasPurchasePrice => PurchasePrice > 0;
}

/// <summary>
/// Итоговый отчёт по магазину: строки + суммы двумя оценками.
/// </summary>
public class LossReport
{
    public long ShopId { get; set; }
    public string? ShopTitle { get; set; }
    public DateOnly Date { get; set; }

    public List<LossReportRow> Rows { get; set; } = new();

    /// <summary>Сколько SKU всего просмотрено (для контекста).</summary>
    public int TotalSkusScanned { get; set; }

    public int TotalMissing => Rows.Sum(r => r.Missing);
    public int TotalDefected => Rows.Sum(r => r.Defected);
    public int TotalProblemUnits => Rows.Sum(r => r.TotalUnits);

    /// <summary>Итог по цене продажи.</summary>
    public long TotalEstimateBySalePrice => Rows.Sum(r => r.EstimateBySalePrice);

    /// <summary>Итог по закупу (только SKU с заполненным закупом).</summary>
    public long TotalEstimateByPurchasePrice => Rows.Sum(r => r.EstimateByPurchasePrice);

    /// <summary>Есть ли хоть один SKU без заполненного закупа (для пометки в отчёте).</summary>
    public bool HasSkusWithoutPurchasePrice => Rows.Any(r => !r.HasPurchasePrice);
}
