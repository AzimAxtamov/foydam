namespace SellerTools.Core.Entities;

/// <summary>
/// «Ров данных»: ежедневный срез состояния SKU. Uzum API истории не отдаёт,
/// поэтому копим сами — по строке на (ShopId, Date, SkuId).
/// Уникальный индекс: ShopId + Date + SkuId.
/// Деньги — long (единицы уточняются живым ключом: сум или тийины). TODO(В2).
/// </summary>
public class SkuDailySnapshot
{
    public long Id { get; set; }

    public long ShopId { get; set; }

    /// <summary>Дата среза (UTC, только дата).</summary>
    public DateOnly Date { get; set; }

    public long SkuId { get; set; }
    public string? SkuTitle { get; set; }
    public string? ProductTitle { get; set; }

    // --- Количества из /v1/product/shop/{shopId} ---
    public int QuantityActive { get; set; }
    public int QuantitySold { get; set; }
    public int QuantityReturned { get; set; }

    /// <summary>Потеряно складом Uzum.</summary>
    public int QuantityMissing { get; set; }

    /// <summary>Брак.</summary>
    public int QuantityDefected { get; set; }

    // --- Деньги (long), из ЛК ---
    /// <summary>Цена продажи.</summary>
    public long Price { get; set; }

    /// <summary>Закуп из ЛК — не доверять (может быть пустым/мусором).</summary>
    public long PurchasePrice { get; set; }

    /// <summary>ИКПУ-код (нужен для ЭТТН, выгрузка в Excel).</summary>
    public string? Ikpu { get; set; }

    public string? SellerItemCode { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
