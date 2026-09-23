namespace Mise.SharedKernel.Persistence;

/// <summary>
/// Thrown by <see cref="AuditCompletenessInterceptor"/> when a <c>SaveChangesAsync</c> call
/// would persist an Added/Modified/Deleted <see cref="Infrastructure.IAuditableEntity"/> with
/// no matching <see cref="Infrastructure.AuditLogEntry"/> staged in that same call. This is a
/// programmer error (a new handler forgot to call <c>IAuditWriter.Stage</c>, or staged it after
/// the mutating call instead of before), never an expected business outcome — deliberately left
/// unmapped so it falls through to <c>Mise.ApiService</c>'s <c>GlobalExceptionHandler</c> as a
/// generic 500, the same way any other unanticipated bug does (CLAUDE.md's Error handling
/// section: audit failures must propagate, never be swallowed).
/// </summary>
public sealed class AuditCompletenessViolationException(string message) : Exception(message);
