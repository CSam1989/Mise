namespace Mise.ApiService;

/// <summary>
/// Thrown directly by a Tables endpoint (not a handler) when a mutating request on a
/// concurrency-tracked resource arrives without a usable <c>If-Match</c> header — purely an
/// HTTP-shape concern, so unlike <c>ConcurrencyConflictException</c> it never needs to cross
/// out of Mise.ApiService (docs/plan.md correction #5's "missing header → 428").
/// </summary>
internal sealed class PreconditionRequiredException(string message) : Exception(message);
