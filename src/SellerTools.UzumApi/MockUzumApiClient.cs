using System.Net;
using SellerTools.UzumApi.Diagnostics;
using SellerTools.UzumApi.Dto;

namespace SellerTools.UzumApi;

/// <summary>
/// Детерминированный мок (фиксированный seed). ~25 SKU с узбекско-русскими
/// названиями; у нескольких заполнены quantityMissing/Defected и у части
/// пустой закуп (как в реальной жизни). Ключ игнорируется.
/// </summary>
public class MockUzumApiClient : IUzumApiClient
{
    private const int Seed = 20260704; // фиксированный seed → воспроизводимость

    // Пары «товар / вариант» на смеси русского и узбекского.
    private static readonly (string Product, string Sku)[] Catalog =
    {
        ("Чойнак «Пахта гул»", "0.8 л, кўк"),
        ("Термос Sardor", "500 мл, стальной"),
        ("Кружка «Самарканд»", "керамика, синяя"),
        ("Кроссовки Yugurish", "42, оқ"),
        ("Футболка Paxta 100%", "L, белая"),
        ("Джинсы Shahar", "32/32, тёмно-синие"),
        ("Куртка Qish", "XL, чёрная"),
        ("Носки Ippak (5 пар)", "41-45, ассорти"),
        ("Плов-казан «Ошхона»", "6 л, чугун"),
        ("Нож «Пичоқ» Chust", "сталь, ручная работа"),
        ("Пиала «Риштон» (6 шт)", "220 мл, роспись"),
        ("Сюзане настенное", "1.2×0.8 м, шёлк"),
        ("Тюбетейка «Дўппи»", "58, чустская"),
        ("Халат «Чапан»", "L, полосатый"),
        ("Ковёр «Гилам»", "2×3 м, бордовый"),
        ("Сумка «Хуржун»", "кожа, коричневая"),
        ("Powerbank Quvvat", "20000 мАч"),
        ("Наушники Eshit Pro", "TWS, чёрные"),
        ("Зарядка Tez USB-C", "65 Вт, GaN"),
        ("Лампа «Чироқ» LED", "12 Вт, тёплая"),
        ("Мёд «Асал» горный", "1 кг, банка"),
        ("Курага «Ўрик» (500 г)", "премиум"),
        ("Изюм «Майиз» кишмиш", "1 кг"),
        ("Специи «Зира» набор", "5 видов"),
        ("Чай «Кўк чой» зелёный", "250 г, листовой"),
    };

    // Мок-магазин(ы) ключа. Один магазин — самый частый кейс.
    public Task<IReadOnlyList<ShopDto>> GetShopsAsync(
        string apiKey, CancellationToken ct = default)
    {
        var shops = new List<ShopDto>
        {
            new() { Id = 777001, Name = "Демо-магазин" },
        };
        return Task.FromResult<IReadOnlyList<ShopDto>>(shops);
    }

