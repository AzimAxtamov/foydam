using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SellerTools.UzumApi.Diagnostics;
using SellerTools.UzumApi.Dto;

namespace SellerTools.UzumApi;

/// <summary>
/// Реальный клиент Uzum Seller API поверх HttpClient.
/// - конфигурируемый auth-заголовок/схема (имя не подтверждено, В2);
/// - читает и логирует rate-limit заголовки каждого ответа;
/// - ретраит 429/5xx с экспоненциальным бэкоффом (учитывает Retry-After);
/// - постранично обходит товары.
/// Ключ в логи НЕ попадает.
/// </summary>
public class RealUzumApiClient : IUzumApiClient
{
    private readonly HttpClient _http;
    private readonly UzumApiOptions _options;
    private readonly ILogger<RealUzumApiClient> _log;

    // Заголовки rate-limit из swagger — читаем и логируем (значения узнаём живым ключом).
    private static readonly string[] RateLimitHeaders =
    {
        "x-ratelimit-remaining",
        "x-ratelimit-replenish-rate",
        "x-ratelimit-burst-capacity",
        "x-ratelimit-requested-tokens",
        "x-ratelimit-limit-per-day",
        "x-ratelimit-remaining-per-day"
    };

    public RealUzumApiClient(
        HttpClient http,
        IOptions<UzumApiOptions> options,
        ILogger<RealUzumApiClient> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<IReadOnlyList<ShopDto>> GetShopsAsync(
        string apiKey, CancellationToken ct = default)
    {
        var resp = await SendWithRetryAsync("/v1/shops", apiKey, ct);
        var shops = await resp.Content.ReadFromJsonAsync<List<ShopDto>>(ct);
        return shops ?? new List<ShopDto>();
    }

    public async Task<IReadOnlyList<ProductDto>> GetAllProductsAsync(
        long shopId, string apiKey, CancellationToken ct = default)
    {
        var all = new List<ProductDto>();
        var page = 0;

        while (true)
        {
            var result = await GetProductsPageAsync(shopId, apiKey, page, _options.PageSize, ct);
            all.AddRange(result.Products);
            if (result.IsLast)
                break;
            page++;
        }

        return all;
    }

    public async Task<ProductsPage> GetProductsPageAsync(
        long shopId, string apiKey, int page, int size, CancellationToken ct = default)
    {
        var url = $"/v1/product/shop/{shopId}?filter=ALL&size={size}&page={page}";
        using var resp = await SendWithRetryAsync(url, apiKey, ct);
        var body = await resp.Content.ReadFromJsonAsync<ProductShopResponse>(ct)
                   ?? new ProductShopResponse();

        return new ProductsPage
        {
            Products = body.ProductList,
            IsLast = body.ProductList.Count < size,
            RateLimitHeaders = ExtractRateLimitHeaders(resp),
        };
    }

    public async Task<ProbeResult> ProbeAsync(
        ProbeEndpoint endpoint, long shopId, string apiKey, CancellationToken ct = default)
    {
        var url = BuildProbeUrl(endpoint, shopId);
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(req, apiKey);

            // Диагностика: без ретраев, хотим видеть сырую правду.
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            var body = await resp.Content.ReadAsStringAsync(ct);

            return new ProbeResult
            {
                Endpoint = endpoint,
                Url = url,
                Executed = true,
                StatusCode = resp.StatusCode,
                RateLimitHeaders = ExtractRateLimitHeaders(resp),
                BodyPreview = Preview(body),
                ElapsedMs = sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProbeResult
            {
                Endpoint = endpoint,
                Url = url,
                Executed = false,
                Error = ex.Message,
                ElapsedMs = sw.ElapsedMilliseconds,
            };
        }
    }

    private string BuildProbeUrl(ProbeEndpoint endpoint, long shopId) => endpoint switch
    {
        ProbeEndpoint.Products => $"/v1/product/shop/{shopId}?filter=ALL&size=5&page=0",
        // group=false → плоский список позиций; size маленький — диагностика.
        ProbeEndpoint.FinanceOrders => $"/v1/finance/orders?page=0&size=5&group=false&shopIds={shopId}",
        ProbeEndpoint.FinanceExpenses => $"/v1/finance/expenses?page=0&size=5&shopIds={shopId}",
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
    };

    private static string Preview(string body, int max = 600)
    {
        if (string.IsNullOrEmpty(body)) return "(пустое тело)";
        body = body.Trim();
        return body.Length <= max ? body : body[..max] + "…(обрезано)";
    }

    private Dictionary<string, string> ExtractRateLimitHeaders(HttpResponseMessage resp)
    {
        var result = new Dictionary<string, string>();
        foreach (var h in RateLimitHeaders)
        {
            if (resp.Headers.TryGetValues(h, out var values))
                result[h] = string.Join(",", values);
        }
        return result;
    }

    /// <summary>GET с ретраями 429/5xx и логированием rate-limit заголовков.</summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string url, string apiKey, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyAuth(req, apiKey);

            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            LogRateLimit(resp);

            if (resp.IsSuccessStatusCode)
                return resp;

            var retriable = resp.StatusCode == HttpStatusCode.TooManyRequests
                            || (int)resp.StatusCode >= 500;

            if (!retriable || attempt >= _options.MaxRetries)
            {
                resp.EnsureSuccessStatusCode(); // бросит с корректным статусом
                return resp;
            }

            var delay = ComputeDelay(resp, attempt);
            _log.LogWarning(
                "Uzum API {Status} на {Url}, попытка {Attempt}/{Max}, повтор через {Delay}мс",
                (int)resp.StatusCode, url, attempt + 1, _options.MaxRetries, delay.TotalMilliseconds);
            resp.Dispose();
            await Task.Delay(delay, ct);
        }
    }

    private void ApplyAuth(HttpRequestMessage req, string apiKey)
    {
        if (string.Equals(_options.AuthHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            req.Headers.Authorization = string.IsNullOrEmpty(_options.AuthScheme)
                ? AuthenticationHeaderValue.Parse(apiKey)
                : new AuthenticationHeaderValue(_options.AuthScheme, apiKey);
        }
        else
        {
            var value = string.IsNullOrEmpty(_options.AuthScheme)
                ? apiKey
                : $"{_options.AuthScheme} {apiKey}";
            req.Headers.TryAddWithoutValidation(_options.AuthHeaderName, value);
        }
    }

    private TimeSpan ComputeDelay(HttpResponseMessage resp, int attempt)
    {
        // Уважаем Retry-After, если сервер прислал.
        if (resp.Headers.RetryAfter?.Delta is { } delta)
            return delta;

        // Экспоненциальный бэкофф с небольшим джиттером.
        var baseMs = _options.RetryBaseDelayMs * Math.Pow(2, attempt);
        var jitter = Random.Shared.Next(0, _options.RetryBaseDelayMs);
        return TimeSpan.FromMilliseconds(baseMs + jitter);
    }

    private void LogRateLimit(HttpResponseMessage resp)
    {
        if (!_log.IsEnabled(LogLevel.Debug))
            return;

        foreach (var h in RateLimitHeaders)
        {
            if (resp.Headers.TryGetValues(h, out var values))
                _log.LogDebug("rate-limit {Header}={Value}", h, string.Join(",", values));
        }
    }
}
