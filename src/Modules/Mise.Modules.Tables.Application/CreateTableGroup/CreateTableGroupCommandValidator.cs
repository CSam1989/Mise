using FluentValidation;

namespace Mise.Modules.Tables.Application.CreateTableGroup;

public sealed class CreateTableGroupCommandValidator : AbstractValidator<CreateTableGroupCommand>
{
    public CreateTableGroupCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().WithMessage("Name is required.")
            .MaximumLength(50).WithMessage("Name must be 50 characters or fewer.");
        RuleFor(c => c.TableIds).Must(ids => ids.Distinct().Count() >= 2)
            .WithMessage("TableIds must name at least two distinct tables.");
    }
}
