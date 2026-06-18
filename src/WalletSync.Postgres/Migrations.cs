using System.Reflection;
using Npgsql;

namespace WalletSync.Postgres;

public static class Migrations
{
    public static async Task ApplyAsync(NpgsqlDataSource ds, CancellationToken ct = default)
    {
        await using (var cmd = ds.CreateCommand(
            "CREATE TABLE IF NOT EXISTS schema_migrations (version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())"))
            await cmd.ExecuteNonQueryAsync(ct);

        var asm = typeof(Migrations).Assembly;
        var scripts = asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var name in scripts)
        {
            var version = name; // resource name is stable & ordered (…Migrations.001_init.sql)
            await using var check = ds.CreateCommand("SELECT 1 FROM schema_migrations WHERE version = @v");
            check.Parameters.AddWithValue("v", version);
            if (await check.ExecuteScalarAsync(ct) is not null) continue;

            var sql = await ReadResourceAsync(asm, name);
            await using var conn = await ds.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            await using (var run = new NpgsqlCommand(sql, conn, tx))
                await run.ExecuteNonQueryAsync(ct);
            await using (var mark = new NpgsqlCommand("INSERT INTO schema_migrations (version) VALUES (@v)", conn, tx))
            {
                mark.Parameters.AddWithValue("v", version);
                await mark.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
    }

    private static async Task<string> ReadResourceAsync(Assembly asm, string name)
    {
        await using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
