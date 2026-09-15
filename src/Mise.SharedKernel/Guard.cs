namespace Mise.SharedKernel;

/// <summary>Guard clauses for enforcing Domain invariants at the boundary of a factory method.</summary>
public static class Guard
{
    public static class Against
    {
        public static string NullOrWhiteSpace(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{parameterName} must not be null or whitespace.", parameterName);
            }

            return value;
        }

        public static int NegativeOrZero(int value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be greater than 0.");
            }

            return value;
        }

        public static T Null<T>(T? value, string parameterName) where T : class =>
            value ?? throw new ArgumentNullException(parameterName);
    }
}
