namespace SellerTools.Core.Entities;

/// <summary>
/// Накладная поставки FBO (/v1/invoice), сохранённая в БД. PK = Id из API (upsert).
/// Принятые (ACCEPTED) накладные — неизменяемый факт: качаем один раз, храним навсегда.
/// Непринятые (CREATED и др.) — pending: могут ещё стать ACCEPTED, дельта их досинкивает.
/// </summary>
public class InvoiceRecord
{
    /// <summary>id накладной из Uzum API (PK, идемпотентный upsert).</summary>
    public long Id { get; set; }

    public long ShopId { get; set; }

    /// <summary>Человекочитаемый номер накладной (invoiceNumber).</summary>
    public long? InvoiceNumber { get; set; }

    /// <summary>Статус из invoiceStatus.value (ACCEPTED | CREATED | …), нормализован в UPPER.</summary>
    public string Status { get; set; } = "";

    /// <summary>
    /// Дата создания. ⚠️ В спеке type:string без format — формат неизвестен, парсим
    /// толерантно; при непарсибельности здесь null (дельта тогда фолбэчит на полный синк).
    /// </summary>
    public DateTimeOffset? DateCreated { get; set; }

    /// <summary>Дата приёмки (dateAccepted).</summary>
    public DateTimeOffset? DateAccepted { get; set; }

    /// <summary>Итог принятого по накладной (totalAccepted).</summary>
    public int TotalAccepted { get; set; }

    /// <summary>Итог к размещению на складе (totalToStock).</summary>
    public int TotalToStock { get; set; }

    /// <summary>ACCEPTED — терминальное (неизменяемое) состояние. Хранится для дельты.</summary>
    public bool IsTerminal { get; set; }

    /// <summary>Момент последнего сохранения/обновления строки нами.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<InvoiceItem> Items { get; set; } = new();
}

/// <summary>
/// Строка SKU накладной. accepted берём отсюда (только по накладным ACCEPTED).
/// SkuKeyNorm — нормализованный ключ матча (SkuKey.Normalize), по нему join с картой.
/// </summary>
public class InvoiceItem
{
    public long Id { get; set; }

    public long InvoiceId { get; set; }
    public InvoiceRecord? Invoice { get; set; }

    /// <summary>Сырое skuTitle накладной (полный код SKU) — для аудита.</summary>
    public string? SkuTitleRaw { get; set; }

    /// <summary>Нормализованный ключ матча (SkuKey.Normalize от SkuTitleRaw).</summary>
    public string SkuKeyNorm { get; set; } = "";

    public int QuantityToStock { get; set; }
    public int QuantityAccepted { get; set; }
}
