namespace Mise.SharedKernel.Infrastructure;

/// <summary>
/// Phase 9 bug found while wiring up the interceptor, not by inspection:
/// <c>IAuditWriter</c> is registered once per module (each closed over that module's own
/// <c>DbContext</c>, so a write lands on the same connection/transaction as the mutation it
/// audits), but all four registrations share the same unkeyed service type — ASP.NET Core's
/// container resolves a plain <c>IAuditWriter</c> request to whichever module registered
/// *last*, regardless of which module's handler is asking. Invisible before Phase 9 because
/// <c>WriteAsync</c> saved itself immediately, so it didn't matter which module's physical
/// <c>DbContext</c> connection actually sent the INSERT — <c>shared.audit_log_entry</c> is the
/// identical physical table either way. <c>Stage</c> broke that accidental tolerance: staging
/// onto the wrong module's <c>DbContext</c> instance leaves the *correct* one with a mutated
/// <see cref="IAuditableEntity"/> and no staged entry, tripping <c>AuditCompletenessInterceptor</c>
/// on every create. Fixed with keyed DI services — every command handler's <c>IAuditWriter</c>
/// constructor parameter is annotated
/// <c>[FromKeyedServices(AuditWriterKeys.ThisModule)]</c>, and each module's
/// <c>AddXPersistence</c> registers with <c>AddKeyedScoped</c> using the matching key. Plain
/// string constants, not each module's own <c>DbContext</c> type, because a module's
/// Application-layer handler (where the attribute lives) may never reference its own module's
/// Infrastructure project — see CLAUDE.md's module boundary table.
/// </summary>
public static class AuditWriterKeys
{
    public const string Reservations = "Reservations";
    public const string Tables = "Tables";
    public const string Scheduling = "Scheduling";
    public const string StaffIdentity = "StaffIdentity";
}
