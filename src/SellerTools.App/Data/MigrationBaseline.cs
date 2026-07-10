using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SellerTools.App.Data;

/// <summary>
/// Baseline существующей прод-БД под EF-миграции. Прежде схема создавалась
/// EnsureCreated (без __EFMigrationsHistory). Прямой Migrate() на такой БД упал бы
/// на CREATE уже существующих таблиц InitialCreate.
///
/// Логика:
///  - истории миграций НЕТ, но таблица "Shops" уже существует → это legacy-БД от
///    EnsureCreated. Создаём __EFMigrationsHistory и вставляем строку InitialCreate
///    (помечаем применённой), чтобы Migrate() применил ТОЛЬКО AddSyncStorage;
///  - истории нет и "Shops" нет → чистая БД, ничего не делаем (Migrate создаст всё);
///  - история уже есть → ничего не делаем.
/// Идемпотентно: повторный запуск ничего не портит.
/// </summary>
public static class MigrationBaseline
{
    // Должен ТОЧНО совпадать с именем файла миграции InitialCreate (без .cs).
    private const string InitialCreateId = "20260710071528_InitialCreate";

    public static async Task EnsureAsync(AppDbContext db, ILogger logger)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            // Уже применён InitialCreate (строка в истории есть) → под миграциями, выходим.
            var historyExists = await ScalarBoolAsync(conn,
                "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL");
            if (historyExists)
            {
                var initialApplied = await ScalarBoolAsync(conn,
                    "SELECT EXISTS(SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @id)",
                    InitialCreateId);
                if (initialApplied)
                    return; // InitialCreate уже отмечен — Migrate накатит остальное
            }

            // Legacy-таблицы от EnsureCreated (или от прерванного InitialCreate) есть, а
            // строки InitialCreate в истории нет → нужно baseline'ить (в т.ч. когда таблица
            // истории уже создана, но пустая — иначе Migrate снова попытается CREATE Sellers).
            var legacyTablesExist = await ScalarBoolAsync(conn,
                "SELECT to_regclass('public.\"Shops\"') IS NOT NULL");
            if (!legacyTablesExist)
            {
                logger.LogInformation("BASELINE: чистая БД — Migrate создаст схему с нуля.");
                return; // чистая БД
            }

            logger.LogWarning(
                "BASELINE: обнаружены legacy-таблицы без строки {Migration} в истории — " +
                "помечаю её применённой, чтобы Migrate применил только новые таблицы.",
                InitialCreateId);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                );
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES (@id, '8.0.0')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "@id";
            p.Value = InitialCreateId;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static async Task<bool> ScalarBoolAsync(
        System.Data.Common.DbConnection conn, string sql, string? idParam = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (idParam is not null)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@id";
            p.Value = idParam;
            cmd.Parameters.Add(p);
        }
        var result = await cmd.ExecuteScalarAsync();
        return result is bool b && b;
    }
}
