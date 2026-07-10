using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SellerTools.UzumApi.Dto;

// DTO для балансовой реконсиляции потерь (GET /v1/invoice, GET /v1/return).
// Обе ручки отдают ГОЛЫЙ JSON-массив (не envelope), пагинация size<=50, page с 0.
// Матч SKU — ТОЛЬКО по skuTitle: поле id в накладных/возвратах — это внутренний
// id строки, НЕ skuId карточки. Числа приходят nullable → accessor'ы с ?? 0.
//
// Поля и типы взяты из docs/uzum-openapi.json (схемы InvoiceWithSkuListDto,
// SellerReturnDto, ProductForInvoiceDto, SkuForInvoiceDto, SellerReturnItemDto).
// ⚠️ Иерархия доверия: живой JSON > спека (в спеке уже находили ошибки типов).
// Все даты принимаем ТОЛЕРАНТНО (см. FlexibleDate): в этом API даты приходят и
// epoch-числом, и ISO-строкой — жёсткий тип STJ роняет весь разбор страницы.

// ---------------------------------------------------------------------------
// FlexibleDate — толерантный разбор дат. Uzum отдаёт даты РАЗНЫМИ типами:
//   - ISO-строка ("2026-06-01T10:00:00Z"),
//   - epoch millis числом (1774693507364),
//   - null / отсутствие поля.
// Спека часто обещает format:date-time, но у invoice.dateCreated format ВООБЩЕ
// НЕ УКАЗАН (TODO(verify-live) — уточнить живым ключом). Поэтому не привязываемся
// к одному формату: храним сырой JsonElement, наружу отдаём DateTimeOffset? и флаг
// наличия. Непарсибельная дата → null (вызывающий логирует warning, см. правила).

