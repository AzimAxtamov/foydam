using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SellerTools.App.Data;

/// <summary>
/// Фабрика контекста для dotnet-ef во время дизайна (генерация миграций). Не запускает
/// весь хост (Program.cs — WebApplication с режимами по args), поэтому EF использует
/// эту фабрику. Строка подключения — из env или дефолт (миграции генерируются без БД).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=sellertools;Username=sellertools;Password=sellertools";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new AppDbContext(options);
    }
}
