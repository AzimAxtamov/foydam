using System.Net;
using Microsoft.Extensions.Logging;
using SellerTools.UzumApi;
using SellerTools.UzumApi.Diagnostics;
using SellerTools.UzumApi.Dto;

namespace SellerTools.App.Services;

/// <summary>Категория исхода проверки ключа — маппится в дружелюбный текст.</summary>
public enum KeyCheckOutcome
{
    Ok = 0,
    Unauthorized,   // 401/403 — ключ не тот / нет прав
    NotFound,       // 404 — магазин не найден
    RateLimited,    // 429
    ServerError,    // 5xx
    Network,        // не выполнился (сеть/DNS/таймаут)
    Unknown,        // прочие статусы
}

public record KeyCheckResult(KeyCheckOutcome Outcome, int? StatusCode, string? Detail);

/// <summary>
/// Результат обнаружения магазинов по ключу: исход (для дружелюбного текста)
/// и сам список магазинов при успехе.
/// </summary>
public record ShopDiscoveryResult(
    KeyCheckOutcome Outcome, int? StatusCode, IReadOnlyList<ShopDto> Shops);

/// <summary>
/// Проверяет доступ ключа к API через диагностический Probe и классифицирует
/// результат. Никаких исключений наружу — только KeyCheckResult.
/// </summary>
public class KeyVerification
{
    private readonly IUzumApiClient _api;
    private readonly ILogger<KeyVerification> _log;

    public KeyVerification(IUzumApiClient api, ILogger<KeyVerification> log)
    {
        _api = api;
        _log = log;
    }

    // Разворачивает цепочку InnerException в читаемую строку (тип: сообщение → …).
    // Ключ сюда попасть не может — логируем только типы/сообщения исключений и host.
    private static string DescribeException(Exception ex)
    {
        var parts = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            parts.Add($"{e.GetType().Name}: {e.Message}");
        return string.Join(" → ", parts);
    }

    public async Task<KeyCheckResult> CheckAsync(
        long shopId, string apiKey, CancellationToken ct = default)
    {
        var probe = await _api.ProbeAsync(ProbeEndpoint.Products, shopId, apiKey, ct);

        if (!probe.Executed)
            return new KeyCheckResult(KeyCheckOutcome.Network, null, probe.Error);

        var code = (int)probe.StatusCode!.Value;
        var outcome = probe.StatusCode switch
        {
            HttpStatusCode.OK => KeyCheckOutcome.Ok,
            HttpStatusCode.Unauthorized => KeyCheckOutcome.Unauthorized,
            HttpStatusCode.Forbidden => KeyCheckOutcome.Unauthorized,
            HttpStatusCode.NotFound => KeyCheckOutcome.NotFound,
            HttpStatusCode.TooManyRequests => KeyCheckOutcome.RateLimited,
            _ when code >= 500 => KeyCheckOutcome.ServerError,
            _ when code is >= 200 and < 300 => KeyCheckOutcome.Ok,
            _ => KeyCheckOutcome.Unknown,
        };

        return new KeyCheckResult(outcome, code, null);
    }

    /// <summary>
    /// Проверяет ключ и одновременно узнаёт его магазины через GET /v1/shops.
    /// Это и валидация ключа, и автоопределение shopId — пользователю ничего
    /// вводить не нужно. Исключения не пробрасываются — только результат.
    /// </summary>
    public async Task<ShopDiscoveryResult> DiscoverShopsAsync(
        string apiKey, CancellationToken ct = default)
    {
        try
        {
            var shops = await _api.GetShopsAsync(apiKey, ct);
            return new ShopDiscoveryResult(KeyCheckOutcome.Ok, 200, shops);
        }
        catch (HttpRequestException ex)
        {
            var code = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : (int?)null;
            var outcome = ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized => KeyCheckOutcome.Unauthorized,
                HttpStatusCode.Forbidden => KeyCheckOutcome.Unauthorized,
                HttpStatusCode.NotFound => KeyCheckOutcome.NotFound,
                HttpStatusCode.TooManyRequests => KeyCheckOutcome.RateLimited,
                { } s when (int)s >= 500 => KeyCheckOutcome.ServerError,
                null => KeyCheckOutcome.Network, // соединение не состоялось
                _ => KeyCheckOutcome.Unknown,
            };
            // Детальный лог: без деталей ("Network HTTP null") стоил ночи отладки.
            _log.LogWarning(
                "Проверка ключа: HttpRequestException, host={Host}, httpStatus={Status}, errno={Errno}, цепочка={Chain}",
                Host, code, ex.HttpRequestError, DescribeException(ex));
            return new ShopDiscoveryResult(outcome, code, Array.Empty<ShopDto>());
        }
        catch (TaskCanceledException ex)
        {
            // Таймаут (или отмена). У таймаута InnerException обычно TimeoutException.
            _log.LogWarning(
                "Проверка ключа: таймаут/отмена, host={Host}, цепочка={Chain}",
                Host, DescribeException(ex));
            return new ShopDiscoveryResult(KeyCheckOutcome.Network, null, Array.Empty<ShopDto>());
        }
        catch (Exception ex)
        {
            // Прочее (DNS, сокет, TLS). Логируем полный тип+цепочку+host.
            _log.LogWarning(
                "Проверка ключа: {Type}, host={Host}, цепочка={Chain}",
                ex.GetType().Name, Host, DescribeException(ex));
            return new ShopDiscoveryResult(KeyCheckOutcome.Network, null, Array.Empty<ShopDto>());
        }
    }

    // Хост целевого API (для логов). Ключ/секреты сюда не попадают.
    private static string Host =>
        Uri.TryCreate("https://api-seller.uzum.uz", UriKind.Absolute, out var u) ? u.Host : "api-seller.uzum.uz";
}
