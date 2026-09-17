using Mise.Modules.Reservations.Application.CreateReservation;

namespace Mise.UnitTests.Reservations;

public class CreateReservationCommandValidatorTests
{
    private const string PartySizeRequiredMessage = "Party size is required and must be more than 0.";
    private readonly CreateReservationCommandValidator _validator = new();

    private static CreateReservationCommand CommandWithPartySize(int partySize) =>
        new(Guid.NewGuid(), "Jane Doe", partySize, DateTimeOffset.UtcNow, Guid.NewGuid());

    [Fact]
    public void Validate_PartySizeZero_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWithPartySize(0));

        result.IsValid.Should().BeFalse(because: "0 violates the one rule this validator enforces.");
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.PartySize))
            .Which.ErrorMessage.Should().Be(PartySizeRequiredMessage,
                because: "the mockup's exact copy is what the UI shows verbatim (CLAUDE.md's validator test contract).");
    }

    [Fact]
    public void Validate_PartySizeNegative_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWithPartySize(-3));

        result.IsValid.Should().BeFalse(because: "a negative party size is as invalid as zero.");
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.PartySize))
            .Which.ErrorMessage.Should().Be(PartySizeRequiredMessage);
    }

    [Fact]
    public void Validate_PartySizePositive_HasNoValidationErrors()
    {
        var result = _validator.Validate(CommandWithPartySize(4));

        result.IsValid.Should().BeTrue(because: "a positive party size satisfies the only rule this validator enforces.");
        result.Errors.Should().BeEmpty();
    }
}
