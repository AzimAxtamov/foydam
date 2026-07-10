namespace SellerTools.UzumApi.Dto;

/// <summary>
/// Одна страница «голого массива» ручки (/v1/invoice, /v1/return) + rate-limit
/// заголовки этого ответа. Синк управляет пагинацией сам (дельта: идём с page=0 до
/// точки останова), поэтому нужен доступ к каждой странице, а не «забрать всё».
/// </summary>
public class ApiPage<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>Последняя страница (пришло меньше size — дальше пусто).</summary>
    public bool IsLast { get; init; }

    /// <summary>Rate-limit заголовки ответа (имя → значение), как пришли.</summary>
    public IReadOnlyDictionary<string, string> RateLimitHeaders { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Остаток дневного лимита (x-ratelimit-remaining-per-day), если пришёл.</summary>
    public long? RemainingPerDay => TryLong("x-ratelimit-remaining-per-day");

    /// <summary>Дневной лимит (x-ratelimit-limit-per-day), если пришёл.</summary>
    public long? LimitPerDay => TryLong("x-ratelimit-limit-per-day");

    private long? TryLong(string header) =>
        RateLimitHeaders.TryGetValue(header, out var v) && long.TryParse(v, out var n) ? n : null;
}
