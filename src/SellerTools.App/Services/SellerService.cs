using Microsoft.EntityFrameworkCore;
using SellerTools.App.Data;
using SellerTools.Core.Entities;

namespace SellerTools.App.Services;

/// <summary>
/// Работа с селлером и его магазинами: язык, привязка ключа.
/// Ключ шифруется перед сохранением; plaintext не хранится.
/// </summary>
public class SellerService
{
    private readonly AppDbContext _db;
    private readonly ICryptoService _crypto;

    public SellerService(AppDbContext db, ICryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    /// <summary>Гарантирует наличие селлера; возвращает его.</summary>
    public async Task<Seller> EnsureSellerAsync(long sellerId, CancellationToken ct = default)
    {
        var seller = await _db.Sellers.FirstOrDefaultAsync(s => s.Id == sellerId, ct);
        if (seller is null)
        {
            seller = new Seller { Id = sellerId };
            _db.Sellers.Add(seller);
            await _db.SaveChangesAsync(ct);
        }
        return seller;
    }

    public async Task<string?> GetLanguageAsync(long sellerId, CancellationToken ct = default)
        => (await _db.Sellers.FirstOrDefaultAsync(s => s.Id == sellerId, ct))?.Language;

    /// <summary>Сохраняет выбранный язык селлера.</summary>
    public async Task SetLanguageAsync(long sellerId, string lang, CancellationToken ct = default)
    {
        var seller = await EnsureSellerAsync(sellerId, ct);
        seller.Language = lang;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Привязывает магазин к селлеру: сохраняет shopId и зашифрованный ключ.
    /// Если магазин уже есть — обновляет ключ и владельца.
    /// </summary>
    public async Task BindShopAsync(
        long sellerId, long shopId, string apiKeyPlain,
        string? title = null, DateTimeOffset? keyExpiresAt = null, CancellationToken ct = default)
    {
        await EnsureSellerAsync(sellerId, ct);

        var encrypted = _crypto.Encrypt(apiKeyPlain);

        var shop = await _db.Shops.FirstOrDefaultAsync(s => s.Id == shopId, ct);
        if (shop is null)
        {
            shop = new Shop { Id = shopId };
            _db.Shops.Add(shop);
        }

        shop.SellerId = sellerId;
        shop.ApiKeyEncrypted = encrypted;
        shop.KeyExpiresAt = keyExpiresAt;
        if (!string.IsNullOrWhiteSpace(title))
            shop.Title = title;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<Shop>> GetShopsAsync(long sellerId, CancellationToken ct = default)
        => await _db.Shops.Where(s => s.SellerId == sellerId).ToListAsync(ct);
}
