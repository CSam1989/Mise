using Mise.Modules.Tables.Application.CreateSection;

namespace Mise.UnitTests.Tables;

public class CreateSectionCommandValidatorTests
{
    private readonly CreateSectionCommandValidator _validator = new();

    private static CreateSectionCommand CommandWith(string name = "Patio", int displayOrder = 0) =>
        new(Guid.NewGuid(), name, displayOrder, Guid.NewGuid());

    [Fact]
    public void Validate_NameEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(name: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateSectionCommand.Name))
            .Which.ErrorMessage.Should().Be("Name is required.");
    }

    [Fact]
    public void Validate_NameTooLong_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(name: new string('a', 51)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateSectionCommand.Name))
            .Which.ErrorMessage.Should().Be("Name must be 50 characters or fewer.");
    }

    [Fact]
    public void Validate_DisplayOrderNegative_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(displayOrder: -1));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateSectionCommand.DisplayOrder))
            .Which.ErrorMessage.Should().Be("DisplayOrder must not be negative.");
    }

    [Fact]
    public void Validate_ValidCommand_HasNoValidationErrors()
    {
        var result = _validator.Validate(CommandWith());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
