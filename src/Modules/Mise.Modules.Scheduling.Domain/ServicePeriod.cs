using Mise.SharedKernel;

namespace Mise.Modules.Scheduling.Domain;

/// <summary>
/// FR-08 ("Managers define service periods/hours ... and mark days closed") maps to a single
/// entity, not two: a row with <see cref="IsClosed"/> false is a normal open window (Lunch,
/// Dinner); a row with <see cref="IsClosed"/> true is a closure — a whole day (00:00-00:00,
/// <see cref="EndsNextDay"/> true) or a partial one (e.g. "kitchen closed 14:00-17:00"). The
/// charter's own data model (§10) never gave FR-08 acceptance criteria the way US-04 got them
/// for Tables/Sections, so this shape is a resolved gap, documented in CLAUDE.md, not a charter
/// requirement transcribed verbatim.
///
/// <see cref="EndsNextDay"/> is docs/plan.md correction #9: a plain Date+StartTime+EndTime
/// can't represent "Dinner 18:00-01:00" — the service crosses midnight. Landed in this type's
/// first commit rather than retrofitted, since Scheduling is where ServicePeriod is born.
/// </summary>
public sealed class ServicePeriod : AggregateRoot<Guid>
{
    private ServicePeriod(
        Guid id, DateOnly date, string label, TimeOnly startTime, TimeOnly endTime, bool endsNextDay, bool isClosed)
        : base(id)
    {
        Date = date;
        Label = label;
        StartTime = startTime;
        EndTime = endTime;
        EndsNextDay = endsNextDay;
        IsClosed = isClosed;
    }

    public DateOnly Date { get; private set; }
    public string Label { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public bool EndsNextDay { get; private set; }
    public bool IsClosed { get; private set; }

    public static ServicePeriod Create(
        Guid id, DateOnly date, string label, TimeOnly startTime, TimeOnly endTime, bool endsNextDay, bool isClosed)
    {
        ValidateFields(label, startTime, endTime, endsNextDay);
        return new ServicePeriod(id, date, label, startTime, endTime, endsNextDay, isClosed);
    }

    public void UpdateDetails(
        DateOnly date, string label, TimeOnly startTime, TimeOnly endTime, bool endsNextDay, bool isClosed)
    {
        ValidateFields(label, startTime, endTime, endsNextDay);
        Date = date;
        Label = label;
        StartTime = startTime;
        EndTime = endTime;
        EndsNextDay = endsNextDay;
        IsClosed = isClosed;
    }

    /// <summary>Same-day window must have positive length; a next-day window is always valid
    /// (even EndTime == StartTime, which represents exactly 24 hours) because it never
    /// collapses to zero length the way a same-day window with EndTime &lt;= StartTime would.</summary>
    private static void ValidateFields(string label, TimeOnly startTime, TimeOnly endTime, bool endsNextDay)
    {
        Guard.Against.NullOrWhiteSpace(label, nameof(label));
        if (!endsNextDay && endTime <= startTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endTime), endTime, "EndTime must be after StartTime unless the period ends the next day.");
        }
    }
}
