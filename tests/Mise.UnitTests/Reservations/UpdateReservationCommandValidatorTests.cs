using Mise.Modules.Reservations.Application.UpdateReservation;

namespace Mise.UnitTests.Reservations;

public class UpdateReservationCommandValidatorTests
{
    private readonly UpdateReservationCommandValidator _validator = new();

    private static UpdateReservationCommand ValidCommand(
        int partySize = 4, string customerName = "Jane Doe", string customerPhone = "+32 470 00 00 00",
        int durationMinutes = 90) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, customerName, customerPhone, null, partySize,
            DateTimeOffset.UtcNow.AddDays(1), durationMinutes, null, null, Guid.NewGuid());

    [Fact]
    public void Validate_PartySizeZero_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(partySize: 0));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(UpdateReservationCommand.PartySize))
            .Which.ErrorMessage.Should().Be("Party size is required and must be more than 0.");
    }

    [Fact]
    public void Validate_CustomerPhoneEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(customerPhone: ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(UpdateReservationCommand.CustomerPhone))
            .Which.ErrorMessage.Should().Be("CustomerPhone is required.");
    }

    [Fact]
    public void Validate_DurationMinutesZero_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(durationMinutes: 0));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(UpdateReservationCommand.DurationMinutes))
            .Which.ErrorMessage.Should().Be("DurationMinutes must be greater than 0.",
                because: "unlike Create, Update's DurationMinutes is never nullable — no default to fall back to.");
    }

    [Fact]
    public void Validate_AllFieldsValid_HasNoValidationErrors()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }
}
