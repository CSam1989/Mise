namespace Mise.IntegrationTests;

/// <summary>
/// Rollback harness: opens a connection and begins a transaction before each test, and
/// disposes both after — Npgsql treats disposing a transaction that was never committed as
/// a rollback, and disposing one that was already rolled back explicitly (as the proof
/// test below does, to make the point observable within a single test method) as a no-op.
///
/// FamilySplit's version of this harness joins the DbContext to this same transaction via
/// `UseTransaction` — there is no DbContext to join yet (no module exists until Phase 2),
/// so this stays a plain ADO.NET connection/transaction for now. A subclass gains a
/// `DbContext` property the moment there's a real one to give it.
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class IntegrationTestBase(PostgresContainerFixture fixture) : IAsyncLifetime
{
    protected NpgsqlConnection Connection { get; private set; } = null!;
    protected NpgsqlTransaction Transaction { get; private set; } = null!;

    // Exposed rather than letting a derived class reference the `fixture` primary-constructor
    // parameter directly: doing so from a derived type's own member body is flagged (CS9107)
    // as ambiguous capture between the base and derived class.
    protected string ConnectionString => fixture.ConnectionString;

    protected static CancellationToken CT => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Connection = new NpgsqlConnection(fixture.ConnectionString);
        await Connection.OpenAsync(CT);
        Transaction = await Connection.BeginTransactionAsync(CT);
    }

    public async ValueTask DisposeAsync()
    {
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }

    protected async Task<bool> RowExistsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select exists(select 1 from test_proof.rollback_probe where id = @id)";
        command.Parameters.AddWithValue("id", id);
        return (bool)(await command.ExecuteScalarAsync(CT))!;
    }
}
