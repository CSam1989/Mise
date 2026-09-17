using Mise.Modules.StaffIdentity.Application.Login;

namespace Mise.UnitTests.StaffIdentity;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_UsernameEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(new LoginCommand("", "correct-horse"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(LoginCommand.Username))
            .Which.ErrorMessage.Should().Be("Username is required.");
    }

    [Fact]
    public void Validate_PasswordEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(new LoginCommand("jdoe", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(LoginCommand.Password))
            .Which.ErrorMessage.Should().Be("Password is required.");
    }

    [Fact]
    public void Validate_BothFieldsPresent_HasNoValidationErrors()
    {
        var result = _validator.Validate(new LoginCommand("jdoe", "correct-horse"));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