public static class FlexibleDate
{
    /// <summary>Есть ли непустое значение (не null/undefined и не пустая строка).</summary>
    public static bool HasValue(JsonElement? el) =>
        el is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } e
        && !(e.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(e.GetString()));

    /// <summary>
    /// Толерантный разбор в DateTimeOffset (UTC). Число → epoch millis (13 цифр) или
    /// секунды (10 цифр). Строка → ISO (в т.ч. с наносекундами/без TZ). Непарсибельно
    /// → null. НЕ бросает — иерархия доверия «живой JSON > спека».
    /// </summary>
    public static DateTimeOffset? Parse(JsonElement? el)
    {
        if (!HasValue(el))
            return null;
        var e = el!.Value;

        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var epoch))
        {
            // 13 цифр — миллисекунды, 10 — секунды (эвристика по величине).
            return epoch >= 1_000_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                : DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        if (e.ValueKind == JsonValueKind.String)
        {
            var s = e.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return null;
            s = s.Trim();

            // ⚠️ [ЖИВОЙ КЛЮЧ] invoice.dateCreated приходит как "22.08.2023" (dd.MM.yyyy) —
            // спека обещала string без формата, реальность = день.месяц.год. InvariantCulture
            // в TryParse ждёт MM/dd/yyyy и такое НЕ парсит → лавина warning + null. Поэтому
            // сначала пробуем известные явные форматы Uzum, только потом общий разбор.
            if (DateTimeOffset.TryParseExact(
                    s, ExplicitFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var exact))
                return exact;

            if (DateTimeOffset.TryParse(
                    s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                return dto;

            // ISO с наносекундами без TZ (напр. "2026-07-08T11:40:14.999493203") —
            // TryParse на .NET может споткнуться; берём первые 7 знаков дробной части.
            if (DateTime.TryParse(
                    s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        return null; // непарсибельно — вызывающий пусть логирует
    }

    // Явные форматы дат Uzum (подтверждено живым ключом: invoice.dateCreated = dd.MM.yyyy).
    // Держим набор с временем и без — на случай, если поле придёт с часами/минутами.
    private static readonly string[] ExplicitFormats =
    {
        "dd.MM.yyyy",
        "dd.MM.yyyy HH:mm:ss",
        "dd.MM.yyyy HH:mm",
        "yyyy-MM-dd",
    };
}

// ---------------------------------------------------------------------------
// /v1/invoice — накладные поставки FBO. Схема InvoiceWithSkuListDto.
// accepted = сумма quantityAccepted по SKU, только по накладным ACCEPTED.

public class InvoiceDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("shopId")]
    public long? ShopId { get; set; }

    [JsonPropertyName("invoiceNumber")]
    public long? InvoiceNumber { get; set; }

    /// <summary>
    /// Дата создания накладной. ⚠️ В спеке type:string БЕЗ format — реальный формат
    /// неизвестен (epoch или ISO). Парсим толерантно. TODO(verify-live): подтвердить
    /// формат живым ключом. Нужна для порядка страниц в дельта-синке.
    /// </summary>
    [JsonPropertyName("dateCreated")]
    public JsonElement? DateCreatedRaw { get; set; }
    [JsonIgnore] public DateTimeOffset? DateCreated => FlexibleDate.Parse(DateCreatedRaw);

    /// <summary>Дата приёмки накладной (spec: date-time). Толерантный разбор.</summary>
    [JsonPropertyName("dateAccepted")]
    public JsonElement? DateAcceptedRaw { get; set; }
    [JsonIgnore] public DateTimeOffset? DateAccepted => FlexibleDate.Parse(DateAcceptedRaw);

    /// <summary>Устаревшее строковое поле статуса (спека: deprecated). Источник истины — invoiceStatus.value.</summary>
    [JsonPropertyName("status")]
    public string? StatusDeprecated { get; set; }

    /// <summary>Текущий статус накладной (invoiceStatus.value = ACCEPTED | CREATED | …).</summary>
    [JsonPropertyName("invoiceStatus")]
    public InvoiceStatusDto? InvoiceStatus { get; set; }

    /// <summary>Итог принятого по накладной (spec int32). Может быть null → 0.</summary>
    [JsonPropertyName("totalAccepted")]
    public int? TotalAcceptedRaw { get; set; }
    [JsonIgnore] public int TotalAccepted => TotalAcceptedRaw ?? 0;

    /// <summary>Итог к размещению на складе (spec int32). Может быть null → 0.</summary>
    [JsonPropertyName("totalToStock")]
    public int? TotalToStockRaw { get; set; }
    [JsonIgnore] public int TotalToStock => TotalToStockRaw ?? 0;

    // ⚠️ [ЖИВОЙ КЛЮЧ] STJ при JSON "productForInvoiceDto": null ЗАТИРАЕТ инициализатор
    // = new() и ставит null → foreach в синке падал NullReferenceException (мок такого
    // не давал). Backing-поле nullable, наружу — всегда не-null список.
    [JsonPropertyName("productForInvoiceDto")]
    public List<ProductForInvoiceDto>? ProductsRaw { get; set; }
    [JsonIgnore] public List<ProductForInvoiceDto> Products => ProductsRaw ??= new();

    /// <summary>Нормализованное значение статуса в верхнем регистре (для сравнения).</summary>
    [JsonIgnore]
    public string StatusValue => (InvoiceStatus?.Value ?? "").Trim().ToUpperInvariant();

    /// <summary>ACCEPTED — накладная принята складом (участвует в accepted).</summary>
    [JsonIgnore]
    public bool IsAccepted => StatusValue == UzumConstants.InvoiceStatus.Accepted;

    /// <summary>
    /// Терминален ли статус накладной для дельта-синка. ACCEPTED — конечное состояние
    /// (факт неизменяем). Всё остальное (CREATED и любое иное) считаем НЕ терминальным:
    /// накладная может ещё перейти в ACCEPTED → при дельте досинкиваем глубже.
    /// </summary>
    [JsonIgnore]
    public bool IsTerminal => IsAccepted;
}

public class InvoiceStatusDto
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Ключевое значение статуса: ACCEPTED (принято складом) или CREATED (черновик) и т.п.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

public class ProductForInvoiceDto
{
    [JsonPropertyName("productTitle")]
    public string? ProductTitle { get; set; }

    // Null-safe: STJ при JSON null затирает = new() → null. Наружу всегда список.
    [JsonPropertyName("skuForInvoiceDtoList")]
    public List<SkuForInvoiceDto>? SkusRaw { get; set; }
    [JsonIgnore] public List<SkuForInvoiceDto> Skus => SkusRaw ??= new();
}

public class SkuForInvoiceDto
{
    /// <summary>⚠️ Внутренний id строки накладной — НЕ skuId карточки. Матчить не по нему.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Единственный надёжный ключ матча SKU в накладных (полный код SKU).</summary>
    [JsonPropertyName("skuTitle")]
    public string? SkuTitle { get; set; }

    /// <summary>Сколько единиц принято складом. Может быть null → 0.</summary>
    [JsonPropertyName("quantityAccepted")]
    public int? QuantityAcceptedRaw { get; set; }
    [JsonIgnore] public int QuantityAccepted => QuantityAcceptedRaw ?? 0;

    /// <summary>Сколько единиц должно попасть на склад. Может быть null → 0.</summary>
    [JsonPropertyName("quantityToStock")]
    public int? QuantityToStockRaw { get; set; }
    [JsonIgnore] public int QuantityToStock => QuantityToStockRaw ?? 0;
}

// ---------------------------------------------------------------------------
// /v1/return — возвраты (физический вывоз со склада). Схема SellerReturnDto.
// removed по legacy-правилу: если totalPackedAmount==0 И assembledDate==null →
// берём amount строки; иначе packedAmount.

public class SellerReturnDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Дата создания возврата (spec: date-time). Толерантный разбор. Порядок в дельте.</summary>
    [JsonPropertyName("dateCreated")]
    public JsonElement? DateCreatedRaw { get; set; }
    [JsonIgnore] public DateTimeOffset? DateCreated => FlexibleDate.Parse(DateCreatedRaw);

    /// <summary>Строковый статус возврата (spec: string, без enum-словаря).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Тип возврата: DEFECTED | RETURN | FBS.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Дата сборки возврата. Живой API отдаёт её РАЗНЫМИ типами (строка ISO, epoch
    /// millis числом, либо null). Для legacy-правила важен только факт наличия.
    /// </summary>
    [JsonPropertyName("assembledDate")]
    public JsonElement? AssembledDate { get; set; }

    /// <summary>Дата завершения возврата (spec: date-time). Терминальный статус для дельты.</summary>
    [JsonPropertyName("completedDate")]
    public JsonElement? CompletedDateRaw { get; set; }
    [JsonIgnore] public DateTimeOffset? CompletedDate => FlexibleDate.Parse(CompletedDateRaw);

    /// <summary>Дата отмены возврата (spec: date-time). Терминальный статус для дельты.</summary>
    [JsonPropertyName("canceledDate")]
    public JsonElement? CanceledDateRaw { get; set; }
    [JsonIgnore] public DateTimeOffset? CanceledDate => FlexibleDate.Parse(CanceledDateRaw);

    /// <summary>Есть ли непустая дата сборки (учитывает и число, и строку, и JSON null).</summary>
    [JsonIgnore]
    public bool HasAssembledDate => FlexibleDate.HasValue(AssembledDate);

    /// <summary>Общее упакованное кол-во по возврату. 0 при legacy-правиле.</summary>
    [JsonPropertyName("totalPackedAmount")]
    public int TotalPackedAmount { get; set; }

    /// <summary>Общее заявленное кол-во по возврату (spec int32).</summary>
    [JsonPropertyName("totalAmount")]
    public int TotalAmount { get; set; }

    // Null-safe: STJ при JSON null затирает = new() → null. Наружу всегда список.
    [JsonPropertyName("returnItems")]
    public List<SellerReturnItemDto>? ReturnItemsRaw { get; set; }
    [JsonIgnore] public List<SellerReturnItemDto> ReturnItems => ReturnItemsRaw ??= new();

    /// <summary>
    /// Legacy-правило вывоза: если по родительскому возврату totalPackedAmount==0
    /// И assembledDate отсутствует → в реальности упакованное кол-во не проставлено,
    /// берём amount строки; иначе packedAmount.
    /// </summary>
    [JsonIgnore]
    public bool UseAmount => TotalPackedAmount == 0 && !HasAssembledDate;

    /// <summary>
    /// Терминален ли возврат для дельта-синка: завершён (completedDate) или отменён
    /// (canceledDate). Без обеих дат — ещё в работе (pending), при дельте досинкиваем.
    /// </summary>
    [JsonIgnore]
    public bool IsTerminal =>
        FlexibleDate.HasValue(CompletedDateRaw) || FlexibleDate.HasValue(CanceledDateRaw);
}

public class SellerReturnItemDto
{
    /// <summary>⚠️ skuId возврата НЕ совпадает с id накладной — но для матча берём title.</summary>
    [JsonPropertyName("skuId")]
    public long SkuId { get; set; }

    /// <summary>Ключ матча SKU (единый с накладными — полный код SKU).</summary>
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
