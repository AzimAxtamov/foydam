namespace SellerTools.Core.Entities;

/// <summary>
/// Факт тапа по кнопке «Поделиться результатом». Одна строка на каждый тап
/// (повторные тапы = новые строки, это ок). Из этой таблицы + <see cref="Seller.RefCode"/>
/// / <see cref="Seller.ReferredBySellerId"/> собирается воронка шеринга запросами
/// (отдельные счётчики не храним). См. <c>ReferralService.GetShareStatsAsync</c>.
/// </summary>
public class ShareEvent
{
    public long Id { get; set; }

    /// <summary>Кто нажал «Поделиться» (telegram user id = <see cref="Seller.Id"/>).</summary>
    public long SellerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
