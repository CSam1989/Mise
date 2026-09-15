using FluentValidation;

namespace Mise.Modules.Reservations.Application.CreateReservation;

public sealed class CreateReservationCommandValidator : AbstractValidator<CreateReservationCommand>
{
    public CreateReservationCommandValidator()
    {
        RuleFor(c => c.PartySize)
            .GreaterThan(0)
            .WithMessage("Party size is required and must be more than 0.");
    }
}
