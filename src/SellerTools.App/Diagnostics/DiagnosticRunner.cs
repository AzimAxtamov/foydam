using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using SellerTools.UzumApi;
using SellerTools.UzumApi.Diagnostics;

namespace SellerTools.App.Diagnostics;

/// <summary>
/// Консольная диагностика: принимает ключ и shopId, прогоняет все три GET-
/// эндпоинта и печатает статус, rate-limit заголовки и начало тела ответа.
/// Инструмент проверки живым ключом (В2): подтвердить auth-заголовок,
/// доступ к finance/*, форматы дат/денег. Плюс проверка канонического
/// базового пути (открытый вопрос про Server URL).
/// </summary>
public class DiagnosticRunner
{
    private readonly IUzumApiClient _api;
    private readonly UzumApiOptions _options;

    // Два кандидата на базовый путь (открытый вопрос про Server URL из спеки).
    private static readonly (string Label, string BaseUrl)[] BasePaths =
    {
        ("без префикса", "https://api-seller.uzum.uz/"),
        ("с /api/seller-openapi/", "https://api-seller.uzum.uz/api/seller-openapi/"),
    };

    public DiagnosticRunner(IUzumApiClient api, IOptions<UzumApiOptions> options)
    {
        _api = api;
        _options = options.Value;
    }

    public async Task RunAsync(long shopId, string apiKey, CancellationToken ct = default)
    {
        var line = new string('═', 72);
        Console.WriteLine();
        Console.WriteLine(line);
        Console.WriteLine("  ДИАГНОСТИКА UZUM SELLER API");
        Console.WriteLine($"  shopId: {shopId}   ключ: {MaskKey(apiKey)}");
        Console.WriteLine(line);

        foreach (var endpoint in new[]
                 {
                     ProbeEndpoint.Products,
                     ProbeEndpoint.FinanceOrders,
                     ProbeEndpoint.FinanceExpenses,
                 })
        {
            var r = await _api.ProbeAsync(endpoint, shopId, apiKey, ct);
            PrintProbe(r);
        }

        await CheckBasePathsAsync(shopId, apiKey, ct);

        Console.WriteLine(line);
        Console.WriteLine("  Подсказки:");
        Console.WriteLine("  • 200 на /product → auth-заголовок и схема верны.");
        Console.WriteLine("  • 401/403 → проверьте UzumApi:AuthHeaderName / AuthScheme.");
        Console.WriteLine("  • 200 на finance/* → read-only ключ отдаёт финансы.");
        Console.WriteLine("  • Сверьте единицы дат/денег в теле ответа (чек-лист «Неизвестное»).");
        Console.WriteLine(line);
    }

    /// <summary>
    /// Прогоняет GET /v1/shops через оба базовых пути и печатает вердикт,
    /// какой канонический (открытый вопрос про Server URL из спеки).
    /// </summary>
    private async Task CheckBasePathsAsync(long shopId, string apiKey, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine("▶ Проверка базового пути (GET /v1/shops через оба варианта)");

        var results = new List<(string Label, string BaseUrl, int? Status, string Note)>();
        foreach (var (label, baseUrl) in BasePaths)
        {
            using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var req = new HttpRequestMessage(HttpMethod.Get, "v1/shops");
            ApplyAuth(req, apiKey);
            try
            {
                using var resp = await http.SendAsync(req, ct);
                var code = (int)resp.StatusCode;
                var note = code switch
                {
                    >= 200 and < 300 => "OK — путь рабочий",
                    401 or 403 => "дошло до авторизации (путь есть, ключ/права)",
                    404 => "НЕ найдено — путь неверный",
                    _ => $"HTTP {code}",
                };
                results.Add((label, baseUrl, code, note));
                Console.WriteLine($"  {baseUrl}v1/shops → HTTP {code} — {note}");
            }
            catch (Exception ex)
            {
                results.Add((label, baseUrl, null, "ошибка сети: " + ex.Message));
                Console.WriteLine($"  {baseUrl}v1/shops → сеть: {ex.Message}");
            }
        }

        Console.WriteLine("  Вердикт: " + BasePathVerdict(results));
    }

