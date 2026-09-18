using Mise.Modules.Tables.Application.CreateTableGroup;

namespace Mise.UnitTests.Tables;

public class CreateTableGroupCommandValidatorTests
{
    private readonly CreateTableGroupCommandValidator _validator = new();

    private static CreateTableGroupCommand ValidCommand(string name = "T1+T2", IReadOnlyList<Guid>? tableIds = null) =>
        new(Guid.NewGuid(), name, tableIds ?? [Guid.NewGuid(), Guid.NewGuid()], Guid.NewGuid());

    [Fact]
    public void Validate_NameEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(name: ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateTableGroupCommand.Name))
            .Which.ErrorMessage.Should().Be("Name is required.");
    }

    [Fact]
    public void Validate_OneTableId_FailsWithExactMessage()
    {
        var result = _validator.Validate(ValidCommand(tableIds: [Guid.NewGuid()]));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateTableGroupCommand.TableIds))
            .Which.ErrorMessage.Should().Be("TableIds must name at least two distinct tables.");
    }

    [Fact]
    public void Validate_ValidCommand_HasNoValidationErrors()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }
}
