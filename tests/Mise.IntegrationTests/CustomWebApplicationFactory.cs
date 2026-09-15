extern alias ApiService;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mise.IntegrationTests;

/// <summary>
/// Wraps Mise.ApiService for endpoint-level integration tests. No configuration override
/// yet — the API has no database dependency until Phase 2 adds the first module; this
/// gains a Postgres connection-string override (pointed at PostgresContainerFixture) the
/// day that changes.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<ApiService::Program>;
