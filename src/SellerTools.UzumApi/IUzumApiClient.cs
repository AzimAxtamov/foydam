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
}
