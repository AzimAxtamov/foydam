using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.Core.Entities;

namespace SellerTools.App.Services;

/// <summary>
/// Реферальная механика шеринга (V1). Отвечает за:
/// — ленивую генерацию реф-кода селлера (base36, крипто-рандом, ретрай на коллизию);
/// — запись «кто привёл» (first touch, ровно один раз, self-ref игнор);
/// — лог тапов «Поделиться» (share_events);
/// — агрегаты воронки для админ-статистики.
/// Тексты и кнопки здесь НЕ формирует — только данные.
/// </summary>
public class ReferralService
{
    // base36 без легко путаемых символов не делаем — код в дип-линке, не диктуется вслух.
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
    private const int CodeLength = 8;
    private const int MaxCollisionRetries = 5;

    private readonly AppDbContext _db;
    private readonly ILogger<ReferralService> _log;

    public ReferralService(AppDbContext db, ILogger<ReferralService> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Возвращает реф-код селлера, создавая его лениво при первом обращении.
    /// Гарантирует наличие селлера. Коллизии кода → ретрай.
    /// </summary>
    public async Task<string> GetOrCreateRefCodeAsync(long sellerId, CancellationToken ct = default)
    {
        var seller = await _db.Sellers.FirstOrDefaultAsync(s => s.Id == sellerId, ct);
        if (seller is null)
        {
            seller = new Seller { Id = sellerId };
            _db.Sellers.Add(seller);
        }

        if (!string.IsNullOrEmpty(seller.RefCode))
            return seller.RefCode;

        for (var attempt = 0; attempt < MaxCollisionRetries; attempt++)
        {
            var code = GenerateCode();
            var taken = await _db.Sellers.AnyAsync(s => s.RefCode == code, ct);
            if (taken) continue;

            seller.RefCode = code;
            try
            {
                await _db.SaveChangesAsync(ct);
                return code;
            }
            catch (DbUpdateException) // гонка на уникальном индексе — пробуем другой код
            {
                _db.Entry(seller).Property(x => x.RefCode).CurrentValue = null;
            }
        }

        throw new InvalidOperationException(
            $"Не удалось сгенерировать уникальный реф-код для селлера {sellerId} за {MaxCollisionRetries} попыток.");
    }

    /// <summary>Пишет тап «Поделиться» в share_events (повторные тапы = новые строки).</summary>
    public async Task LogShareAsync(long sellerId, CancellationToken ct = default)
    {
        _db.ShareEvents.Add(new ShareEvent { SellerId = sellerId });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Приход по дип-линку <c>ref_{code}</c>: гарантирует селлера и, если у него ещё нет
    /// реферера, пишет его РОВНО ОДИН РАЗ (first touch). Self-ref и неизвестный код — игнор.
    /// Возвращает true, если реферер был записан (для лога/статистики).
    /// </summary>
    public async Task<bool> TryRecordReferralAsync(long newSellerId, string refCode, CancellationToken ct = default)
    {
        var seller = await _db.Sellers.FirstOrDefaultAsync(s => s.Id == newSellerId, ct);
        if (seller is null)
        {
            seller = new Seller { Id = newSellerId };
            _db.Sellers.Add(seller);
        }

        // First touch: уже есть реферер — не трогаем.
        if (seller.ReferredBySellerId is not null)
        {
            await _db.SaveChangesAsync(ct); // сохранить, если селлер только что создан
            return false;
        }

        var referrer = await _db.Sellers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.RefCode == refCode, ct);

        // Неизвестный/битый код или self-ref — молча оставляем без реферера.
        if (referrer is null || referrer.Id == newSellerId)
        {
            await _db.SaveChangesAsync(ct);
            return false;
        }

        seller.ReferredBySellerId = referrer.Id;
        await _db.SaveChangesAsync(ct);
        _log.LogInformation("Реферал: селлер {New} привёден селлером {Ref} (код {Code})",
            newSellerId, referrer.Id, refCode);
        return true;
    }

    /// <summary>Числа воронки шеринга за последние <paramref name="days"/> дней.</summary>
    public async Task<ShareStats> GetShareStatsAsync(int days, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-days);

        var shareTaps = await _db.ShareEvents.CountAsync(e => e.CreatedAt >= since, ct);
        var sharers = await _db.ShareEvents
            .Where(e => e.CreatedAt >= since)
            .Select(e => e.SellerId).Distinct().CountAsync(ct);

        // Приходы по рефералке: селлеры с непустым referred_by, созданные за окно.
        var referredArrivals = await _db.Sellers
            .CountAsync(s => s.ReferredBySellerId != null && s.CreatedAt >= since, ct);

        // Из них подключили магазин (есть хоть один Shop).
        var referredActivated = await _db.Sellers
            .CountAsync(s => s.ReferredBySellerId != null && s.CreatedAt >= since && s.Shops.Any(), ct);

        return new ShareStats(days, shareTaps, sharers, referredArrivals, referredActivated);
    }

    private static string GenerateCode()
    {
        Span<byte> bytes = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }
}

/// <summary>Агрегаты воронки шеринга за период (для админ-статистики).</summary>
public readonly record struct ShareStats(
    int Days, int ShareTaps, int Sharers, int ReferredArrivals, int ReferredActivated);
