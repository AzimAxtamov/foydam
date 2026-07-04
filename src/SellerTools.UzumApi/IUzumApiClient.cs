using SellerTools.UzumApi.Diagnostics;
using SellerTools.UzumApi.Dto;

namespace SellerTools.UzumApi;

/// <summary>
/// Клиент Uzum Seller API. Реализации: RealUzumApiClient, MockUzumApiClient.
/// Ключ передаётся в вызов (per-shop), в самом клиенте не хранится.
/// </summary>
public interface IUzumApiClient
{
    /// <summary>
    /// Все товары магазина (обходит все страницы). filter=ALL.
    /// </summary>
    Task<IReadOnlyList<ProductDto>> GetAllProductsAsync(
        long shopId,
        string apiKey,
        CancellationToken ct = default);

    /// <summary>
    /// Диагностика: одиночный сырой GET к эндпоинту. Возвращает статус,
    /// rate-limit заголовки и начало тела. Не ретраит, не бросает исключения —
    /// сетевые ошибки кладёт в ProbeResult.Error. Нужно для проверки живым ключом
    /// (имя auth-заголовка, доступ к finance/*, формат дат/денег и т.п.).
    /// </summary>
    Task<ProbeResult> ProbeAsync(
        ProbeEndpoint endpoint,
        long shopId,
        string apiKey,
        CancellationToken ct = default);
}
