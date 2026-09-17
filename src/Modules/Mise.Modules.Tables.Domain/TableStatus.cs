namespace Mise.Modules.Tables.Domain;

/// <summary>
/// Charter §10's five values. Nothing moves a table away from <see cref="Available"/> until
/// Phase 7's cross-module Seat/status-change handling lands — today the only thing that reads
/// this field is <see cref="Table.Deactivate"/>'s own guard (docs/plan.md correction #13).
/// </summary>
public enum TableStatus
{
    Available,
    Reserved,
    Occupied,
    NeedsCleaning,
    Blocked,
}
