namespace SellerTools.Core.Reports;

/// <summary>
/// Строка отчёта «Сколько Uzum вам должен» по одному SKU (только те, где lost &gt; 0).
///
/// lost — результат балансовой реконсиляции на 1 SKU:
///   lost = accepted − removed − sold − defected − stock
/// где accepted (принято складом, /v1/invoice ACCEPTED), removed (физвывоз,
/// /v1/return по legacy-правилу), sold=quantitySold (нетто), defected=quantityDefected,
/// stock=quantityActive (всё из карточки SKU). Отрицательный lost клампится в 0
/// (рассинхрон снапшота, не потеря).
/// </summary>
public class LossReportRow
{
    public long SkuId { get; set; }
    public string SkuTitle { get; set; } = "";
    public string ProductTitle { get; set; } = "";

    /// <summary>Артикул/код продавца (для Excel).</summary>
    public string? Article { get; set; }

    /// <summary>Штрихкод (для Excel). 0 = нет.</summary>
    public long Barcode { get; set; }

    /// <summary>Потеряно складом Uzum по балансу (уже склампленное в 0 снизу).</summary>
    public int Lost { get; set; }

    /// <summary>Компоненты баланса — для Excel/аудита (можно показать в детализации).</summary>
    public int Accepted { get; set; }
    public int Removed { get; set; }
    public int Sold { get; set; }
    public int Defected { get; set; }
    public int Stock { get; set; }

    /// <summary>Цена продажи (СУМ) из карточки.</summary>
    public long Price { get; set; }

    /// <summary>
    /// Себестоимость (COGS), введённая селлером у нас. В Фазе 0 ввода нет → всегда 0
    /// («закуп не указан»). purchasePrice из API НЕ подставляем (грязный).
    /// </summary>
    public long Cogs { get; set; }

    /// <summary>Оценка потери по цене продажи: Lost × Price.</summary>
    public long EstimateBySalePrice => Lost * Price;

    /// <summary>Оценка потери по себестоимости: Lost × Cogs, только если Cogs &gt; 0.</summary>
    public long EstimateByCogs => Cogs > 0 ? Lost * Cogs : 0;

    /// <summary>Задан ли COGS (введён селлером). В Фазе 0 — всегда false.</summary>
    public bool HasCogs => Cogs > 0;
}

/// <summary>
/// Итоговый отчёт по магазину: строки + суммы.
/// </summary>
public class LossReport
{
    public long ShopId { get; set; }
    public string? ShopTitle { get; set; }
    public DateOnly Date { get; set; }

    /// <summary>
    /// Момент, на который актуальны данные отчёта (последнее обновление данных в БД).
    /// Показывается в подвале «Данные на …». null — если неизвестно.
    /// </summary>
    public DateTimeOffset? DataAsOf { get; set; }

    public List<LossReportRow> Rows { get; set; } = new();

    /// <summary>Сколько SKU всего просмотрено (для контекста).</summary>
    public int TotalSkusScanned { get; set; }

    public int TotalLost => Rows.Sum(r => r.Lost);

    /// <summary>Суммарный брак по строкам отчёта (для share-карточки). Строки — только с lost&gt;0.</summary>
    public int TotalDefected => Rows.Sum(r => r.Defected);

    /// <summary>
    /// Оценка для share-карточки (§7 спеки шеринга): Σ(потеряшки + брак) × цена продажи.
    /// Считается по строкам отчёта (те же данные, что видел селлер).
    /// </summary>
    public long ShareSumMax => Rows.Sum(r => (long)(r.Lost + r.Defected) * r.Price);

    /// <summary>Итог к взысканию по цене продажи.</summary>
    public long TotalEstimateBySalePrice => Rows.Sum(r => r.EstimateBySalePrice);

    /// <summary>Итог по себестоимости (только SKU с заданным COGS). В Фазе 0 — 0.</summary>
    public long TotalEstimateByCogs => Rows.Sum(r => r.EstimateByCogs);

    /// <summary>Есть ли хоть один SKU без заданного COGS (для пометки в отчёте).</summary>
    public bool HasSkusWithoutCogs => Rows.Any(r => !r.HasCogs);
}
