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
    /// Магазины, доступные ключу (GET /v1/shops). Позволяет узнать shopId по ключу,
    /// не спрашивая пользователя. Бросает при не-2xx (обработать выше).
    /// </summary>
    Task<IReadOnlyList<ShopDto>> GetShopsAsync(
        string apiKey,
        CancellationToken ct = default);

    /// <summary>
    /// Все товары магазина (обходит все страницы). filter=ALL.
    /// </summary>
    Task<IReadOnlyList<ProductDto>> GetAllProductsAsync(
        long shopId,
        string apiKey,
        CancellationToken ct = default);

    /// <summary>
    /// Одна страница товаров + rate-limit заголовки. Для синка: даёт синку
    /// решать между страницами, паузиться ли по остатку лимита.
    /// </summary>
    Task<ProductsPage> GetProductsPageAsync(
        long shopId,
        string apiKey,
        int page,
        int size,
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
