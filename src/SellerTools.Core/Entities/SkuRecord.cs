namespace SellerTools.Core.Entities;

/// <summary>
/// Актуальное состояние SKU магазина (карточка /v1/product/shop/{shopId}), сохранённое
/// в БД. PK (ShopId, SkuId). Освежается синком карточек; отчёт берёт quantity*/цену/ИКПУ
/// отсюда (sold/defected/stock для баланса, ikpu/barcode для Excel).
/// В отличие от SkuDailySnapshot (история по дням) — это ПОСЛЕДНИЙ срез (одна строка на SKU).
/// </summary>
public class SkuRecord
{
    public long ShopId { get; set; }
    public long SkuId { get; set; }

    /// <summary>Короткий skuTitle карточки (напр. «11193»/«СЕРЫЙ»).</summary>
    public string? SkuTitle { get; set; }

    /// <summary>Полный код SKU (skuFullTitle) — из него считаем ключ матча.</summary>
    public string? SkuFullTitle { get; set; }

    /// <summary>Нормализованный ключ матча (SkuKey.Normalize от SkuFullTitle).</summary>
    public string SkuKeyNorm { get; set; } = "";

    public long ProductId { get; set; }
    public string? ProductTitle { get; set; }

    // --- Количества для баланса потерь ---
    public int QuantitySold { get; set; }       // НЕТТО
    public int QuantityReturned { get; set; }    // клиентские возвраты (в формуле НЕ участвуют)
    public int QuantityDefected { get; set; }
    public int QuantityActive { get; set; }      // stock

    // --- Для Excel / оценки ---
    /// <summary>ИКПУ-код (для ЭТТН). Строка — ведущие нули значимы.</summary>
    public string? Ikpu { get; set; }
    public string? Article { get; set; }
    public string? SellerItemCode { get; set; }
    public long Barcode { get; set; }
    public long Price { get; set; }

    /// <summary>Закуп из ЛК — ГРЯЗНЫЙ, в оценку не подставляем (храним как есть).</summary>
    public long PurchasePriceDirty { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
