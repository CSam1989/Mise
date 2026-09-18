using Mise.Modules.Reservations.Application.CreateReservation;

namespace Mise.UnitTests.Reservations;

public class CreateReservationCommandValidatorTests
{
    private const string PartySizeRequiredMessage = "Party size is required and must be more than 0.";
    private readonly CreateReservationCommandValidator _validator = new();

    private static CreateReservationCommand ValidCommand(
        int partySize = 4, string customerName = "Jane Doe", string customerPhone = "+32 470 00 00 00",
        string? customerEmail = null, int? durationMinutes = null) =>
        new(Guid.NewGuid(), customerName, customerPhone, customerEmail, partySize,
            DateTimeOffset.UtcNow.AddDays(1), durationMinutes, null, null, Guid.NewGuid());

    [Fact]
    public void Validate_PartySizeZero_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(partySize: 0));

        result.IsValid.Should().BeFalse(because: "0 violates BR-01/BR-07's precondition that a party actually exists.");
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.PartySize))
            .Which.ErrorMessage.Should().Be(PartySizeRequiredMessage,
                because: "the mockup's exact copy is what the UI shows verbatim (CLAUDE.md's validator test contract).");
    }

    [Fact]
    public void Validate_PartySizeNegative_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(partySize: -3));

        result.IsValid.Should().BeFalse(because: "a negative party size is as invalid as zero.");
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.PartySize))
            .Which.ErrorMessage.Should().Be(PartySizeRequiredMessage);
    }

    [Fact]
    public void Validate_CustomerNameEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(customerName: ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.CustomerName))
            .Which.ErrorMessage.Should().Be("CustomerName is required.");
    }

    [Fact]
    public void Validate_CustomerPhoneEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(customerPhone: ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.CustomerPhone))
            .Which.ErrorMessage.Should().Be("CustomerPhone is required.");
    }

    [Fact]
    public void Validate_CustomerPhoneMalformed_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(customerPhone: "not-a-phone!!"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.CustomerPhone))
            .Which.ErrorMessage.Should().Be("CustomerPhone must be a valid phone number.");
    }

    [Fact]
    public void Validate_CustomerEmailMalformed_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(customerEmail: "not-an-email"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.CustomerEmail))
            .Which.ErrorMessage.Should().Be("CustomerEmail must be a valid email address.");
    }

    [Fact]
    public void Validate_CustomerEmailAbsent_HasNoValidationErrors()
    {
        var result = _validator.Validate(ValidCommand(customerEmail: null));

        result.IsValid.Should().BeTrue(because: "CustomerEmail is optional.");
    }

    [Fact]
    public void Validate_DurationMinutesZero_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(durationMinutes: 0));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.DurationMinutes))
            .Which.ErrorMessage.Should().Be("DurationMinutes must be greater than 0.");
    }

    [Fact]
    public void Validate_DurationMinutesAbsent_HasNoValidationErrors()
    {
        var result = _validator.Validate(ValidCommand(durationMinutes: null));

        result.IsValid.Should().BeTrue(because: "a null DurationMinutes falls back to the configured default (Decision #7).");
    }

    [Fact]
    public void Validate_AllFieldsValid_HasNoValidationErrors()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
