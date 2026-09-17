using Mise.Modules.Scheduling.Application.CreateServicePeriod;

namespace Mise.UnitTests.Scheduling;

public class CreateServicePeriodCommandValidatorTests
{
    private readonly CreateServicePeriodCommandValidator _validator = new();

    private static CreateServicePeriodCommand CommandWith(
        string label = "Lunch",
        TimeOnly? startTime = null,
        TimeOnly? endTime = null,
        bool endsNextDay = false) =>
        new(
            Guid.NewGuid(), new DateOnly(2026, 9, 17), label,
            startTime ?? new TimeOnly(12, 0), endTime ?? new TimeOnly(14, 30), endsNextDay, IsClosed: false,
            Guid.NewGuid());

    [Fact]
    public void Validate_LabelEmpty_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(label: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateServicePeriodCommand.Label))
            .Which.ErrorMessage.Should().Be("Label is required.");
    }

    [Fact]
    public void Validate_LabelTooLong_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(label: new string('a', 31)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateServicePeriodCommand.Label))
            .Which.ErrorMessage.Should().Be("Label must be 30 characters or fewer.");
    }

    [Fact]
    public void Validate_SameDayEndTimeNotAfterStartTime_FailsWithExactMessage()
    {
        var result = _validator.Validate(CommandWith(startTime: new TimeOnly(14, 0), endTime: new TimeOnly(12, 0)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateServicePeriodCommand.EndTime))
            .Which.ErrorMessage.Should().Be("EndTime must be after StartTime unless the period ends the next day.");
    }

    [Fact]
    public void Validate_EndsNextDayEndTimeBeforeStartTime_HasNoValidationErrors()
    {
        var result = _validator.Validate(
            CommandWith(label: "Dinner", startTime: new TimeOnly(18, 0), endTime: new TimeOnly(1, 0), endsNextDay: true));

        result.IsValid.Should().BeTrue(because: "docs/plan.md correction #9 — a midnight-crossing period is valid when EndsNextDay is set.");
    }

    [Fact]
    public void Validate_ValidCommand_HasNoValidationErrors()
    {
        var result = _validator.Validate(CommandWith());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
