using Mise.Modules.Tables.Application.CreateTable;

namespace Mise.UnitTests.Tables;

public class CreateTableCommandValidatorTests
{
    private readonly CreateTableCommandValidator _validator = new();

    private static CreateTableCommand CommandWith(
        Guid? sectionId = null, string name = "T12", int minCapacity = 2, int maxCapacity = 4) =>
        new(Guid.NewGuid(), sectionId ?? Guid.NewGuid(), name, minCapacity, maxCapacity, false, null, null, Guid.NewGuid());

    [Fact]
    public void Validate_SectionIdEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(sectionId: Guid.Empty));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateTableCommand.SectionId))
            .Which.ErrorMessage.Should().Be("SectionId is required.");
    }

    [Fact]
    public void Validate_NameEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(name: ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateTableCommand.Name))
            .Which.ErrorMessage.Should().Be("Name is required.");
    }

    [Fact]
    public void Validate_MinCapacityZero_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(minCapacity: 0));

        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTableCommand.MinCapacity)
            && e.ErrorMessage == "MinCapacity must be greater than 0.");
    }

    [Fact]
    public void Validate_MaxCapacityLessThanMinCapacity_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(minCapacity: 4, maxCapacity: 2));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateTableCommand.MaxCapacity))
            .Which.ErrorMessage.Should().Be("MaxCapacity must be greater than or equal to MinCapacity.");
    }

    [Fact]
    public void Validate_ValidCommand_HasNoValidationErrors()
    {
        var result = _validator.Validate(CommandWith());

        result.IsValid.Should().BeTrue();
    }
}
