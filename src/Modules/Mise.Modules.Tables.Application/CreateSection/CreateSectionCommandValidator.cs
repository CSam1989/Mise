using FluentValidation;

namespace Mise.Modules.Tables.Application.CreateSection;

public sealed class CreateSectionCommandValidator : AbstractValidator<CreateSectionCommand>
{
    public CreateSectionCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.")
            .MaximumLength(50).WithMessage("Name must be 50 characters or fewer.");
        RuleFor(c => c.DisplayOrder).GreaterThanOrEqualTo(0).WithMessage("DisplayOrder must not be negative.");
    }
}
