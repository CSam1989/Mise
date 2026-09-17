using Mise.Modules.Tables.Domain;
using Mise.SharedKernel;

namespace Mise.UnitTests.Tables;

public class TableTests
{
    private static Table ValidTable() =>
        Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T12", minCapacity: 2, maxCapacity: 4, isCombinable: false, positionX: null, positionY: null);

    [Fact]
    public void Create_NameEmpty_ThrowsArgumentException()
    {
        var act = () => Table.Create(Guid.NewGuid(), Guid.NewGuid(), "  ", 2, 4, false, null, null);

        act.Should().Throw<ArgumentException>(because: "Guard.Against.NullOrWhiteSpace backstops the table name.");
    }

    [Fact]
    public void Create_SectionIdEmpty_ThrowsArgumentException()
    {
        var act = () => Table.Create(Guid.NewGuid(), Guid.Empty, "T12", 2, 4, false, null, null);

        act.Should().Throw<ArgumentException>(because: "a table must belong to a real section.");
    }

    [Fact]
    public void Create_MinCapacityZero_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T12", 0, 4, false, null, null);

        act.Should().Throw<ArgumentOutOfRangeException>(because: "a table must seat at least one guest.");
    }

    [Fact]
    public void Create_MaxCapacityLessThanMinCapacity_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T12", 4, 2, false, null, null);

        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "MaxCapacity below MinCapacity is an unrepresentable table.");
    }

    [Fact]
    public void Create_MaxCapacityEqualToMinCapacity_Succeeds()
    {
        var table = Table.Create(Guid.NewGuid(), Guid.NewGuid(), "T12", 4, 4, false, null, null);

        table.MaxCapacity.Should().Be(4, because: "MaxCapacity == MinCapacity is the boundary just past the invariant this factory enforces.");
    }

    [Fact]
    public void Create_ValidInput_StartsAvailableAndActive()
    {
        var table = ValidTable();

        table.Status.Should().Be(TableStatus.Available, because: "nothing sets a table's status away from Available until Phase 7.");
        table.IsActive.Should().BeTrue(because: "a newly created table starts active.");
    }

    [Fact]
    public void UpdateDetails_ValidInput_UpdatesFields()
    {
        var table = ValidTable();
        var newSectionId = Guid.NewGuid();

        table.UpdateDetails(newSectionId, "T13", 3, 6, isCombinable: true, positionX: 10.5, positionY: 20.5);

        table.SectionId.Should().Be(newSectionId);
        table.Name.Should().Be("T13");
        table.MinCapacity.Should().Be(3);
        table.MaxCapacity.Should().Be(6);
        table.IsCombinable.Should().BeTrue();
        table.PositionX.Should().Be(10.5);
        table.PositionY.Should().Be(20.5);
    }

    [Fact]
    public void UpdateDetails_MaxCapacityLessThanMinCapacity_ThrowsArgumentOutOfRangeException()
    {
        var table = ValidTable();

        var act = () => table.UpdateDetails(table.SectionId, table.Name, 4, 2, false, null, null);

        act.Should().Throw<ArgumentOutOfRangeException>(because: "UpdateDetails re-validates the same invariant Create enforces.");
    }

    [Fact]
    public void Deactivate_StatusAvailable_Succeeds()
    {
        var table = ValidTable();

        table.Deactivate();

        table.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(TableStatus.Reserved)]
    [InlineData(TableStatus.Occupied)]
    public void Deactivate_StatusReservedOrOccupied_ThrowsDomainRuleViolationException(TableStatus status)
    {
        var table = ValidTable();
        SetStatus(table, status);

        var act = table.Deactivate;

        act.Should().Throw<DomainRuleViolationException>(
            because: "docs/plan.md correction #13 — deactivating a table currently Reserved or Occupied is blocked with a clear reason, not a silent failure.");
        table.IsActive.Should().BeTrue(because: "a rejected deactivation must not partially apply.");
    }

    [Theory]
    [InlineData(TableStatus.NeedsCleaning)]
    [InlineData(TableStatus.Blocked)]
    public void Deactivate_StatusNeedsCleaningOrBlocked_Succeeds(TableStatus status)
    {
        var table = ValidTable();
        SetStatus(table, status);

        table.Deactivate();

        table.IsActive.Should().BeFalse(because: "only Reserved/Occupied block deactivation — every other status is fair game.");
    }

    /// <summary>No public mutator sets Status yet (Phase 7 adds one) — reflection is the only
    /// way a unit test can reach the Reserved/Occupied boundary today, which is itself
    /// evidence the guard is exercising real, if not-yet-reachable-in-production, logic.</summary>
    private static void SetStatus(Table table, TableStatus status) =>
        typeof(Table).GetProperty(nameof(Table.Status))!.SetValue(table, status);
}
