using System.Text.RegularExpressions;

namespace SellerTools.Core;

/// <summary>
/// Единый нормализатор ключа матча SKU между источниками. Полный код SKU
/// (напр. «GALERIA-193194-11193») лежит в РАЗНЫХ полях:
///   - в карточке товара — в skuFullTitle (skuTitle там короткий: «11193»/«СЕРЫЙ»);
///   - в накладных /v1/invoice и возвратах /v1/return — в их skuTitle.
/// Чтобы join сматчился, обе стороны нормализуем ОДНОЙ функцией и по её результату
/// пишем sku_key_norm в БД. id/skuId в накладных/возвратах — внутренние, не матчим.
/// </summary>
public static partial class SkuKey
{
    /// <summary>
    /// trim + верхний регистр + схлопывание повторных пробелов (защита от «грязных»
    /// строк Uzum). Null/пусто → пустая строка (не сматчится).
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var collapsed = Whitespace().Replace(raw.Trim(), " ");
        return collapsed.ToUpperInvariant();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
