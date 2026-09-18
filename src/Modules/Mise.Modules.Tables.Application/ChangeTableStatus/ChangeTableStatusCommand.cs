namespace Mise.Modules.Tables.Application.ChangeTableStatus;

/// <summary>FR-06's "change a table's status directly ... independent of a reservation."
/// <see cref="Status"/> travels as a string (matching every DTO's own <c>Status.ToString()</c>
/// wire shape elsewhere in this codebase) so an invalid value is a field-scoped 400 with a
/// controlled, exact message (the validator test-contract row) rather than depending on ASP.NET
/// Core's default JSON enum-binding behavior.</summary>
public sealed record ChangeTableStatusCommand(
    Guid OperationId, Guid TableId, uint ExpectedVersion, string Status, Guid PerformedByStaffId);
