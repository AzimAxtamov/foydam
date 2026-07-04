using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    public async Task<IReadOnlyList<ProductDto>> GetAllProductsAsync(
        long shopId, string apiKey, CancellationToken ct = default)
    {
        var all = new List<ProductDto>();
        var page = 0;

        while (true)
        {
            var url = $"/v1/product/shop/{shopId}?filter=ALL&size={_options.PageSize}&page={page}";
            var resp = await SendWithRetryAsync(url, apiKey, ct);
            var body = await resp.Content.ReadFromJsonAsync<ProductShopResponse>(ct)
                       ?? new ProductShopResponse();

            all.AddRange(body.ProductList);

            // Пагинация: пока страница полная — идём дальше.
            if (body.ProductList.Count < _options.PageSize)
                break;
            page++;
        }

        return all;
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
