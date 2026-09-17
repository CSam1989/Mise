namespace Mise.IntegrationTests;

/// <summary>
/// Proves the "four DbContexts sharing one Postgres database need per-module migration
/// history tables" gotcha (docs/plan.md) actually holds now that a second module exists —
/// Phase 2 could only assert one table existed; this is the first point a *collision* would
/// have been possible to observe at all.
/// </summary>
[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class MigrationHistoryTests(MiseApiFixture fixture)
{
    [Fact]
    public async Task EachModule_HasItsOwnDistinctMigrationsHistoryTable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(ct);

        var reservationsMigrationCount = await CountRowsAsync(connection, "reservations", ct);
        var staffIdentityMigrationCount = await CountRowsAsync(connection, "staff_identity", ct);
        var tablesMigrationCount = await CountRowsAsync(connection, "tables", ct);
        var schedulingMigrationCount = await CountRowsAsync(connection, "scheduling", ct);

        reservationsMigrationCount.Should().Be(1,
            because: "Reservations' own migrations-history table must exist and record its one migration.");
        staffIdentityMigrationCount.Should().Be(1,
            because: "StaffIdentity's own migrations-history table must exist, distinct from Reservations' — a shared table would make the second module's migration appear \"already applied\" via an id collision.");
        tablesMigrationCount.Should().Be(1,
            because: "Tables' own migrations-history table must exist, distinct from the other two — same collision risk Phase 3 already proved out, now checked against a third module.");
        schedulingMigrationCount.Should().Be(1,
            because: "Scheduling's own migrations-history table must exist, distinct from the other three — same collision risk, now checked against a fourth module.");
    }

    private static async Task<long> CountRowsAsync(NpgsqlConnection connection, string schema, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"select count(*) from {schema}.\"__ef_migrations_history\"";
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
