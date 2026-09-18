using Mise.Modules.Tables.Application.ChangeTableStatus;

namespace Mise.UnitTests.Tables;

public class ChangeTableStatusCommandValidatorTests
{
    private const string StatusInvalidMessage = "Status must be one of: Available, Reserved, Occupied, NeedsCleaning, Blocked.";
    private readonly ChangeTableStatusCommandValidator _validator = new();

    private static ChangeTableStatusCommand ValidCommand(string status = "Occupied") =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, status, Guid.NewGuid());

    [Fact]
    public void Validate_StatusNotARealTableStatusValue_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(status: "OnFire"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(ChangeTableStatusCommand.Status))
            .Which.ErrorMessage.Should().Be(StatusInvalidMessage,
                because: "the exact message string is what the UI shows verbatim (CLAUDE.md's validator test contract).");
    }

    [Fact]
    public void Validate_StatusEmpty_Fails()
    {
        var result = _validator.Validate(ValidCommand(status: ""));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Available")]
    [InlineData("Reserved")]
    [InlineData("Occupied")]
    [InlineData("NeedsCleaning")]
    [InlineData("Blocked")]
    public void Validate_EveryRealTableStatusValue_Succeeds(string status)
    {
        var result = _validator.Validate(ValidCommand(status));

        result.IsValid.Should().BeTrue();
    }
}
