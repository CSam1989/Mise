using Mise.Modules.Scheduling.Application.UpdateServicePeriod;

namespace Mise.UnitTests.Scheduling;

public class UpdateServicePeriodCommandValidatorTests
{
    private readonly UpdateServicePeriodCommandValidator _validator = new();

    private static UpdateServicePeriodCommand CommandWith(
        string label = "Lunch",
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        bool endsNextDay = false) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 17), label,
            startTime ?? new TimeOnly(12, 0), endTime ?? new TimeOnly(14, 30), endsNextDay, IsClosed: false,
            Guid.NewGuid());

    [Fact]
    public void Validate_LabelEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(label: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(UpdateServicePeriodCommand.Label))
            .Which.ErrorMessage.Should().Be("Label is required.");
    }

    [Fact]
    public void Validate_SameDayEndTimeNotAfterStartTime_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(startTime: new TimeOnly(14, 0), endTime: new TimeOnly(12, 0)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(UpdateServicePeriodCommand.EndTime))
            .Which.ErrorMessage.Should().Be("EndTime must be after StartTime unless the period ends the next day.");
    }

    [Fact]
    public void Validate_ValidCommand_HasNoValidationErrors()
    {
        var result = _validator.Validate(CommandWith());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
