namespace Mise.SharedKernel;

/// <summary>
/// Outcome of an operation that can fail in an expected way (a business-rule violation,
/// not an exceptional condition). Application handlers return this instead of throwing for
/// anything a caller is meant to handle — see CLAUDE.md's per-feature test contract.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, string error)
    {
        if (isSuccess && error != string.Empty)
        {
            throw new ArgumentException("A successful result cannot have an error message.", nameof(error));
        }

        if (!isSuccess && error == string.Empty)
        {
            throw new ArgumentException("A failed result must have an error message.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string Error { get; }

    public static Result Success() => new(true, string.Empty);
    public static Result Failure(string error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, string.Empty);
    public static Result<TValue> Failure<TValue>(string error) => new(default, false, error);
}

/// <summary>A <see cref="Result"/> that carries a value on success.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, string error) : base(isSuccess, error) => _value = value;

    /// <summary>The success value. Throws if the result is a failure — check <see cref="Result.IsSuccess"/> first.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");
}
