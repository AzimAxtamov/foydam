namespace SellerTools.UzumApi.Dto;

/// <summary>
/// Одна страница товаров + rate-limit заголовки этого ответа. Позволяет синку
/// смотреть остаток лимита между страницами и паузиться при необходимости.
/// </summary>
public class ProductsPage
{
    public IReadOnlyList<ProductDto> Products { get; init; } = Array.Empty<ProductDto>();

    /// <summary>Это последняя страница (меньше size — дальше идти не нужно).</summary>
    public bool IsLast { get; init; }

    /// <summary>Rate-limit заголовки ответа (имя → значение), как пришли.</summary>
    public IReadOnlyDictionary<string, string> RateLimitHeaders { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Остаток дневного лимита (x-ratelimit-remaining-per-day), если пришёл.</summary>
    public long? RemainingPerDay =>
        TryLong("x-ratelimit-remaining-per-day");

    /// <summary>Дневной лимит (x-ratelimit-limit-per-day), если пришёл.</summary>
    public long? LimitPerDay =>
        TryLong("x-ratelimit-limit-per-day");

    private long? TryLong(string header) =>
        RateLimitHeaders.TryGetValue(header, out var v) && long.TryParse(v, out var n) ? n : null;
}
