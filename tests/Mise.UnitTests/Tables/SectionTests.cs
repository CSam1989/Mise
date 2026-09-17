using Mise.Modules.Tables.Domain;

namespace Mise.UnitTests.Tables;

public class SectionTests
{
    [Fact]
    public void Create_NameEmpty_ThrowsArgumentException()
    {
        var act = () => Section.Create(Guid.NewGuid(), "   ", 0);

        act.Should().Throw<ArgumentException>(because: "Guard.Against.NullOrWhiteSpace backstops the section name.");
    }

    [Fact]
    public void Create_DisplayOrderNegative_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Section.Create(Guid.NewGuid(), "Patio", -1);

        act.Should().Throw<ArgumentOutOfRangeException>(because: "a negative DisplayOrder has no meaning.");
    }

    [Fact]
    public void Create_DisplayOrderZero_Succeeds()
    {
        var section = Section.Create(Guid.NewGuid(), "Patio", 0);

        section.DisplayOrder.Should().Be(0, because: "0 is the smallest valid DisplayOrder — the boundary just past the invariant.");
        section.IsActive.Should().BeTrue(because: "a newly created section starts active.");
    }

    [Fact]
    public void UpdateDetails_ValidInput_UpdatesNameAndDisplayOrder()
    {
        var section = Section.Create(Guid.NewGuid(), "Patio", 0);

        section.UpdateDetails("Main Room", 2);

        section.Name.Should().Be("Main Room");
        section.DisplayOrder.Should().Be(2);
    }

    [Fact]
    public void UpdateDetails_NameEmpty_ThrowsArgumentException()
    {
        var section = Section.Create(Guid.NewGuid(), "Patio", 0);

        var act = () => section.UpdateDetails("", 1);

        act.Should().Throw<ArgumentException>(because: "UpdateDetails re-validates the same invariant Create enforces.");
    }

    [Fact]
    public void Deactivate_ActiveSection_SetsIsActiveFalse()
    {
        var section = Section.Create(Guid.NewGuid(), "Patio", 0);

        section.Deactivate();

        section.IsActive.Should().BeFalse(
            because: "Deactivate is unconditional at the Domain level — the \"still has active tables\" guard lives in the Application handler instead.");
    }
}
