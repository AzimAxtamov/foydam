namespace SellerTools.UzumApi;

/// <summary>Режим клиента API.</summary>
public enum UzumApiMode
{
    Mock = 0,
    Real = 1
}

/// <summary>
/// Настройки клиента Uzum Seller API (секция "UzumApi" в конфиге).
/// </summary>
public class UzumApiOptions
{
    public const string SectionName = "UzumApi";

    public UzumApiMode Mode { get; set; } = UzumApiMode.Mock;

    public string BaseUrl { get; set; } = "https://api-seller.uzum.uz";

    /// <summary>
    /// Имя auth-заголовка. НЕ ПОДТВЕРЖДЕНО — дефолт Authorization.
    /// </summary>
    public string AuthHeaderName { get; set; } = "Authorization";

    /// <summary>
    /// Схема auth. Дефолт "Bearer". Пустая строка = сырое значение ключа в заголовке.
    /// </summary>
    public string AuthScheme { get; set; } = "Bearer";

    /// <summary>Кол-во ретраев при 429/5xx.</summary>
    public int MaxRetries { get; set; } = 4;

    /// <summary>Базовая задержка бэкоффа (мс), растёт экспоненциально.</summary>
    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>Размер страницы при постраничном обходе товаров.</summary>
    public int PageSize { get; set; } = 100;
}
