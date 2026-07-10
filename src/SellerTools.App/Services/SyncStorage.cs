using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.Core;
using SellerTools.Core.Entities;
using SellerTools.UzumApi.Dto;

namespace SellerTools.App.Services;

/// <summary>
/// Идемпотентный upsert данных синка в Postgres по API id. Повторный синк тех же
/// записей НЕ создаёт дублей: накладные/возвраты — по PK=Id из API, карточки — по
/// (ShopId, SkuId). Дочерние строки (items) при обновлении родителя пересоздаются
/// (удаляем старые, вставляем новые) — так проще держать их консистентными.
///
/// SkuKeyNorm везде считается ОДНОЙ функцией SkuKey.Normalize (единый ключ матча).
/// </summary>
public class SyncStorage
{
    private readonly AppDbContext _db;
    private readonly ILogger<SyncStorage> _log;

    public SyncStorage(AppDbContext db, ILogger<SyncStorage> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// Upsert накладных с их строками SKU. Возвращает кол-во затронутых накладных.
    /// Даты парсятся толерантно; непарсибельная dateCreated → null + warning
    /// (дельта тогда фолбэчит на полный синк).
    /// </summary>
    public async Task<int> UpsertInvoicesAsync(
        long shopId, IReadOnlyList<InvoiceDto> invoices, CancellationToken ct)
    {
        if (invoices.Count == 0)
            return 0;

        var ids = invoices.Select(i => i.Id).ToList();
        var existing = await _db.Invoices
            .Where(x => ids.Contains(x.Id))
            .Include(x => x.Items)
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var dto in invoices)
        {
            WarnIfDateUnparsed(dto.DateCreatedRaw, dto.DateCreated, "invoice", dto.Id, "dateCreated");

            if (!existing.TryGetValue(dto.Id, out var rec))
            {
                rec = new InvoiceRecord { Id = dto.Id };
                _db.Invoices.Add(rec);
            }
            else if (rec.Items.Count > 0)
            {
                _db.InvoiceItems.RemoveRange(rec.Items); // пересоздаём строки
                rec.Items.Clear();
            }

            rec.ShopId = shopId;
            rec.InvoiceNumber = dto.InvoiceNumber;
            rec.Status = dto.StatusValue;
            rec.DateCreated = dto.DateCreated;
            rec.DateAccepted = dto.DateAccepted;
            rec.TotalAccepted = dto.TotalAccepted;
            rec.TotalToStock = dto.TotalToStock;
            rec.IsTerminal = dto.IsTerminal;
            rec.UpdatedAt = DateTimeOffset.UtcNow;

            foreach (var product in dto.Products)
            foreach (var sku in product.Skus)
            {
                rec.Items.Add(new InvoiceItem
                {
                    SkuTitleRaw = sku.SkuTitle,
                    SkuKeyNorm = SkuKey.Normalize(sku.SkuTitle),
                    QuantityToStock = sku.QuantityToStock,
                    QuantityAccepted = sku.QuantityAccepted,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return invoices.Count;
    }

    /// <summary>Upsert возвратов с их строками SKU. UseAmount фиксируем по legacy-правилу.</summary>
    public async Task<int> UpsertReturnsAsync(
        long shopId, IReadOnlyList<SellerReturnDto> returns, CancellationToken ct)
    {
        if (returns.Count == 0)
            return 0;

        var ids = returns.Select(r => r.Id).ToList();
        var existing = await _db.Returns
            .Where(x => ids.Contains(x.Id))
            .Include(x => x.Items)
            .ToDictionaryAsync(x => x.Id, ct);

        foreach (var dto in returns)
        {
            WarnIfDateUnparsed(dto.DateCreatedRaw, dto.DateCreated, "return", dto.Id, "dateCreated");

            if (!existing.TryGetValue(dto.Id, out var rec))
            {
                rec = new ReturnRecord { Id = dto.Id };
                _db.Returns.Add(rec);
            }
            else if (rec.Items.Count > 0)
            {
                _db.ReturnItems.RemoveRange(rec.Items);
                rec.Items.Clear();
            }

            rec.ShopId = shopId;
            rec.Type = dto.Type;
            rec.Status = dto.Status;
            rec.DateCreated = dto.DateCreated;
            rec.AssembledDate = FlexibleDate.Parse(dto.AssembledDate);
            rec.CompletedDate = dto.CompletedDate;
            rec.CanceledDate = dto.CanceledDate;
            rec.TotalAmount = dto.TotalAmount;
            rec.TotalPackedAmount = dto.TotalPackedAmount;
            rec.UseAmount = dto.UseAmount;      // фиксируем legacy-правило при сохранении
            rec.IsTerminal = dto.IsTerminal;
            rec.UpdatedAt = DateTimeOffset.UtcNow;

            foreach (var item in dto.ReturnItems)
            {
                rec.Items.Add(new ReturnItem
                {
                    SkuId = item.SkuId,
                    SkuTitleRaw = item.SkuTitle,
                    SkuKeyNorm = SkuKey.Normalize(item.SkuTitle),
                    Amount = item.Amount,
                    PackedAmount = item.PackedAmount,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return returns.Count;
    }

    /// <summary>
    /// Upsert последнего среза карточек SKU по (ShopId, SkuId). SkuKeyNorm — от
    /// skuFullTitle (полный код лежит там, см. SkuKey). Возвращает кол-во SKU.
    /// </summary>
    public async Task<int> UpsertSkusAsync(
        long shopId, IReadOnlyList<SkuDto> skus, IReadOnlyDictionary<long, long> productIdBySku,
        CancellationToken ct)
    {
        if (skus.Count == 0)
            return 0;

        var skuIds = skus.Select(s => s.SkuId).ToList();
        var existing = await _db.Skus
            .Where(x => x.ShopId == shopId && skuIds.Contains(x.SkuId))
            .ToDictionaryAsync(x => x.SkuId, ct);

        foreach (var s in skus)
        {
            if (!existing.TryGetValue(s.SkuId, out var rec))
            {
                rec = new SkuRecord { ShopId = shopId, SkuId = s.SkuId };
                _db.Skus.Add(rec);
            }

            rec.SkuTitle = s.SkuTitle;
            rec.SkuFullTitle = s.SkuFullTitle;
            rec.SkuKeyNorm = SkuKey.Normalize(s.SkuFullTitle);
            rec.ProductId = productIdBySku.GetValueOrDefault(s.SkuId, 0);
            rec.ProductTitle = s.ProductTitle;
            rec.QuantitySold = s.QuantitySold;
            rec.QuantityReturned = s.QuantityReturned;
            rec.QuantityDefected = s.QuantityDefected;
            rec.QuantityActive = s.QuantityActive;
            rec.Ikpu = s.Ikpu;
            rec.Article = s.Article;
            rec.SellerItemCode = s.SellerItemCode;
            rec.Barcode = s.Barcode;
            rec.Price = s.Price;
            rec.PurchasePriceDirty = s.PurchasePrice;
            rec.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return skus.Count;
    }

    // Если сырое значение даты было, а разобрать не удалось — логируем (иерархия
    // доверия «живой JSON > спека»: непарсибельное = warning + null, не падение).
    private void WarnIfDateUnparsed(
        System.Text.Json.JsonElement? raw, DateTimeOffset? parsed, string kind, long id, string field)
    {
        if (FlexibleDate.HasValue(raw) && parsed is null)
            _log.LogWarning(
                "Не удалось разобрать дату {Field} у {Kind} id={Id} (сырое значение: {Raw}) — сохранено null.",
                field, kind, id, raw?.ToString());
    }
}
