namespace Mise.SharedKernel;

/// <summary>
/// A business-rule violation that's self-contained within a single aggregate's own state —
/// as opposed to FluentValidation's <c>ValidationException</c> (input shape) or a
/// cross-aggregate, database-dependent check (handled as a field-scoped
/// <c>ValidationException</c> thrown from the Application handler instead — e.g.
/// RegisterStaffCommandHandler's taken-username case, which needs a query the aggregate
/// itself has no way to answer). <c>Table.Deactivate()</c> (Phase 4) is the first user: the
/// guard against deactivating an Occupied/Reserved table only reads the aggregate's own
/// Status field, so it belongs in the Domain, not the handler. Mapped to a 409 Conflict by
/// Mise.ApiService's DomainRuleViolationExceptionHandler — see CLAUDE.md's Error handling
/// section for why a new expected-outcome exception type gets its own handler instead of
/// growing the GlobalExceptionHandler catch-all.
/// </summary>
public sealed class DomainRuleViolationException(string message) : Exception(message);
