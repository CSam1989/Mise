using FluentValidation;

namespace Mise.Modules.Scheduling.Application.CreateServicePeriod;

public sealed class CreateServicePeriodCommandValidator : AbstractValidator<CreateServicePeriodCommand>
{
    public CreateServicePeriodCommandValidator()
    {
        RuleFor(c => c.Label).NotEmpty().WithMessage("Label is required.")
            .MaximumLength(30).WithMessage("Label must be 30 characters or fewer.");
        RuleFor(c => c.EndTime)
            .Must((command, endTime) => command.EndsNextDay || endTime > command.StartTime)
            .WithMessage("EndTime must be after StartTime unless the period ends the next day.");
    }
}
