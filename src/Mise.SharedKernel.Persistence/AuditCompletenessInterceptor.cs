using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Mise.SharedKernel.Infrastructure;

namespace Mise.SharedKernel.Persistence;

/// <summary>
/// Phase 9 / ADR-009's runtime "belt" guardrail (the architecture-tier
/// <c>CrossCuttingTests.EveryCommandHandler_DependsOnSomethingImplementingIAuditWriter</c> is
/// the "suspenders" — it catches a handler with no <c>IAuditWriter</c> dependency at all; this
/// catches one that has the dependency but staged nothing, or staged it too late). No ambient
/// "flag" object exists or is needed: the change tracker itself is the flag. If this
/// <c>SaveChangesAsync</c> call is about to persist an Added/Modified/Deleted
/// <see cref="IAuditableEntity"/> with no matching <see cref="AuditLogEntry"/> staged in the
/// same call, the save is refused before any SQL is sent. Deliberately keyed off the
/// <see cref="IAuditableEntity"/> marker rather than "any entity change at all" — ASP.NET
/// Identity's own internal writes (password-hash rehash on login, security stamp updates) touch
/// <c>StaffIdentityUser</c>, which does not implement the marker, so they never trip this check;
/// only this project's own domain aggregates (<c>Reservation</c>, <c>Table</c>, <c>Section</c>,
/// <c>TableGroup</c>, <c>ServicePeriod</c>, <c>StaffUser</c>) do.
/// </summary>
public sealed class AuditCompletenessInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is not null)
        {
            var mutatedAnAuditableEntity = context.ChangeTracker.Entries()
                .Any(e => e.Entity is IAuditableEntity
                          && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

            if (mutatedAnAuditableEntity)
            {
                var stagedAnAuditEntry = context.ChangeTracker.Entries<AuditLogEntry>()
                    .Any(e => e.State == EntityState.Added);

                if (!stagedAnAuditEntry)
                {
                    throw new AuditCompletenessViolationException(
                        $"{context.GetType().Name}.SaveChangesAsync would persist a mutated IAuditableEntity " +
                        "with no matching AuditLogEntry staged in the same call. The caller either never called " +
                        "IAuditWriter.Stage, or called it after the mutating gateway call instead of before.");
                }
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
