using Testcontainers.PostgreSql;

namespace Mise.IntegrationTests;

/// <summary>
/// One real PostgreSQL container, shared across the whole assembly via
/// <see cref="PostgresCollection"/> — starting a container per test would make the suite
/// too slow to be the default. Also creates the scratch schema the two proof-test harnesses
/// (<see cref="IntegrationTestBase"/>, <see cref="CommittedWriteTestBase"/>) write into;
/// there is no module DbContext yet for either harness to exercise instead (Phase 2 adds
/// the first one).
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Container has not been started yet.");

    public async ValueTask InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _container.StartAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            create schema if not exists test_proof;
            create table if not exists test_proof.rollback_probe (id uuid primary key);
            create table if not exists test_proof.committed_write_probe (id uuid primary key);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "Postgres";
}
