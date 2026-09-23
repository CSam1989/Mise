namespace Mise.SharedKernel.Infrastructure;

/// <summary>
/// Cross-cutting audit port every mutating command handler depends on — CrossCuttingTests'
/// <c>EveryCommandHandler_DependsOnSomethingImplementingIAuditWriter</c> enforces the
/// constructor dependency at the architecture-test tier. Phase 9 (ADR-009) adds
/// <see cref="Stage"/> and the <c>AuditCompletenessInterceptor</c> guarantee that a handler
/// holding this dependency actually used it: a handler that owns a gateway mutation call
/// stages the entry first — no save here, so it lands in the *same* <c>SaveChangesAsync</c>
/// call the gateway's own mutation makes, true atomicity instead of two separate round trips.
/// <see cref="WriteAsync"/> stays for the rarer case with no co-occurring mutation to attach to
/// (e.g. <c>LoginCommandHandler</c>'s "SignedIn" entry — <c>ValidateCredentialsAsync</c> is a
/// pure read, so there's nothing for a staged entry to piggyback on).
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Adds <paramref name="entry"/> to the change tracker only — does not save. The caller must
    /// be about to trigger its own <c>SaveChangesAsync</c> (typically a gateway mutation call
    /// immediately after) on the same <c>DbContext</c>; on any early-return path that call never
    /// happens (an idempotent replay, a rejected business rule, a concurrency conflict), the
    /// staged-but-unflushed entry is simply discarded when the request's scoped
    /// <c>DbContext</c> disposes — no explicit rollback needed.
    /// </summary>
    void Stage(AuditLogEntry entry);

    /// <summary>Adds <paramref name="entry"/> and saves immediately — use only when there is no
    /// other mutation in the same request to piggyback on.</summary>
    Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken);
}
