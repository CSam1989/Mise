using FluentValidation;

namespace Mise.Modules.Tables.Application.CreateTable;

public sealed class CreateTableCommandValidator : AbstractValidator<CreateTableCommand>
{
    public CreateTableCommandValidator()
    {
        RuleFor(c => c.SectionId).NotEmpty().WithMessage("SectionId is required.");
        RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.")
            .MaximumLength(50).WithMessage("Name must be 50 characters or fewer.");
        RuleFor(c => c.MinCapacity).GreaterThan(0).WithMessage("MinCapacity must be greater than 0.");
        RuleFor(c => c.MaxCapacity).GreaterThanOrEqualTo(c => c.MinCapacity)
            .WithMessage("MaxCapacity must be greater than or equal to MinCapacity.");
    }
}
