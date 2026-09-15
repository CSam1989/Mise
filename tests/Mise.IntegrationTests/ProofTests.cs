namespace Mise.IntegrationTests;

/// <summary>
/// Proves the two database harnesses actually work, before any feature relies on them
/// (docs/plan.md, "a test harness that has never failed is not yet known to work").
/// </summary>
[Trait("Category", "Integration")]
public class RollbackHarnessProofTests(PostgresContainerFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task InsertedRow_IsVisibleWithinTheTransaction_ButGoneAfterRollbackToAFreshConnection()
    {
        var id = Guid.NewGuid();

        await using (var insert = Connection.CreateCommand())
        {
            insert.Transaction = Transaction;
            insert.CommandText = "insert into test_proof.rollback_probe (id) values (@id)";
            insert.Parameters.AddWithValue("id", id);
            await insert.ExecuteNonQueryAsync(CT);
        }

        (await RowExistsAsync(Connection, Transaction, id)).Should().BeTrue(
            because: "the same connection, within the same transaction, must see its own uncommitted write.");

        await Transaction.RollbackAsync(CT);

        await using var freshConnection = new NpgsqlConnection(ConnectionString);
        await freshConnection.OpenAsync(CT);

        (await RowExistsAsync(freshConnection, null, id)).Should().BeFalse(
            because: "a rolled-back write must never have been visible to any other connection — this is the isolation the whole rollback harness depends on.");
    }
}

public class CommittedWriteHarnessProofTests(PostgresContainerFixture fixture) : CommittedWriteTestBase(fixture)
{
    [Fact]
    public async Task CommittedWrite_IsVisibleFromASeparateConnection()
    {
        var id = Guid.NewGuid();

        await using (var insert = Connection.CreateCommand())
        {
            insert.CommandText = "insert into test_proof.committed_write_probe (id) values (@id)";
            insert.Parameters.AddWithValue("id", id);
            await insert.ExecuteNonQueryAsync(CT); // no ambient transaction on this connection: auto-commits.
        }

        await using var secondConnection = new NpgsqlConnection(ConnectionString);
        await secondConnection.OpenAsync(CT);
        await using var query = secondConnection.CreateCommand();
        query.CommandText = "select exists(select 1 from test_proof.committed_write_probe where id = @id)";
        query.Parameters.AddWithValue("id", id);

        ((bool)(await query.ExecuteScalarAsync(CT))!).Should().BeTrue(
            because: "a committed write must be visible to a second, independent connection — the rollback harness above can never prove this by construction.");
    }

    [Fact]
    public async Task ProbeTable_AtTheStartOfEveryTest_IsEmpty()
    {
        (await CountRowsAsync("committed_write_probe")).Should().Be(0,
            because: "Respawn must reset the scratch schema before each test in this harness, regardless of what an earlier test committed.");
    }
}
