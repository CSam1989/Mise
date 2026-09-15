namespace Mise.UnitTests.SharedKernel;

public class GuardTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AgainstNegativeOrZero_NonPositiveValue_Throws(int value)
    {
        var act = () => Guard.Against.NegativeOrZero(value, "partySize");

        act.Should().Throw<ArgumentOutOfRangeException>(
            because: "party size (and every other count this guard protects) must be strictly positive.");
    }

    [Fact]
    public void AgainstNegativeOrZero_PositiveValue_ReturnsIt()
    {
        Guard.Against.NegativeOrZero(4, "partySize").Should().Be(4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AgainstNullOrWhiteSpace_BlankValue_Throws(string? value)
    {
        var act = () => Guard.Against.NullOrWhiteSpace(value, "customerName");

        act.Should().Throw<ArgumentException>(because: "a blank customer name is not a valid customer name.");
    }
}
