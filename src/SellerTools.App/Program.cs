using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SellerTools.App;
using SellerTools.App.Data;
using SellerTools.App.Services;
using SellerTools.UzumApi;

// Кириллица в консоли Windows.
Console.OutputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// Тихий SQL: показываем отчёт, а не поток команд EF.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

// Клиент Uzum API по режиму (Mock | Real) из appsettings.
builder.Services.AddUzumApi(builder.Configuration);

// EF Core + PostgreSQL.
var conn = builder.Configuration["ConnectionStrings:Postgres"]
           ?? "Host=localhost;Port=5433;Database=sellertools;Username=sellertools;Password=sellertools";
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(conn));

// Сервисы.
builder.Services.AddScoped<LossReportService>();

// Smoke-тест В1.
builder.Services.AddHostedService<SmokeReportService>();

var host = builder.Build();
await host.RunAsync();
