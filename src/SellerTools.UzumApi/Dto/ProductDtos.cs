using System.Text.Json.Serialization;

namespace SellerTools.UzumApi.Dto;

// DTO для GET /v1/product/shop/{shopId}.
// Только поля, подтверждённые в swagger (см. CLAUDE.md). Лишнее не выдумываем.
// Деньги — long (единицы уточнить живым ключом, В2).

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

    /// <summary>Цена продажи (long — единицы уточнить, В2).</summary>
    [JsonPropertyName("price")]
    public long Price { get; set; }

    /// <summary>Закуп из ЛК — не доверять.</summary>
    [JsonPropertyName("purchasePrice")]
    public long PurchasePrice { get; set; }

    [JsonPropertyName("commission")]
    public long Commission { get; set; }

    [JsonPropertyName("paidStorageAmount")]
    public long PaidStorageAmount { get; set; }

    /// <summary>Средние дневные продажи.</summary>
    [JsonPropertyName("avgdsales")]
    public double AvgDSales { get; set; }

    /// <summary>ИКПУ-код (для ЭТТН).</summary>
    [JsonPropertyName("ikpu")]
    public string? Ikpu { get; set; }

    [JsonPropertyName("sellerItemCode")]
    public string? SellerItemCode { get; set; }

    [JsonPropertyName("article")]
    public string? Article { get; set; }

    [JsonPropertyName("barcode")]
    public long Barcode { get; set; }

    [JsonPropertyName("archived")]
    public bool Archived { get; set; }

    [JsonPropertyName("blocked")]
    public bool Blocked { get; set; }
}
