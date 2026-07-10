using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SellerTools.App.Data;
using SellerTools.App.Services;
using SellerTools.Core.Entities;
using SellerTools.UzumApi.Dto;

namespace SellerTools.App;

/// <summary>
/// syncsmoke: прогоняет полный + дельта-синк на мок-магазине против реальной БД и
/// печатает счётчики таблиц. Проверяет: миграции применяются, full наполняет таблицы,
/// delta идёт идемпотентно (без дублей), page0/page1-guard не срабатывает на моке.
/// Запуск: dotnet run --project src/SellerTools.App -- syncsmoke
/// </summary>
public static class SyncSmoke
{
    private const long ShopId = 777001;

    public static async Task RunAsync(IServiceProvider root)
    {
        using var scope = root.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var crypto = sp.GetRequiredService<ICryptoService>();
        var sync = sp.GetRequiredService<UzumSyncService>();

        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("syncsmoke");

        Console.WriteLine("== syncsmoke ==");

        // Регрессия прод-бага: живой Uzum отдаёт "productForInvoiceDto": null,
        // "returnItems": null и dateCreated в формате "22.08.2023" (dd.MM.yyyy).
        // STJ затирал = new() → NullReferenceException в синке; InvariantCulture не
        // парсил dd.MM.yyyy → лавина warning + null. Проверяем оба до синка.
        AssertLiveDataShapes();


        // Baseline существующей (legacy, от EnsureCreated) БД перед Migrate — как в Program.cs.
        await MigrationBaseline.EnsureAsync(db, logger);
        await db.Database.MigrateAsync();
        Console.WriteLine("миграции применены (с baseline)");

        // Готовим мок-магазин (ключ шифруется как в проде).
        var seller = await db.Sellers.FirstOrDefaultAsync(s => s.Id == 1);
        if (seller is null)
        {
            db.Sellers.Add(new Seller { Id = 1, Language = "ru" });
            await db.SaveChangesAsync();
        }
        var shop = await db.Shops.FirstOrDefaultAsync(s => s.Id == ShopId);
        if (shop is null)
        {
            shop = new Shop { Id = ShopId, SellerId = 1, Title = "Демо-магазин" };
            db.Shops.Add(shop);
        }
        shop.ApiKeyEncrypted = crypto.Encrypt("mock-key");
        await db.SaveChangesAsync();

        // Сброс состояния синка на Never → первый прогон будет полным.
        var state = await db.ShopSyncStates.FirstOrDefaultAsync(x => x.ShopId == ShopId);
        if (state is not null)
        {
            db.ShopSyncStates.Remove(state);
            await db.SaveChangesAsync();
        }

        Console.WriteLine("\n-- ПОЛНЫЙ синк --");
        var r1 = await sync.SyncShopAsync(shop);
        Console.WriteLine($"ok={r1.Ok} inactive={r1.Inactive} error={r1.Error}");
        await PrintCountsAsync(db);

        Console.WriteLine("\n-- ДЕЛЬТА синк (повтор, должен быть идемпотентен) --");
        var r2 = await sync.SyncShopAsync(shop);
        Console.WriteLine($"ok={r2.Ok} inactive={r2.Inactive} error={r2.Error}");
        await PrintCountsAsync(db);

        // Проверка идемпотентности: счётчики после дельты не должны вырасти.
        Console.WriteLine("\n-- Проверки --");
        var invoices = await db.Invoices.CountAsync(x => x.ShopId == ShopId);
        var invItems = await db.InvoiceItems.CountAsync(i => db.Invoices.Any(v => v.Id == i.InvoiceId && v.ShopId == ShopId));
        var accepted = await db.Invoices.Where(x => x.ShopId == ShopId && x.IsTerminal).CountAsync();
        var returns = await db.Returns.CountAsync(x => x.ShopId == ShopId);
        var skus = await db.Skus.CountAsync(x => x.ShopId == ShopId);
        var st = await db.ShopSyncStates.FirstAsync(x => x.ShopId == ShopId);

        Console.WriteLine($"накладных={invoices} (терминальных={accepted}), строк накладных={invItems}");
        Console.WriteLine($"возвратов={returns}, карточек SKU={skus}");
        Console.WriteLine($"состояние: status={st.Status} full={st.LastFullSyncAt:o} delta={st.LastDeltaSyncAt:o}");

        Assert(invoices == 2, $"ожидалось 2 накладные (ACCEPTED+CREATED), получено {invoices}");
        Assert(accepted == 1, $"ожидалась 1 терминальная (ACCEPTED) накладная, получено {accepted}");
        Assert(skus == 25, $"ожидалось 25 карточек, получено {skus}");
        Assert(st.Status == SyncStatus.Ready, $"ожидался статус Ready, получено {st.Status}");
        Assert(st.LastFullSyncAt != null, "ожидался проставленный LastFullSyncAt");

        // Отчёт ИЗ БД: у мока заложены потери на 6 SKU — join card↔invoice ДОЛЖЕН
        // сматчиться (это ловит «немой ноль»: если join сломан — потерь будет 0).
        var report = sp.GetRequiredService<LossReportService>();
        var lossReport = await report.BuildFromDbAsync(ShopId, "Демо-магазин");
        Console.WriteLine(
            $"\nотчёт из БД: строк с потерями={lossReport.Rows.Count}, " +
            $"итого потеряно={lossReport.TotalLost}, данные на={lossReport.DataAsOf:o}");
        Assert(lossReport.Rows.Count == 6, $"ожидалось 6 SKU с потерями (заложено в моке), получено {lossReport.Rows.Count}");
        Assert(lossReport.TotalLost > 0, $"ожидались ненулевые потери, получено {lossReport.TotalLost}");
        Assert(lossReport.DataAsOf != null, "ожидался проставленный DataAsOf (подвал «Данные на …»)");

        Console.WriteLine("\n✅ syncsmoke OK");
    }

