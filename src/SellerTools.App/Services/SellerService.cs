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
    /// Модель «один магазин на селлера»: любые ДРУГИЕ магазины этого селлера
    /// отвязываются (со снапшотами и курсорами), чтобы новый выбор ЗАМЕНЯЛ старый.
    /// Иначе после повторного /start бот брал бы shops[0] = первый (старый) магазин.
    /// Тот же shopId — просто обновление ключа/имени.
    /// </summary>
    public async Task BindShopAsync(
        long sellerId, long shopId, string apiKeyPlain,
        string? title = null, DateTimeOffset? keyExpiresAt = null, CancellationToken ct = default)
    {
        await EnsureSellerAsync(sellerId, ct);

        // Убираем прежние магазины этого селлера, кроме выбранного (чтобы не «залипал»
        // старый выбор). Заодно чистим их снапшоты и курсоры — FK-каскада нет.
        var oldShops = await _db.Shops
            .Where(s => s.SellerId == sellerId && s.Id != shopId)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (oldShops.Count > 0)
        {
            var snaps = _db.SkuDailySnapshots.Where(x => oldShops.Contains(x.ShopId));
            _db.SkuDailySnapshots.RemoveRange(snaps);
            var cursors = _db.SyncCursors.Where(x => oldShops.Contains(x.ShopId));
            _db.SyncCursors.RemoveRange(cursors);
            var shopsToRemove = _db.Shops.Where(s => oldShops.Contains(s.Id));
            _db.Shops.RemoveRange(shopsToRemove);
        }

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
