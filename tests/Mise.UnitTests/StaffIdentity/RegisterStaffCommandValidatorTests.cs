using Mise.Modules.StaffIdentity.Application.RegisterStaff;
using Mise.Modules.StaffIdentity.Domain;

namespace Mise.UnitTests.StaffIdentity;

public class RegisterStaffCommandValidatorTests
{
    private readonly RegisterStaffCommandValidator _validator = new();

    private static RegisterStaffCommand ValidCommand(
        string username = "jdoe", string password = "correct-horse", string fullName = "Jane Doe", StaffRole role = StaffRole.FloorStaff) =>
        new(Guid.NewGuid(), username, password, fullName, role, Guid.NewGuid());

    [Fact]
    public void Validate_UsernameEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(username: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterStaffCommand.Username) && e.ErrorMessage == "Username is required.");
    }

    [Fact]
    public void Validate_UsernameTooShort_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(username: "ab"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RegisterStaffCommand.Username))
            .Which.ErrorMessage.Should().Be("Username must be at least 3 characters.");
    }

    [Fact]
    public void Validate_UsernameHasDisallowedCharacter_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(username: "jane doe"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RegisterStaffCommand.Username))
            .Which.ErrorMessage.Should().Be("Username may only contain letters, digits, and the characters - . _ @ +.",
                because: "a space (and anything else outside Identity's own AllowedUserNameCharacters) must be caught here, as an ordinary 400 — not surface as a 500 from Identity's CreateAsync rejecting it for a reason this validator never checked.");
    }

    [Fact]
    public void Validate_UsernameHasOnlyAllowedCharacters_HasNoValidationErrors()
    {
        var result = _validator.Validate(ValidCommand(username: "jane.doe-2@site+1"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PasswordEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(password: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterStaffCommand.Password) && e.ErrorMessage == "Password is required.");
    }

    [Fact]
    public void Validate_PasswordTooShort_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(password: "short1"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RegisterStaffCommand.Password))
            .Which.ErrorMessage.Should().Be("Password must be at least 8 characters.");
    }

    [Fact]
    public void Validate_FullNameEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(fullName: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RegisterStaffCommand.FullName))
            .Which.ErrorMessage.Should().Be("Full name is required.");
    }

    [Fact]
    public void Validate_RoleOutOfRange_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand() with { Role = (StaffRole)99 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RegisterStaffCommand.Role))
            .Which.ErrorMessage.Should().Be("Role must be FloorStaff or Manager.");
    }

    [Fact]
    public void Validate_AllFieldsValid_HasNoValidationErrors()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
