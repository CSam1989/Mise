using FluentValidation;

namespace Mise.Modules.StaffIdentity.Application.RegisterStaff;

public sealed class RegisterStaffCommandValidator : AbstractValidator<RegisterStaffCommand>
{
    public RegisterStaffCommandValidator()
    {
        RuleFor(c => c.Username)
            .NotEmpty().WithMessage("Username is required.")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters.")
            // Matches Identity's own default IdentityOptions.User.AllowedUserNameCharacters
            // exactly (StaffIdentityPersistenceServiceCollectionExtensions leaves it at that
            // default) — same reasoning as the relaxed password policy: one source of truth
            // for what's a valid username, so a disallowed character surfaces as this
            // validator's own field-scoped 400, not an opaque 500 from Identity's CreateAsync
            // rejecting it for a reason this validator never checked.
            .Matches("^[a-zA-Z0-9\\-._@+]+$").WithMessage("Username may only contain letters, digits, and the characters - . _ @ +.");

        RuleFor(c => c.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.");

        RuleFor(c => c.FullName)
            .NotEmpty().WithMessage("Full name is required.");

        RuleFor(c => c.Role)
            .IsInEnum().WithMessage("Role must be FloorStaff or Manager.");
    }
}
