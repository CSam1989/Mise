using FluentValidation;

namespace Mise.Modules.Reservations.Application.SeatReservation;

/// <summary>BR-04's structural half ("cannot be marked Seated without an assigned table") — the
/// request-shape check; <see cref="Domain.Reservation.MarkSeated"/> re-asserts the same
/// non-empty guard at the Domain boundary as defense in depth.</summary>
public sealed class SeatReservationCommandValidator : AbstractValidator<SeatReservationCommand>
{
    public SeatReservationCommandValidator()
    {
        RuleFor(c => c.TableId).NotEmpty().WithMessage("TableId is required to seat a reservation.");
    }
}