    private static async Task PrintCountsAsync(AppDbContext db)
    {
        Console.WriteLine(
            $"   invoices={await db.Invoices.CountAsync()} " +
            $"invoiceItems={await db.InvoiceItems.CountAsync()} " +
            $"returns={await db.Returns.CountAsync()} " +
            $"returnItems={await db.ReturnItems.CountAsync()} " +
            $"skus={await db.Skus.CountAsync()}");
    }

    // Проверяет устойчивость DTO к реальным «грязным» формам Uzum (без БД и API).
    private static void AssertLiveDataShapes()
    {
        Console.WriteLine("\n-- Проверка форм живых данных (регрессия прод-NRE) --");

        // 1) invoice.dateCreated = "22.08.2023" (dd.MM.yyyy) → должен распарситься.
        var inv = JsonSerializer.Deserialize<InvoiceDto>(
            """{"id":331710,"invoiceStatus":{"value":"ACCEPTED"},"dateCreated":"22.08.2023","productForInvoiceDto":null}""")!;
        Assert(inv.DateCreated != null, $"dateCreated '22.08.2023' должен распарситься, получено {inv.DateCreated?.ToString() ?? "null"}");
        Assert(inv.DateCreated!.Value.Year == 2023 && inv.DateCreated.Value.Month == 8 && inv.DateCreated.Value.Day == 22,
            $"22.08.2023 → 2023-08-22, получено {inv.DateCreated:yyyy-MM-dd}");

        // 2) "productForInvoiceDto": null → Products НЕ null (иначе foreach = NRE).
        Assert(inv.Products != null && inv.Products.Count == 0, "Products при JSON null → пустой список, не null");

        // 3) "returnItems": null → ReturnItems НЕ null.
        var ret = JsonSerializer.Deserialize<SellerReturnDto>(
            """{"id":600001,"returnItems":null,"totalPackedAmount":0}""")!;
        Assert(ret.ReturnItems != null && ret.ReturnItems.Count == 0, "ReturnItems при JSON null → пустой список, не null");

        // 4) Массив накладных с null-полями обходится без NRE (как в синке).
        var invoices = JsonSerializer.Deserialize<List<InvoiceDto>>(
            """[{"id":1,"productForInvoiceDto":null},{"id":2,"invoiceStatus":null,"productForInvoiceDto":[{"skuForInvoiceDtoList":null}]}]""")!;
        var totalSkus = 0;
        foreach (var i in invoices)
            foreach (var p in i.Products)
                foreach (var s in p.Skus)
                    totalSkus += s.QuantityAccepted;
        Assert(totalSkus == 0, "обход накладных с null-коллекциями не падает NRE");
    }

    private static void Assert(bool cond, string msg)
    {
        if (!cond)
            throw new Exception("ПРОВЕРКА НЕ ПРОШЛА: " + msg);
        Console.WriteLine("   ✓ " + msg.Split('—')[0].Trim());
    }
}
