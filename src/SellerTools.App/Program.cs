using System.Text;
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
using SellerTools.App.Services;
using SellerTools.UzumApi;

// Кириллица в консоли Windows.
Console.OutputEncoding = Encoding.UTF8;

// Режим запуска: bot (по умолчанию) | diag <shopId> <key> | smoke
var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "bot";

var builder = Host.CreateApplicationBuilder(args);

// Секреты — из отдельного gitignore-файла (рядом с бинарником) и env.
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.Secrets.json"),
    optional: true, reloadOnChange: false);

// Тихий SQL и HTTP: показываем отчёты/диагностику, а не поток служебных логов.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Клиент Uzum API по режиму (Mock | Real) из appsettings.
builder.Services.AddUzumApi(builder.Configuration);

// EF Core + PostgreSQL.
var conn = builder.Configuration["ConnectionStrings:Postgres"]
           ?? "Host=localhost;Port=5432;Database=sellertools;Username=sellertools;Password=sellertools";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));

// Опции.
builder.Services.Configure<BotOptions>(builder.Configuration.GetSection(BotOptions.SectionName));
builder.Services.Configure<CryptoOptions>(builder.Configuration.GetSection(CryptoOptions.SectionName));

// Доменные сервисы.
builder.Services.AddSingleton<ICryptoService, AesCryptoService>();
builder.Services.AddSingleton<ConversationStore>();
builder.Services.AddScoped<SellerService>();
builder.Services.AddScoped<KeyVerification>();
builder.Services.AddScoped<LossReportService>();
builder.Services.AddTransient<DiagnosticRunner>();

switch (mode)
{
    case "diag":
        await RunDiagnosticAsync(builder, args);
        return;

    case "smoke":
        builder.Services.AddHostedService<SmokeReportService>();
        break;

    case "bot":
    default:
        builder.Services.AddHostedService<TelegramBotService>();
        break;
}

var host = builder.Build();

// Для бота гарантируем схему БД до старта поллинга.
if (mode is "bot")
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

await host.RunAsync();

// --- Локальные функции ---

static async Task RunDiagnosticAsync(HostApplicationBuilder builder, string[] args)
{
    if (args.Length < 3
        || !long.TryParse(args[1], out var shopId))
    {
        Console.WriteLine("Использование: dotnet run --project src/SellerTools.App -- diag <shopId> <apiKey>");
        Console.WriteLine("Пример:        dotnet run --project src/SellerTools.App -- diag 123456 my-secret-key");
        return;
    }

    var apiKey = args[2];

    using var host = builder.Build();
    using var scope = host.Services.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<DiagnosticRunner>();
    await runner.RunAsync(shopId, apiKey);
}
