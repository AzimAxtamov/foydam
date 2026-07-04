using SellerTools.UzumApi;
using SellerTools.UzumApi.Diagnostics;

namespace SellerTools.App.Diagnostics;

/// <summary>
/// Консольная диагностика: принимает ключ и shopId, прогоняет все три GET-
/// эндпоинта и печатает статус, rate-limit заголовки и начало тела ответа.
/// Инструмент проверки живым ключом (В2): подтвердить auth-заголовок,
/// доступ к finance/*, форматы дат/денег.
/// </summary>
public class DiagnosticRunner
{
    private readonly IUzumApiClient _api;

    public DiagnosticRunner(IUzumApiClient api) => _api = api;

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

        Console.WriteLine(line);
        Console.WriteLine("  Подсказки:");
        Console.WriteLine("  • 200 на /product → auth-заголовок и схема верны.");
        Console.WriteLine("  • 401/403 → проверьте UzumApi:AuthHeaderName / AuthScheme.");
        Console.WriteLine("  • 200 на finance/* → read-only ключ отдаёт финансы.");
        Console.WriteLine("  • Сверьте единицы дат/денег в теле ответа (чек-лист «Неизвестное»).");
        Console.WriteLine(line);
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