    public Task<IReadOnlyList<ProductDto>> GetAllProductsAsync(
        long shopId, string apiKey, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProductDto>>(BuildAllProducts());

    // Синтетические rate-limit заголовки для страниц (как у реального API).
    private static Dictionary<string, string> MockRateLimitHeaders(long remainingPerDay) => new()
    {
        ["x-ratelimit-remaining"] = "999",
        ["x-ratelimit-replenish-rate"] = "10",
        ["x-ratelimit-burst-capacity"] = "20",
        ["x-ratelimit-requested-tokens"] = "1",
        ["x-ratelimit-limit-per-day"] = "100000",
        ["x-ratelimit-remaining-per-day"] = remainingPerDay.ToString(),
    };

    public Task<ProductsPage> GetProductsPageAsync(
        long shopId, string apiKey, int page, int size, CancellationToken ct = default)
    {
        var all = BuildAllProducts();
        var slice = all.Skip(page * size).Take(size).ToList();
        return Task.FromResult(new ProductsPage
        {
            Products = slice,
            IsLast = slice.Count < size,
            RateLimitHeaders = MockRateLimitHeaders(99500 - page),
        });
    }

    private static List<ProductDto> BuildAllProducts()
    {
        var rnd = new Random(Seed);
        var products = new List<ProductDto>(Catalog.Length);

        // Индексы SKU, у которых будут потеряшки/брак: 6 позиций.
        var lossIndexes = new HashSet<int> { 1, 4, 8, 11, 15, 21 };

        for (var i = 0; i < Catalog.Length; i++)
        {
            var (productTitle, skuTitle) = Catalog[i];
            var skuId = 100000 + i;
            var productId = 900000 + i;

            var price = (rnd.Next(15, 400)) * 1000L;      // 15 000 – 400 000
            // У ~половины закуп не заполнен (0) — реалистично.
            var hasPurchase = rnd.Next(0, 2) == 1;
            var purchase = hasPurchase ? price * rnd.Next(40, 75) / 100 : 0L;

            var active = rnd.Next(0, 120);
            var sold = rnd.Next(0, 500);
            var returned = rnd.Next(0, sold / 5 + 1);

            var missing = 0;
            var defected = 0;
            if (lossIndexes.Contains(i))
            {
                missing = rnd.Next(1, 6);
                defected = rnd.Next(0, 4);
            }

            var sku = new SkuDto
            {
                SkuId = skuId,
                SkuTitle = skuTitle,
                SkuFullTitle = $"{productTitle} — {skuTitle}",
                ProductTitle = productTitle,
                QuantityActive = active,
                QuantitySold = sold,
                QuantityReturned = returned,
                QuantityMissing = missing,
                QuantityDefected = defected,
                Price = price,
                PurchasePrice = purchase,
                Commission = price * rnd.Next(8, 18) / 100,
                PaidStorageAmount = rnd.Next(0, 5) * 1000L,
                AvgDSales = Math.Round(rnd.NextDouble() * 5, 2),
                Ikpu = GenerateIkpu(rnd),
                SellerItemCode = $"ART-{1000 + i}",
                Article = $"ART-{1000 + i}",
                Barcode = 4780000000000 + i,
                Archived = false,
                Blocked = false,
            };

            products.Add(new ProductDto
            {
                ProductId = productId,
                Title = productTitle,
                Category = "Тест",
                SkuList = new List<SkuDto> { sku },
                QuantityActive = active,
                QuantityFbs = 0,
            });
        }

        return products;
    }

    public Task<ProbeResult> ProbeAsync(
        ProbeEndpoint endpoint, long shopId, string apiKey, CancellationToken ct = default)
    {
        // Синтетические rate-limit заголовки — чтобы диагностика работала и в Mock.
        var headers = new Dictionary<string, string>
        {
            ["x-ratelimit-remaining"] = "999",
            ["x-ratelimit-replenish-rate"] = "10",
            ["x-ratelimit-burst-capacity"] = "20",
            ["x-ratelimit-requested-tokens"] = "1",
            ["x-ratelimit-limit-per-day"] = "100000",
            ["x-ratelimit-remaining-per-day"] = "99500",
        };

        var (url, body) = endpoint switch
        {
            ProbeEndpoint.Products =>
                ($"/v1/product/shop/{shopId}?filter=ALL&size=5&page=0",
                 "{\"productList\":[{\"productId\":900000,\"title\":\"Чойнак «Пахта гул»\",\"skuList\":[{\"skuId\":100000,\"quantityMissing\":0,\"price\":45000,\"ikpu\":\"12345678901234567\"}]}],\"totalProductsAmount\":25}"),
            ProbeEndpoint.FinanceOrders =>
                ($"/v1/finance/orders?page=0&size=5&group=false&shopIds={shopId}",
                 "{\"orderItems\":[{\"id\":1,\"status\":\"TO_WITHDRAW\",\"orderId\":5001,\"sellerPrice\":45000,\"amount\":1,\"commission\":4500}],\"totalElements\":128}"),
            ProbeEndpoint.FinanceExpenses =>
                ($"/v1/finance/expenses?page=0&size=5&shopIds={shopId}",
                 "{\"payload\":{\"payments\":[{\"id\":1,\"source\":\"LOGISTICS\",\"paymentPrice\":12000,\"type\":\"OUTCOME\",\"status\":\"CREATED\"}]},\"errors\":[]}"),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
        };

        return Task.FromResult(new ProbeResult
        {
            Endpoint = endpoint,
            Url = url,
            Executed = true,
            StatusCode = HttpStatusCode.OK,
            RateLimitHeaders = headers,
            BodyPreview = body,
            ElapsedMs = 3,
        });
    }

    // ИКПУ — 17-значный код (формат нужен для ЭТТН).
    private static string GenerateIkpu(Random rnd)
    {
        Span<char> digits = stackalloc char[17];
        for (var i = 0; i < digits.Length; i++)
            digits[i] = (char)('0' + rnd.Next(0, 10));
        return new string(digits);
    }
}
