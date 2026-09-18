using Mise.Modules.Tables.Domain;

namespace Mise.UnitTests.Tables;

public class TableGroupTests
{
    [Fact]
    public void Create_TwoDistinctTableIds_Succeeds()
    {
        var tableIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var group = TableGroup.Create(Guid.NewGuid(), "T1+T2", tableIds);

        group.TableIds.Should().BeEquivalentTo(tableIds);
        group.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_OneTableId_ThrowsArgumentException()
    {
        var act = () => TableGroup.Create(Guid.NewGuid(), "Solo", [Guid.NewGuid()]);

        act.Should().Throw<ArgumentException>(because: "a 'group' of one table is not a group.");
    }

    [Fact]
    public void Create_DuplicateTableIdsCollapseToOneDistinctId_ThrowsArgumentException()
    {
        var tableId = Guid.NewGuid();

        var act = () => TableGroup.Create(Guid.NewGuid(), "Duplicate", [tableId, tableId]);

        act.Should().Throw<ArgumentException>(because: "the same table listed twice is still only one distinct table.");
    }

    [Fact]
    public void Create_NameEmpty_ThrowsArgumentException()
    {
        var act = () => TableGroup.Create(Guid.NewGuid(), "   ", [Guid.NewGuid(), Guid.NewGuid()]);

        act.Should().Throw<ArgumentException>();
    }
}
