namespace SellerTools.Core.Entities;

/// <summary>
/// Возврат/физвывоз со склада (/v1/return), сохранённый в БД. PK = Id из API (upsert).
/// Завершённый (completedDate) или отменённый (canceledDate) возврат — неизменяемый
/// факт. Без обеих дат — pending: дельта досинкивает до смены статуса.
/// </summary>
public class ReturnRecord
{
    /// <summary>id возврата из Uzum API (PK, идемпотентный upsert).</summary>
    public long Id { get; set; }

    public long ShopId { get; set; }

    /// <summary>Тип возврата: DEFECTED | RETURN | FBS.</summary>
    public string? Type { get; set; }

    /// <summary>Строковый статус возврата (без enum-словаря в спеке).</summary>
    public string? Status { get; set; }

    /// <summary>Дата создания (порядок в дельте). Толерантный разбор, при неудаче null.</summary>
    public DateTimeOffset? DateCreated { get; set; }

    /// <summary>Дата сборки: важна для legacy-правила (null → ветка amount).</summary>
    public DateTimeOffset? AssembledDate { get; set; }

    /// <summary>Дата завершения (терминальный статус).</summary>
    public DateTimeOffset? CompletedDate { get; set; }

    /// <summary>Дата отмены (терминальный статус).</summary>
    public DateTimeOffset? CanceledDate { get; set; }

    public int TotalAmount { get; set; }
    public int TotalPackedAmount { get; set; }

    /// <summary>
    /// Флаг legacy-правила, ЗАФИКСИРОВАННЫЙ при сохранении: если totalPackedAmount==0 И
    /// assembledDate отсутствовала → true (removed берём из amount), иначе false
    /// (removed из packedAmount). Считаем один раз тем же правилом, что и раньше в памяти.
    /// </summary>
    public bool UseAmount { get; set; }

    /// <summary>Завершён или отменён — терминальное состояние. Для дельты.</summary>
    public bool IsTerminal { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ReturnItem> Items { get; set; } = new();
}

/// <summary>
/// Строка SKU возврата. removed по legacy-правилу родителя (UseAmount): amount либо
/// packedAmount. SkuKeyNorm — нормализованный ключ матча.
/// </summary>
public class ReturnItem
{
    public long Id { get; set; }

    public long ReturnId { get; set; }
    public ReturnRecord? Return { get; set; }

    /// <summary>skuId возврата — внутренний, по нему НЕ матчим (храним для аудита).</summary>
    public long SkuId { get; set; }

    /// <summary>Сырое skuTitle возврата (полный код SKU) — для аудита.</summary>
    public string? SkuTitleRaw { get; set; }

    /// <summary>Нормализованный ключ матча (SkuKey.Normalize от SkuTitleRaw).</summary>
    public string SkuKeyNorm { get; set; } = "";

    public int Amount { get; set; }
    public int PackedAmount { get; set; }
}
