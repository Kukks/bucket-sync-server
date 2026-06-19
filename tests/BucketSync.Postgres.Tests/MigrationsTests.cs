using BucketSync.Postgres;
using Xunit;

namespace BucketSync.Postgres.Tests;

[Collection(nameof(PostgresCollection))]
public class MigrationsTests
{
    private readonly PostgresFixture _fx;
    public MigrationsTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task Migrations_are_idempotent_and_create_tables()
    {
        await Migrations.ApplyAsync(_fx.DataSource); // second apply must be a no-op, not an error

        await using var cmd = _fx.DataSource.CreateCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = ANY(@t)");
        cmd.Parameters.AddWithValue("t", new[] { "buckets", "entries", "sessions", "challenges" });
        Assert.Equal(4L, (long)(await cmd.ExecuteScalarAsync())!);
    }
}
