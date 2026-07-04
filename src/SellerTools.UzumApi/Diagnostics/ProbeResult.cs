using System.Net;

namespace SellerTools.UzumApi.Diagnostics;

/// <summary>Какой GET-эндпоинт прогоняем в диагностике.</summary>
public enum ProbeEndpoint
{
    Products = 0,       // GET /v1/product/shop/{shopId}
    FinanceOrders = 1,  // GET /v1/finance/orders
    FinanceExpenses = 2 // GET /v1/finance/expenses
}

/// <summary>
/// Сырой результат одиночного диагностического запроса: статус, rate-limit
/// заголовки и начало тела ответа. Ключ сюда не попадает.
/// </summary>
public class ProbeResult
{
    public ProbeEndpoint Endpoint { get; init; }

    /// <summary>Относительный URL запроса (без ключа).</summary>
    public string Url { get; init; } = "";

    /// <summary>true, если запрос вообще выполнился (иначе см. Error).</summary>
    public bool Executed { get; init; }

    /// <summary>Сетевая/иная ошибка до получения ответа (если была).</summary>
    public string? Error { get; init; }

    public HttpStatusCode? StatusCode { get; init; }
    public bool IsSuccess => StatusCode.HasValue && (int)StatusCode.Value is >= 200 and < 300;

    /// <summary>Rate-limit заголовки ответа (имя → значение), в порядке из swagger.</summary>
    public IReadOnlyDictionary<string, string> RateLimitHeaders { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Начало тела ответа (обрезано), для глазами-проверки формата.</summary>
    public string BodyPreview { get; init; } = "";

    /// <summary>Длительность запроса, мс.</summary>
    public long ElapsedMs { get; init; }
}
