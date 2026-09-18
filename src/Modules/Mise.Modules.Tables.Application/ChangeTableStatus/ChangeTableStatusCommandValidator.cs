using FluentValidation;
using Mise.Modules.Tables.Domain;

namespace Mise.Modules.Tables.Application.ChangeTableStatus;

public sealed class ChangeTableStatusCommandValidator : AbstractValidator<ChangeTableStatusCommand>
{
    public ChangeTableStatusCommandValidator()
    {
        RuleFor(c => c.Status)
            .Must(s => Enum.TryParse<TableStatus>(s, out _))
            .WithMessage("Status must be one of: Available, Reserved, Occupied, NeedsCleaning, Blocked.");
    }
}
