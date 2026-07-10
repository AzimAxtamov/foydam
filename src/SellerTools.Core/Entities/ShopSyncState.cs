namespace SellerTools.Core.Entities;

/// <summary>Состояние синхронизации магазина.</summary>
public enum SyncStatus
{
    /// <summary>Ни разу не синхронизировали — нужен полный первичный синк.</summary>
    Never = 0,

    /// <summary>Идёт синк (полный или дельта).</summary>
    Syncing = 1,

    /// <summary>Данные в БД готовы к построению отчёта.</summary>
    Ready = 2,

    /// <summary>Последний синк упал (см. LastError).</summary>
    Error = 3
}

/// <summary>
/// Состояние синхронизации магазина: статус + курсоры полного/дельта-синка.
/// PK = ShopId. Отчёт смотрит сюда: Never → полный синк + «собираю первый раз»;
/// данные старше порога → дельта; иначе — мгновенно из БД.
/// </summary>
public class ShopSyncState
{
    public long ShopId { get; set; }

    public SyncStatus Status { get; set; } = SyncStatus.Never;

    /// <summary>Когда завершился последний УСПЕШНЫЙ полный синк.</summary>
    public DateTimeOffset? LastFullSyncAt { get; set; }

    /// <summary>Когда завершился последний УСПЕШНЫЙ дельта-синк накладных/возвратов.</summary>
    public DateTimeOffset? LastDeltaSyncAt { get; set; }

    /// <summary>Когда последний раз освежали карточки SKU (порог свежести отчёта).</summary>
    public DateTimeOffset? LastCardsSyncAt { get; set; }

    /// <summary>Текст последней ошибки синка (для админ-диагностики; юзеру не показываем).</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Магазин недоступен ключом (403 «Shop is not available»). Помечаем и пропускаем
    /// в ночных синках; юзеру сырую ошибку не показываем.
    /// </summary>
    public bool Inactive { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
