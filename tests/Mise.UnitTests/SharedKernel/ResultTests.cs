namespace Mise.UnitTests.SharedKernel;

public class ResultTests
{
    [Fact]
    public void Success_ReturnsResultWithNoError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue(because: "Success() must construct a passing result.");
        result.Error.Should().BeEmpty(because: "a successful result carries no error message.");
    }

    [Fact]
    public void Failure_ReturnsResultWithGivenError()
    {
        var result = Result.Failure("party size must be greater than 0");

        result.IsFailure.Should().BeTrue(because: "Failure() must construct a failing result.");
        result.Error.Should().Be("party size must be greater than 0");
    }

    [Fact]
    public void FailureWithValue_ReturnsResultWhoseValueThrowsOnAccess()
    {
        var result = Result.Failure<int>("not found");

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>(
            because: "reading Value on a failed Result<T> is a caller bug — check IsSuccess first.");
    }

    [Fact]
    public void SuccessWithValue_ExposesTheValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }
}
