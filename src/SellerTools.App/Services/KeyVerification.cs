using System.Net;
using SellerTools.UzumApi;
using SellerTools.UzumApi.Diagnostics;

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
/// Проверяет доступ ключа к API через диагностический Probe и классифицирует
/// результат. Никаких исключений наружу — только KeyCheckResult.
/// </summary>
public class KeyVerification
{
    private readonly IUzumApiClient _api;

    public KeyVerification(IUzumApiClient api) => _api = api;

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
}
