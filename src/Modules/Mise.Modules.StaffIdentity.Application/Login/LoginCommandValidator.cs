using FluentValidation;

namespace Mise.Modules.StaffIdentity.Application.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.Username).NotEmpty().WithMessage("Username is required.");
        RuleFor(c => c.Password).NotEmpty().WithMessage("Password is required.");
    }
}
