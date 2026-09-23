namespace Mise.ApiService;

/// <summary>
/// US-05 AC #2 — the wire shape both <c>GET /api/reservations/{id}/audit-history</c> and
/// <c>GET /api/tables/{id}/audit-history</c> return (Phase 9). Not a per-module Contracts DTO:
/// audit is cross-cutting, not owned by Reservations or Tables (same reasoning <c>ETag</c> is a
/// shared root-level type here rather than duplicated per endpoint file).
/// </summary>
internal sealed record AuditHistoryEntryDto(
    Guid Id,
    string Action,
    Guid? PerformedByStaffId,
    string? PerformedBySystemProcess,
    DateTimeOffset OccurredAtUtc,
    string Details);
