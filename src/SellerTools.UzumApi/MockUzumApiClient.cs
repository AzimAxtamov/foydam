using System.Net;
using System.Text.Json;
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

    // Непустая дата сборки для возвратов (JsonElement — как у живого API любого типа).
    private static readonly JsonElement MockAssembledDate =
        JsonDocument.Parse("\"2026-06-01T10:00:00Z\"").RootElement.Clone();

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
                PriceRaw = price,
                PurchasePriceRaw = purchase,
                Commission = rnd.Next(8, 18) + 0.5, // процент, double
                PaidStorageAmount = rnd.Next(0, 5) * 1000L,
                AvgDSales = Math.Round(rnd.NextDouble() * 5, 2),
                Ikpu = GenerateIkpu(rnd),
                SellerItemCode = $"ART-{1000 + i}",
                Article = $"ART-{1000 + i}",
                BarcodeRaw = 4780000000000 + i,
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

    // Балансовая реконсиляция (Mock): накладные и возвраты выдаём так, чтобы баланс
    // lost = accepted − removed − sold − defected − stock сошёлся с построенными
    // товарами. План строим ИЗ САМИХ товаров (не реплицируя rnd повторно) —
    // так accepted гарантированно = целевой lost + removed + sold + defected + stock.
    //
    // dateCreated проставляем УБЫВАЮЩИМ (новые сверху) — как отдаёт живой API; на этом
    // порядке дельта-синк проверяет page0/page1-guard (восходящий порядок → ошибка).
    public Task<IReadOnlyList<InvoiceDto>> GetAllInvoicesAsync(
        long shopId, string apiKey, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InvoiceDto>>(BuildAllInvoices());

    public Task<IReadOnlyList<SellerReturnDto>> GetAllReturnsAsync(
        long shopId, string apiKey, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SellerReturnDto>>(BuildAllReturns());

    public Task<ApiPage<InvoiceDto>> GetInvoicesPageAsync(
        long shopId, string apiKey, int page, int size = 50, CancellationToken ct = default)
        => Task.FromResult(Paginate(BuildAllInvoices(), page, size));

    public Task<ApiPage<SellerReturnDto>> GetReturnsPageAsync(
        long shopId, string apiKey, int page, int size = 50, CancellationToken ct = default)
        => Task.FromResult(Paginate(BuildAllReturns(), page, size));

    private static ApiPage<T> Paginate<T>(IReadOnlyList<T> all, int page, int size)
    {
        var slice = all.Skip(page * size).Take(size).ToList();
        return new ApiPage<T>
        {
            Items = slice,
            IsLast = slice.Count < size,
            RateLimitHeaders = MockRateLimitHeaders(99500 - page),
        };
    }

    // База времени для синтетических дат (новые сверху, шаг вниз по индексу).
    private static readonly DateTimeOffset MockBaseDate =
        new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement IsoDate(DateTimeOffset d) =>
        JsonDocument.Parse($"\"{d:o}\"").RootElement.Clone();

    private static List<InvoiceDto> BuildAllInvoices()
    {
        var acceptedSkus = new List<SkuForInvoiceDto>();
        foreach (var plan in BuildReconPlan())
        {
            acceptedSkus.Add(new SkuForInvoiceDto
            {
                SkuTitle = plan.Title,
                QuantityAcceptedRaw = plan.Accepted,
                QuantityToStockRaw = plan.Accepted,
            });
        }

        // Накладная ACCEPTED (новее) + CREATED-черновик (старее, её accepted игнорируем).
        return new List<InvoiceDto>
        {
            new()
            {
                Id = 500001,
                InvoiceNumber = 500001,
                DateCreatedRaw = IsoDate(MockBaseDate),
                DateAcceptedRaw = IsoDate(MockBaseDate.AddHours(6)),
                InvoiceStatus = new InvoiceStatusDto { Value = UzumConstants.InvoiceStatus.Accepted },
                ProductsRaw = new List<ProductForInvoiceDto>
                {
                    new() { ProductTitle = "Демо-поставка", SkusRaw = acceptedSkus },
                },
            },
            new()
            {
                Id = 500002,
                InvoiceNumber = 500002,
                DateCreatedRaw = IsoDate(MockBaseDate.AddDays(-1)),
                InvoiceStatus = new InvoiceStatusDto { Value = UzumConstants.InvoiceStatus.Created },
                ProductsRaw = new List<ProductForInvoiceDto>
                {
                    new()
                    {
                        ProductTitle = "Черновик поставки",
                        SkusRaw = new List<SkuForInvoiceDto>
                        {
                            // Тот же SKU (полный код), но статус накладной CREATED —
                            // НЕ должно попасть в accepted. Проверяет фильтр по статусу.
                            new()
                            {
                                SkuTitle = $"{Catalog[0].Product} — {Catalog[0].Sku}",
                                QuantityAcceptedRaw = 9999,
                            },
                        },
                    },
                },
            },
        };
    }

    private static List<SellerReturnDto> BuildAllReturns()
    {
        var returns = new List<SellerReturnDto>();
        var id = 600001L;
        var step = 0;

        foreach (var plan in BuildReconPlan())
        {
            if (plan.Removed <= 0)
                continue;

            // Чередуем две ветки legacy-правила, чтобы обе покрывались:
            //  - useAmount: totalPackedAmount==0 && assembledDate==null → берём amount;
            //  - иначе: assembledDate задан → берём packedAmount.
            var useAmount = id % 2 == 1;
            // Возвраты завершены (completedDate) → терминальны (для дельты). Даты убывают.
            var created = MockBaseDate.AddDays(-step);
            returns.Add(new SellerReturnDto
            {
                Id = id++,
                Type = UzumConstants.ReturnType.Return,
                Status = "COMPLETED",
                DateCreatedRaw = IsoDate(created),
                CompletedDateRaw = IsoDate(created.AddHours(12)),
                TotalPackedAmount = useAmount ? 0 : plan.Removed,
                TotalAmount = plan.Removed,
                AssembledDate = useAmount ? null : MockAssembledDate,
                ReturnItemsRaw = new List<SellerReturnItemDto>
                {
                    new()
                    {
                        SkuTitle = plan.Title,
                        AmountRaw = useAmount ? plan.Removed : 0,
                        PackedAmountRaw = useAmount ? 0 : plan.Removed,
                    },
                },
            });
            step++;
        }

        return returns;
    }

    // План реконсиляции: строится ИЗ построенных товаров, чтобы accepted точно
    // балансировался. Для lossIndexes закладываем положительный lost (== missing из
    // карточки, для узнаваемости); для остальных lost = 0. removed — детерминирован.
    private static IEnumerable<ReconPlan> BuildReconPlan()
    {
        var lossIndexes = new HashSet<int> { 1, 4, 8, 11, 15, 21 };
        var products = BuildAllProducts();

        for (var i = 0; i < products.Count; i++)
        {
            var sku = products[i].SkuList[0];
            var removed = i % 3 == 0 ? 2 : 0;
            var lost = lossIndexes.Contains(i) ? sku.QuantityMissing : 0;
            var accepted = lost + removed + sku.QuantitySold + sku.QuantityDefected + sku.QuantityActive;
            // ⚠️ Ключ матча — ПОЛНЫЙ код SKU. В накладных/возвратах он лежит в их skuTitle,
            // а в карточке — в skuFullTitle. Кладём сюда SkuFullTitle, иначе join с картой
            // (по SkuKey.Normalize(skuFullTitle)) не сматчится и accepted=0.
            yield return new ReconPlan(sku.SkuFullTitle ?? "", accepted, removed);
        }
    }

    private readonly record struct ReconPlan(string Title, int Accepted, int Removed);

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
