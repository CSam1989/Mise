using Respawn;

namespace Mise.IntegrationTests;

/// <summary>
/// Committed-write harness (docs/plan.md's "one shared connection can't express this"
/// gap): NFR-03 concurrency, a Postgres exclusion constraint, and SignalR propagation all
/// need genuinely committed data visible to a second connection, which the rollback harness
/// above can never produce by design. Respawn resets the scratch schema before each test
/// instead.
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class CommittedWriteTestBase(PostgresContainerFixture fixture) : IAsyncLifetime
{
    protected NpgsqlConnection Connection { get; private set; } = null!;

    // See IntegrationTestBase.ConnectionString for why this is exposed rather than letting a
    // derived class reference the `fixture` primary-constructor parameter directly (CS9107).
    protected string ConnectionString => fixture.ConnectionString;

    protected static CancellationToken CT => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Connection = new NpgsqlConnection(fixture.ConnectionString);
        await Connection.OpenAsync(CT);

        var respawner = await Respawner.CreateAsync(Connection, new RespawnerOptions
        {
            SchemasToInclude = ["test_proof"],
            DbAdapter = DbAdapter.Postgres,
        });
        await respawner.ResetAsync(Connection);
    }

    public async ValueTask DisposeAsync() => await Connection.DisposeAsync();

    protected async Task<int> CountRowsAsync(string table)
    {
        await using var command = Connection.CreateCommand();
        command.CommandText = $"select count(*) from test_proof.{table}";
        return (int)(long)(await command.ExecuteScalarAsync(CT))!;
    }
}