    private static string BasePathVerdict(
        List<(string Label, string BaseUrl, int? Status, string Note)> results)
    {
        // 2xx = точно канонический. Если 2xx нет, 404 отсекает путь; не-404 (401/403)
        // означает «путь есть, но ключ невалиден» — различить пути так нельзя.
        var ok = results.Where(r => r.Status is >= 200 and < 300).ToList();
        if (ok.Count == 1)
            return $"канонический — {ok[0].BaseUrl} ({ok[0].Label}).";
        if (ok.Count > 1)
            return "оба пути отдают 2xx — работают оба (шлюз принимает и то, и другое).";

        var notFound = results.Where(r => r.Status == 404).Select(r => r.Label).ToList();
        var reached = results.Where(r => r.Status is not null && r.Status != 404).Select(r => r.Label).ToList();
        if (notFound.Count == 1 && reached.Count == 1)
            return $"путь «{reached[0]}» рабочий (не 404), «{notFound[0]}» → 404 (неверный). " +
                   "Для 2xx нужен валидный ключ.";
        if (reached.Count == 2)
            return "оба пути дошли до авторизации (не 404) — различить без валидного ключа нельзя; " +
                   "по спеке канонический — с /api/seller-openapi/.";
        return "неоднозначно — повторите с валидным ключом.";
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
            var value = string.IsNullOrEmpty(_options.AuthScheme) ? apiKey : $"{_options.AuthScheme} {apiKey}";
            req.Headers.TryAddWithoutValidation(_options.AuthHeaderName, value);
        }
    }

    private static void PrintProbe(ProbeResult r)
    {
        Console.WriteLine();
        Console.WriteLine($"▶ {EndpointName(r.Endpoint)}");
        Console.WriteLine($"  GET {r.Url}");

        if (!r.Executed)
        {
            Console.WriteLine($"  ❌ ЗАПРОС НЕ ВЫПОЛНЕН ({r.ElapsedMs} мс): {r.Error}");
            return;
        }

        var mark = r.IsSuccess ? "✅" : "⚠️";
        Console.WriteLine($"  {mark} HTTP {(int)r.StatusCode!.Value} {r.StatusCode}   ({r.ElapsedMs} мс)");

        if (r.RateLimitHeaders.Count > 0)
        {
            Console.WriteLine("  rate-limit:");
            foreach (var (k, v) in r.RateLimitHeaders)
                Console.WriteLine($"    {k}: {v}");
        }
        else
        {
            Console.WriteLine("  rate-limit: (заголовков нет в ответе)");
        }

        Console.WriteLine("  тело (начало):");
        foreach (var bodyLine in WrapBody(r.BodyPreview))
            Console.WriteLine($"    {bodyLine}");
    }

    private static IEnumerable<string> WrapBody(string body, int width = 100)
    {
        body = body.Replace("\r", "").Replace("\n", " ⏎ ");
        for (var i = 0; i < body.Length; i += width)
            yield return body.Substring(i, Math.Min(width, body.Length - i));
    }

    private static string EndpointName(ProbeEndpoint e) => e switch
    {
        ProbeEndpoint.Products => "Товары (/v1/product/shop/{shopId})",
        ProbeEndpoint.FinanceOrders => "Заказы (/v1/finance/orders)",
        ProbeEndpoint.FinanceExpenses => "Расходы (/v1/finance/expenses)",
        _ => e.ToString(),
    };

    /// <summary>Ключ в консоль не печатаем целиком.</summary>
    private static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "(пусто)";
        if (key.Length <= 8) return new string('*', key.Length);
        return key[..4] + new string('*', key.Length - 8) + key[^4..];
    }
}
