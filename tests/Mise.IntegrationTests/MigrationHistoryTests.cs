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

        // Exact counts, not >= 1: a real history-table collision (two modules sharing one
        // physical table) would still leave every schema showing "at least one" row, silently
        // passing a looser assertion — the exact count is what actually catches it, since a
        // shared table would show every module's combined migration count in each schema's
        // query alike. Bump the relevant number in the same commit as any module's next
        // migration (Phase 6 added Reservations' AddReservationDetailsAndConcurrency and
        // Tables' AddTableGroups, taking both from 1 to 2; Phase 9 added Reservations'
        // AddAuditLogEntryEntityTypeEntityIdIndex, taking it from 2 to 3).
        reservationsMigrationCount.Should().Be(3,
            because: "Reservations' own migrations-history table must exist and record exactly its own three migrations.");
        staffIdentityMigrationCount.Should().Be(1,
            because: "StaffIdentity's own migrations-history table must exist, distinct from Reservations' — a shared table would make the second module's migration appear \"already applied\" via an id collision.");
        tablesMigrationCount.Should().Be(2,
            because: "Tables' own migrations-history table must exist, distinct from the other two, and record exactly its own two migrations (InitialCreate + Phase 6's AddTableGroups).");
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
