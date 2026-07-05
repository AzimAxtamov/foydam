using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SellerTools.App;
using SellerTools.App.Bot;
using SellerTools.App.Configuration;
using SellerTools.App.Data;
using SellerTools.App.Diagnostics;
using SellerTools.App.Jobs;
using SellerTools.App.Services;
using SellerTools.UzumApi;

// Кириллица в консоли Windows.
Console.OutputEncoding = Encoding.UTF8;

// Режим запуска: bot (по умолчанию) | diag <shopId> <key> | smoke
var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "bot";

var conn = ResolveConnectionString();

// diag и smoke — лёгкий консольный хост без web/Hangfire.
if (mode is "diag" or "smoke")
{
    await RunConsoleModeAsync(mode, args, conn);
    return;
}

// --- Режим bot: WebApplication (для дашборда Hangfire) + фоновый сервер джоб ---
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.Secrets.json"),
    optional: true, reloadOnChange: false);

builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Hangfire", LogLevel.Information);

RegisterSharedServices(builder.Services, builder.Configuration, conn);

// Hangfire: хранилище в PostgreSQL.
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(conn)));
builder.Services.AddHangfireServer();

// Джоба и бот.
builder.Services.AddScoped<NightlySyncJob>();
builder.Services.AddHostedService<TelegramBotService>();

var app = builder.Build();

// Схема БД до старта.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Дашборд Hangfire — ТОЛЬКО в Development.
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
    app.Logger.LogInformation("Hangfire дашборд: http://localhost:5000/hangfire (только Development)");
}

// Рекуррентная джоба: ночной синк в 03:00 по Ташкенту.
// Через сервис (IRecurringJobManager), а не статический RecurringJob —
// иначе падает "JobStorage не инициализирован" (статика зависит от JobStorage.Current).
var tashkent = ResolveTashkentTimeZone();
app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<NightlySyncJob>(
    "nightly-shop-sync",
    job => job.RunAsync(CancellationToken.None),
    "0 3 * * *",
    new RecurringJobOptions { TimeZone = tashkent });

await app.RunAsync();

// --- Локальные функции ---

static string ResolveConnectionString()
{
    var b = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
        .AddEnvironmentVariables()
        .Build();
    return b["ConnectionStrings:Postgres"]
           ?? "Host=localhost;Port=5432;Database=sellertools;Username=sellertools;Password=sellertools";
}

static TimeZoneInfo ResolveTashkentTimeZone()
{
    // IANA "Asia/Tashkent"; на Windows фолбэк на Windows- id.
    foreach (var id in new[] { "Asia/Tashkent", "Central Asia Standard Time" })
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { }
    }
    // UTC+5 без перехода на летнее время.
    return TimeZoneInfo.CreateCustomTimeZone("UZT", TimeSpan.FromHours(5), "Uzbekistan Time", "UZT");
}

static void RegisterSharedServices(IServiceCollection services, IConfiguration config, string conn)
{
    services.AddUzumApi(config);
    services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));

    services.Configure<BotOptions>(config.GetSection(BotOptions.SectionName));
    services.Configure<CryptoOptions>(config.GetSection(CryptoOptions.SectionName));

    services.AddSingleton<ICryptoService, AesCryptoService>();
    services.AddSingleton<ConversationStore>();
    services.AddScoped<SellerService>();
    services.AddScoped<KeyVerification>();
    services.AddScoped<LossReportService>();
    services.AddScoped<ShopSyncService>();
    services.AddSingleton<ExcelExportService>();
    services.AddTransient<DiagnosticRunner>();
}

static async Task RunConsoleModeAsync(string mode, string[] args, string conn)
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddJsonFile(
        Path.Combine(AppContext.BaseDirectory, "appsettings.Secrets.json"),
        optional: true, reloadOnChange: false);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
    builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

    RegisterSharedServices(builder.Services, builder.Configuration, conn);

    if (mode == "smoke")
    {
        builder.Services.AddHostedService<SmokeReportService>();
        var host = builder.Build();
        await host.RunAsync();
        return;
    }

    // diag <shopId> <apiKey>
    if (args.Length < 3 || !long.TryParse(args[1], out var shopId))
    {
        Console.WriteLine("Использование: dotnet run --project src/SellerTools.App -- diag <shopId> <apiKey>");
        Console.WriteLine("Пример:        dotnet run --project src/SellerTools.App -- diag 123456 my-secret-key");
        return;
    }

    using var diagHost = builder.Build();
    using var scope = diagHost.Services.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<DiagnosticRunner>();
    await runner.RunAsync(shopId, args[2]);
}
