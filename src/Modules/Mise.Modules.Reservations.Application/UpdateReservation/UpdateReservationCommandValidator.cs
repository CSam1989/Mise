using FluentValidation;

namespace Mise.Modules.Reservations.Application.UpdateReservation;

public sealed class UpdateReservationCommandValidator : AbstractValidator<UpdateReservationCommand>
{
    public UpdateReservationCommandValidator()
    {
        RuleFor(c => c.CustomerName).NotEmpty().WithMessage("CustomerName is required.");

        RuleFor(c => c.CustomerPhone).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("CustomerPhone is required.")
            .Matches(@"^[0-9+()\-\s]{6,20}$").WithMessage("CustomerPhone must be a valid phone number.");

        RuleFor(c => c.CustomerEmail).EmailAddress().WithMessage("CustomerEmail must be a valid email address.")
            .When(c => !string.IsNullOrWhiteSpace(c.CustomerEmail));

        RuleFor(c => c.PartySize).GreaterThan(0).WithMessage("Party size is required and must be more than 0.");

        RuleFor(c => c.DurationMinutes).GreaterThan(0).WithMessage("DurationMinutes must be greater than 0.");
    }
}
