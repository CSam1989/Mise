namespace Mise.SharedKernel.Infrastructure;

/// <summary>
/// Cross-cutting audit port every mutating command handler depends on — CrossCuttingTests'
/// <c>EveryCommandHandler_DependsOnSomethingImplementingIAuditWriter</c> enforces the
/// constructor dependency at the architecture-test tier. Phase 9 adds the
/// SaveChangesInterceptor guarantee that a handler holding this dependency actually called
/// it; Phase 2 ships a real, working per-module implementation of the call itself.
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken);
}
