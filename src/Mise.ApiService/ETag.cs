namespace Mise.ApiService;

/// <summary>
/// Wire-format helper for docs/plan.md correction #5's optimistic-concurrency contract: the
/// caller's <c>If-Match</c> header and the server's <c>ETag</c> response header both carry the
/// Postgres <c>xmin</c> value as a quoted decimal string (HTTP's own ETag quoting rule) —
/// Application/Domain never see this formatting, only the raw <see cref="uint"/>.
/// </summary>
internal static class ETag
{
    public static string Format(uint version) => $"\"{version}\"";

    public static bool TryParse(string? headerValue, out uint version)
    {
        version = default;
        return !string.IsNullOrWhiteSpace(headerValue) && uint.TryParse(headerValue.Trim().Trim('"'), out version);
    }
}
