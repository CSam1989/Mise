namespace Mise.SharedKernel;

/// <summary>
/// Marker for an aggregate root whose Added/Modified/Deleted state must never leave a
/// <c>SaveChangesAsync</c> call without a matching <see cref="Infrastructure.AuditLogEntry"/> in
/// the same call — enforced at the EF Core tier by
/// <c>Mise.SharedKernel.Persistence.AuditCompletenessInterceptor</c> (Phase 9, ADR-009).
/// Deliberately explicit opt-in per aggregate rather than every <see cref="AggregateRoot{TId}"/>
/// automatically qualifying — "is an aggregate root" and "requires an audit trail" are different
/// concerns that happen to coincide for every aggregate that exists today
/// (<c>Reservation</c>, <c>Table</c>, <c>Section</c>, <c>TableGroup</c>, <c>ServicePeriod</c>,
/// <c>StaffUser</c>), not a guaranteed-forever correlation.
/// </summary>
public interface IAuditableEntity;
