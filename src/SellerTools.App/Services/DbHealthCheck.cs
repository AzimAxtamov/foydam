using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SellerTools.App.Data;

namespace SellerTools.App.Services;

/// <summary>
/// Health-check доступности Postgres — используется /health и healthcheck контейнера.
/// </summary>
public class DbHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DbHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("Postgres доступен")
                : HealthCheckResult.Unhealthy("Postgres недоступен");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Ошибка подключения к Postgres", ex);
        }
    }
}
