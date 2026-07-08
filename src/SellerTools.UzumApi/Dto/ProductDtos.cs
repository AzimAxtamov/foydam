using System.Text.Json.Serialization;

namespace SellerTools.UzumApi.Dto;

// DTO для GET /v1/product/shop/{shopId}.
// ⚠️ [ЖИВОЙ КЛЮЧ В2] Многие числовые поля SKU приходят как null (purchasePrice,
// paidStorage*, article, avgdsales) или как double (commission=19.5). Поэтому
// JSON-поля НУЛЛАБЕЛЬНЫЕ (…Raw), а наружу отдаём не-null accessor'ы (?? 0), чтобы
// STJ не падал JsonException на null→long / 19.5→long. Деньги = СУМ (подтверждено).

public class ProductShopResponse
{
    [JsonPropertyName("productList")]
    public List<ProductDto> ProductList { get; set; } = new();

    [JsonPropertyName("totalProductsAmount")]
    public int TotalProductsAmount { get; set; }
}

public class ProductDto
{
    [JsonPropertyName("productId")]
    public long ProductId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("skuList")]
    public List<SkuDto> SkuList { get; set; } = new();

    [JsonPropertyName("quantityActive")]
    public int QuantityActive { get; set; }

    [JsonPropertyName("quantityFbs")]
    public int QuantityFbs { get; set; }
}

public class SkuDto
{
    [JsonPropertyName("skuId")]
    public long SkuId { get; set; }

    [JsonPropertyName("skuTitle")]
    public string? SkuTitle { get; set; }

    [JsonPropertyName("skuFullTitle")]
    public string? SkuFullTitle { get; set; }

    [JsonPropertyName("productTitle")]
    public string? ProductTitle { get; set; }

    [JsonPropertyName("quantityActive")]
    public int QuantityActive { get; set; }

    [JsonPropertyName("quantitySold")]
    public int QuantitySold { get; set; }

    [JsonPropertyName("quantityReturned")]
    public int QuantityReturned { get; set; }

    /// <summary>Потеряно складом Uzum.</summary>
    [JsonPropertyName("quantityMissing")]
    public int QuantityMissing { get; set; }

    /// <summary>Брак.</summary>
    [JsonPropertyName("quantityDefected")]
    public int QuantityDefected { get; set; }

    /// <summary>Цена продажи (СУМ). API может вернуть null → отдаём 0.</summary>
    [JsonPropertyName("price")]
    public long? PriceRaw { get; set; }
    [JsonIgnore] public long Price => PriceRaw ?? 0;

    /// <summary>Закуп из ЛК (СУМ) — не доверять. Часто null → 0.</summary>
    [JsonPropertyName("purchasePrice")]
    public long? PurchasePriceRaw { get; set; }
    [JsonIgnore] public long PurchasePrice => PurchasePriceRaw ?? 0;

    /// <summary>Комиссия — double (в живых данных это процент, напр. 19.5), может быть null.</summary>
    [JsonPropertyName("commission")]
    public double? Commission { get; set; }

    /// <summary>Плата за хранение (СУМ), может быть null.</summary>
    [JsonPropertyName("paidStorageAmount")]
    public long? PaidStorageAmount { get; set; }

    /// <summary>Средние дневные продажи, может быть null.</summary>
    [JsonPropertyName("avgdsales")]
    public double? AvgDSales { get; set; }

    /// <summary>ИКПУ-код (для ЭТТН). Строка — ведущие нули значимы.</summary>
    [JsonPropertyName("ikpu")]
    public string? Ikpu { get; set; }

    [JsonPropertyName("sellerItemCode")]
    public string? SellerItemCode { get; set; }

    [JsonPropertyName("article")]
    public string? Article { get; set; }

    /// <summary>Штрихкод (int64). Может быть null → отдаём 0.</summary>
    [JsonPropertyName("barcode")]
    public long? BarcodeRaw { get; set; }
    [JsonIgnore] public long Barcode => BarcodeRaw ?? 0;

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    [JsonPropertyName("blocked")]
    public bool Blocked { get; set; }
}
