namespace SellerTools.Core.Entities;

/// <summary>
/// Вид синка для инкрементальной выкачки (чтобы не делать полных перекачек).
/// </summary>
public enum SyncKind
{
    Products = 0,
    FinanceOrders = 1,
    FinanceExpenses = 2
}

/// <summary>
/// Курсор инкрементального синка. Составной ключ: ShopId + Kind.
/// Хранит момент, с которого продолжать (dateFrom для finance/*).
/// </summary>
public class SyncCursor
{
    public long ShopId { get; set; }
    public SyncKind Kind { get; set; }

    /// <summary>Последняя успешно синхронизированная точка.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Курсор в виде значения (epoch millis / ISO — уточнить в В2).</summary>
    public long? LastCursorValue { get; set; }
}
