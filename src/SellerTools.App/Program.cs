using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
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

// Режим запуска: bot (по умолчанию) | diag <shopId> <key> | smoke | syncsmoke
var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "bot";

var conn = ResolveConnectionString();

// diag / smoke / syncsmoke — лёгкий консольный хост без web/Hangfire.
if (mode is "diag" or "smoke" or "syncsmoke")
{
    await RunConsoleModeAsync(mode, args, conn);
    return;
}

// --- Режим bot: WebApplication (для дашборда Hangfire) + фоновый сервер джоб ---
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.Secrets.json"),
    optional: true, reloadOnChange: false);

// Serilog: конфиг из appsettings (уровни, sinks — консоль + файл с ротацией).
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

RegisterSharedServices(builder.Services, builder.Configuration, conn);

// Health-check для healthcheck контейнера (проверяет доступность БД).
builder.Services.AddScoped<DbHealthCheck>();
builder.Services.AddHealthChecks().AddCheck<DbHealthCheck>("postgres");

// Hangfire: хранилище в PostgreSQL.
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(conn)));

// Дефолтный сервер: обычные джобы (default-очередь).
builder.Services.AddHangfireServer(o => o.Queues = new[] { "default" });

// ⚠️ Отдельный сервер ТОЛЬКО для очереди uzum-sync с РОВНО 3 воркерами — это
// глобальный лимит параллелизма запросов к Uzum на процесс. Синк-джобы ставятся в
// эту очередь; внутри джоба запросы строго последовательны (никаких Parallel к API).
builder.Services.AddHangfireServer(o =>
{
    o.Queues = new[] { UzumSyncJob.Queue };
    o.WorkerCount = 3;
    o.ServerName = "uzum-sync";
});

// Джобы и бот.
builder.Services.AddScoped<UzumSyncJob>();
builder.Services.AddScoped<ReportDeliveryJob>();
builder.Services.AddHostedService<TelegramBotService>();

var app = builder.Build();

// Глобальные необработанные исключения → лог + алерт админу.
var alerts = app.Services.GetRequiredService<IAdminAlertService>();
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    var ex = e.ExceptionObject as Exception;
    Log.Fatal(ex, "Необработанное исключение (AppDomain)");
    // GetAwaiter().GetResult() здесь корректен: обработчик СИНХРОННЫЙ (нельзя await).
    try { alerts.AlertAsync(AlertKind.UnhandledError, $"AppDomain: {ex?.Message}").GetAwaiter().GetResult(); }
    catch (Exception alertEx) { Log.Warning(alertEx, "Не удалось отправить админ-алерт (AppDomain)"); }
};
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "Необработанное исключение в задаче (UnobservedTaskException)");
    try { alerts.AlertAsync(AlertKind.UnhandledError, $"Task: {e.Exception.Message}").GetAwaiter().GetResult(); }
    catch (Exception alertEx) { Log.Warning(alertEx, "Не удалось отправить админ-алерт (Task)"); }
    e.SetObserved();
};

// Схема БД до старта — теперь через EF-миграции (не EnsureCreated).
// ⚠️ BASELINE существующей прод-БД: она была создана прежним EnsureCreated и НЕ имеет
// таблицы __EFMigrationsHistory. Прямой Migrate() на ней попытается создать уже
// существующие таблицы InitialCreate → упадёт. Поэтому на такой БД перед первым
// запуском нужно вручную «пометить» InitialCreate применённой (baseline), см. DEPLOY.md.
// Здесь помогаем автоматически: если старые таблицы уже есть, а истории миграций нет —
// вставляем строку InitialCreate в историю, затем Migrate применит только AddSyncStorage.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await MigrationBaseline.EnsureAsync(db, app.Logger);
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/health");

// Дашборд Hangfire — ТОЛЬКО в Development.
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");
    app.Logger.LogInformation("Hangfire дашборд: http://localhost:5000/hangfire (только Development)");
}

// Рекуррентная джоба: ночной полный синк карточек всех магазинов в 03:00 по Ташкенту.
// Идёт в очередь uzum-sync (глоб. лимит 3 воркера). Внутри — по магазину со случайным
// сдвигом старта (чтобы не бить в Uzum всеми магазинами одновременно), см. NightlyAllAsync.
// Через сервис (IRecurringJobManager), а не статический RecurringJob — иначе падает
// "JobStorage не инициализирован" (статика зависит от JobStorage.Current).
var tashkent = ResolveTashkentTimeZone();
app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<UzumSyncJob>(
    "nightly-shop-sync",
    job => job.NightlyAllAsync(CancellationToken.None),
    "0 3 * * *",
    new RecurringJobOptions { TimeZone = tashkent });

// Приёмка: существующим магазинам (до перехода на «синк → БД») проставить Never и
// поставить первичные синки с интервалом. Идёт после инициализации Hangfire-хранилища.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var jobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
    await StartupProvisioner.ProvisionAsync(db, jobs, app.Logger);
}

try
{
    Log.Information("SellerTools запускается (env={Env})", app.Environment.EnvironmentName);
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Хост упал при запуске");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

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
    services.Configure<AdminOptions>(config.GetSection(AdminOptions.SectionName));

    services.AddSingleton<IAdminAlertService, AdminAlertService>();
    services.AddSingleton<ICryptoService, AesCryptoService>();
    services.AddSingleton<ConversationStore>();
    services.AddScoped<SellerService>();
    services.AddScoped<KeyVerification>();
    services.Configure<SyncOptions>(config.GetSection(SyncOptions.SectionName));

    services.AddScoped<LossReportService>();
    services.AddScoped<SyncStorage>();
    services.AddScoped<UzumSyncService>();
    services.AddScoped<ReportCoordinator>();
    services.AddSingleton<IBotNotifier, BotNotifier>();
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

    // syncsmoke — прогон полного + дельта-синка на мок-магазине с проверкой БД.
    if (mode == "syncsmoke")
    {
        using var host = builder.Build();
        await SyncSmoke.RunAsync(host.Services);
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
