using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mise.Modules.Reservations.Domain;
using Mise.Modules.Reservations.Infrastructure.Persistence;
using Mise.SharedKernel.Persistence;

namespace Mise.IntegrationTests;

/// <summary>
/// Phase 9 / ADR-009's runtime "belt" guardrail, proven directly against a real DbContext
/// resolved from the same DI container every endpoint request uses — so the registered
/// AuditCompletenessInterceptor is the one actually exercised, not a hand-built stand-in. The
/// positive path (staging an entry correctly) is already proven, repeatedly, by every existing
/// *WritesExactlyOneMatchingAuditLogEntry test across the suite — those tests would themselves
/// start failing if the interceptor had any false-positive bug, since they all go through the
/// real stage-before-mutate handlers. This file exists only to prove the negative: a save that
/// never staged anything at all is refused, before any SQL is sent.
/// </summary>
[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class AuditCompletenessInterceptorTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveChangesAsync_MutatesAnAuditableEntityWithNoStagedAuditEntry_ThrowsAndPersistsNothing()
    {
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReservationsDbContext>();

        var reservation = Reservation.Create(
            Guid.NewGuid(), "Interceptor Proof", "+32 470 00 00 01", null, 2,
            DateTimeOffset.UtcNow.AddDays(1), 90, null, null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        // Set<Reservation>(), not the DbContext's own DbSet property: that property is internal
        // (same encapsulation every module's DbSets have — see MiseApiFixture's own comment on
        // StaffIdentityDbContext.StaffUsers), inaccessible from this test project.
        dbContext.Set<Reservation>().Add(reservation);

        var act = () => dbContext.SaveChangesAsync(CT);

        await act.Should().ThrowAsync<AuditCompletenessViolationException>(
            because: "a mutated IAuditableEntity with no matching AuditLogEntry staged in the same SaveChangesAsync " +
            "call must be refused before any SQL is sent — the runtime guarantee that catches a future handler that " +
            "forgot to call IAuditWriter.Stage, or called it after the mutating gateway call instead of before.");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(CT);
        await using var command = connection.CreateCommand();
        command.CommandText = "select exists(select 1 from reservations.reservation where id = @id)";
        command.Parameters.AddWithValue("id", reservation.Id);
        var exists = (bool)(await command.ExecuteScalarAsync(CT))!;
        exists.Should().BeFalse(because: "the interceptor must refuse the save before the INSERT is ever sent, not roll one back after the fact.");
    }
}
