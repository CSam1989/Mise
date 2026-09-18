using Mise.Modules.Reservations.Application.SeatReservation;

namespace Mise.UnitTests.Reservations;

public class SeatReservationCommandValidatorTests
{
    private const string TableIdRequiredMessage = "TableId is required to seat a reservation.";
    private readonly SeatReservationCommandValidator _validator = new();

    private static SeatReservationCommand ValidCommand(Guid? tableId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, tableId ?? Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void Validate_TableIdEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(tableId: Guid.Empty));

        result.IsValid.Should().BeFalse(because: "BR-04 — a reservation cannot be marked Seated without an assigned table.");
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(SeatReservationCommand.TableId))
            .Which.ErrorMessage.Should().Be(TableIdRequiredMessage,
                because: "the exact message string is what the UI shows verbatim (CLAUDE.md's validator test contract).");
    }

    [Fact]
    public void Validate_TableIdProvided_Succeeds()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }
}
