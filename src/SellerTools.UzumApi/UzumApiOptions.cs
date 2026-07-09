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

    /// <summary>
    /// Канонический base-path подтверждён живым ключом: только `/api/seller-openapi/`
    /// доходит до самого Seller API (даёт JSON-envelope). Путь без префикса режет
    /// внешний шлюз ("RBAC: access denied"). Обязателен слеш в конце.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api-seller.uzum.uz/api/seller-openapi/";

    /// <summary>
    /// Имя auth-заголовка. Подтверждено спекой (securitySchemes.TokenAuth): Authorization.
    /// </summary>
    public string AuthHeaderName { get; set; } = "Authorization";

    /// <summary>
    /// Схема auth. По спеке — «Токен авторизации без префикса Bearer», поэтому
    /// дефолт ПУСТОЙ (сырое значение ключа в заголовке). Прежний "Bearer" давал 403.
    /// Конфигурируемо на случай, если живой ключ покажет иное.
    /// </summary>
    public string AuthScheme { get; set; } = "";

    /// <summary>Кол-во ретраев при 429/5xx.</summary>
    public int MaxRetries { get; set; } = 4;

    /// <summary>Базовая задержка бэкоффа (мс), растёт экспоненциально.</summary>
    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>Размер страницы при постраничном обходе товаров.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// Максимум одновременных запросов к Uzum API. При большем край рвёт коннекты
    /// (проверено живьём). Гейт общий на процесс. По умолчанию 3.
    /// </summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>
    /// Порог остатка дневного лимита (доля от 0 до 1), ниже которого синк делает
    /// паузу между страницами и пишет предупреждение. По умолчанию 10%.
    /// </summary>
    public double RateLimitPauseThreshold { get; set; } = 0.10;

    /// <summary>Длительность паузы (мс) при низком остатке дневного лимита.</summary>
    public int RateLimitPauseMs { get; set; } = 60_000;

    /// <summary>
    /// Время жизни соединения в пуле (сек). По истечении — соединение закрывается
    /// и пересоздаётся. Защита от переиспользования «мёртвого» keep-alive: край Uzum
    /// рвёт соединение после ответа, а .NET иначе держит его в пуле и падает
    /// HttpRequestException на следующем запросе. 60с — компромисс (частые запросы
    /// переиспользуют свежее соединение, но не «залипают» на мёртвом).
    /// </summary>
    public int PooledConnectionLifetimeSeconds { get; set; } = 60;
}
