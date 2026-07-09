using System.Text.Json;
using System.Text.Json.Serialization;

namespace SellerTools.UzumApi.Dto;

// DTO для балансовой реконсиляции потерь (GET /v1/invoice, GET /v1/return).
// Обе ручки отдают ГОЛЫЙ JSON-массив (не envelope), пагинация size<=50, page с 0.
// Матч SKU — ТОЛЬКО по skuTitle: поле id в накладных/возвратах — это внутренний
// id строки, НЕ skuId карточки. Числа приходят nullable → accessor'ы с ?? 0.

// ---------------------------------------------------------------------------
// /v1/invoice — накладные поставки FBO. Схема InvoiceWithSkuListDto.
// accepted = сумма quantityAccepted по SKU, только по накладным ACCEPTED.

public class InvoiceDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Текущий статус накладной (invoiceStatus.value = ACCEPTED | CREATED | …).</summary>
    [JsonPropertyName("invoiceStatus")]
    public InvoiceStatusDto? InvoiceStatus { get; set; }

    [JsonPropertyName("productForInvoiceDto")]
    public List<ProductForInvoiceDto> Products { get; set; } = new();
}

public class InvoiceStatusDto
{
    /// <summary>Ключевое значение статуса: ACCEPTED (принято складом) или CREATED (черновик).</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public class ProductForInvoiceDto
{
    [JsonPropertyName("productTitle")]
    public string? ProductTitle { get; set; }

    [JsonPropertyName("skuForInvoiceDtoList")]
    public List<SkuForInvoiceDto> Skus { get; set; } = new();
}

public class SkuForInvoiceDto
{
    /// <summary>⚠️ Внутренний id строки накладной — НЕ skuId карточки. Матчить не по нему.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Единственный надёжный ключ матча SKU в накладных.</summary>
    [JsonPropertyName("skuTitle")]
    public string? SkuTitle { get; set; }

    /// <summary>Сколько единиц принято складом. Может быть null → 0.</summary>
    [JsonPropertyName("quantityAccepted")]
    public int? QuantityAcceptedRaw { get; set; }
    [JsonIgnore] public int QuantityAccepted => QuantityAcceptedRaw ?? 0;
}

// ---------------------------------------------------------------------------
// /v1/return — возвраты (физический вывоз со склада). Схема SellerReturnDto.
// removed по legacy-правилу: если totalPackedAmount==0 И assembledDate==null →
// берём amount строки; иначе packedAmount.

public class SellerReturnDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>
    /// Дата сборки возврата. Живой API отдаёт её РАЗНЫМИ типами (строка ISO, epoch
    /// millis числом, либо null), поэтому принимаем сырой JsonElement — иначе STJ
    /// падает "could not be converted to System.String" на числовом значении.
    /// Для legacy-правила важен только факт наличия/отсутствия (null).
    /// </summary>
    [JsonPropertyName("assembledDate")]
    public JsonElement? AssembledDate { get; set; }

    /// <summary>Есть ли непустая дата сборки (учитывает и число, и строку, и JSON null).</summary>
    [JsonIgnore]
    public bool HasAssembledDate =>
        AssembledDate is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } el
        && !(el.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(el.GetString()));

    /// <summary>Общее упакованное кол-во по возврату. 0 при legacy-правиле.</summary>
    [JsonPropertyName("totalPackedAmount")]
    public int TotalPackedAmount { get; set; }

    [JsonPropertyName("returnItems")]
    public List<SellerReturnItemDto> ReturnItems { get; set; } = new();

    /// <summary>
    /// Legacy-правило вывоза: если по родительскому возврату totalPackedAmount==0
    /// И assembledDate отсутствует → в реальности упакованное кол-во не проставлено,
    /// берём amount строки; иначе packedAmount.
    /// </summary>
    [JsonIgnore]
    public bool UseAmount => TotalPackedAmount == 0 && !HasAssembledDate;
}

public class SellerReturnItemDto
{
    /// <summary>⚠️ skuId возврата НЕ совпадает с id накладной — но для матча берём title.</summary>
    [JsonPropertyName("skuId")]
    public long SkuId { get; set; }

    /// <summary>Ключ матча SKU (единый с накладными).</summary>
    [JsonPropertyName("skuTitle")]
    public string? SkuTitle { get; set; }

    /// <summary>Заявленное кол-во к возврату. Может быть null → 0.</summary>
    [JsonPropertyName("amount")]
    public int? AmountRaw { get; set; }
    [JsonIgnore] public int Amount => AmountRaw ?? 0;

    /// <summary>Упакованное кол-во. Может быть null → 0.</summary>
    [JsonPropertyName("packedAmount")]
    public int? PackedAmountRaw { get; set; }
    [JsonIgnore] public int PackedAmount => PackedAmountRaw ?? 0;
}
