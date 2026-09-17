using System.Net.Http.Json;
using System.Text.Json;

namespace Mise.IntegrationTests;

/// <summary>
/// GlobalExceptionHandler is internal to Mise.ApiService (same convention as
/// ValidationExceptionHandler), so it can't be unit-tested by direct construction from this
/// project — this drives a real, if deliberately engineered, invariant violation through the
/// actual login endpoint instead: a staff_identity.staff_user row deleted out from under an
/// otherwise-valid Identity user, which is exactly the state StaffIdentityData's atomic
/// creation is supposed to make impossible. ValidateCredentialsAsync's SingleAsync then throws
/// InvalidOperationException — a real, unhandled exception, not manufactured test-only code.
/// </summary>
[Trait("Category", "Integration")]
[Collection(MiseApiCollection.Name)]
public class GlobalExceptionHandlerTests(MiseApiFixture fixture)
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnhandledException_Returns500WithGenericProblemDetailsAndNeverLeaksTheExceptionMessage()
    {
        var staff = await StaffRegistrationSteps.RegisterFreshFloorStaffAsync(fixture, CT);

        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync(CT);
            await using var command = connection.CreateCommand();
            command.CommandText = "delete from staff_identity.staff_user where id = @id";
            command.Parameters.AddWithValue("id", staff.StaffUserId);
            await command.ExecuteNonQueryAsync(CT);
        }

        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { staff.Username, staff.Password }, CT);
        var rawBody = await response.Content.ReadAsStringAsync(CT);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            because: "an invariant violation (Identity credentials with no matching profile row) is a genuine bug, not an expected outcome — GlobalExceptionHandler is the safety net for exactly this.");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = JsonDocument.Parse(rawBody);
        json.RootElement.GetProperty("title").GetString().Should().Be("An unexpected error occurred.");
        json.RootElement.TryGetProperty("traceId", out _).Should().BeTrue(
            because: "a trace id is what lets a bug report be matched back to the logged exception.");

        rawBody.Should().NotContain("InvalidOperationException").And.NotContain("Sequence contains no elements",
            because: "the exception type and message must never leak into the response body.");
    }
}
