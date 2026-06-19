using Npgsql;
using Testcontainers.PostgreSql;
using BucketSync.Postgres;
using Xunit;

namespace BucketSync.Postgres.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        await Migrations.ApplyAsync(DataSource);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
