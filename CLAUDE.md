# CLAUDE.md — Mise

Rules for working in this repository. Read this before writing code here, not just once.
Product spec: [docs/Restaurant-Reservations-Project-Charter.md](docs/Restaurant-Reservations-Project-Charter.md).

## What this project is

A staff-only restaurant reservations + live floor-plan tool. Modular monolith, Clean
Architecture per module, PostgreSQL, ASP.NET Core + Blazor Server + .NET MAUI Blazor Hybrid
sharing one Razor Class Library, offline-first MAUI sync. Full architecture rationale lives in
the charter's ADR-001 (stack) and ADR-002 (offline sync); this repo additionally carries
**ADR-004** (the Blazor Server website is an API client over HTTP, exactly like MAUI — not
in-process) and **ADR-005** (no MediatR or other mediator library — see below).

## Non-negotiable rules

- **Every new feature, endpoint, component, or business-rule change ships with tests across
  every relevant tier.** Not optional. See the per-feature test contract below.
- **`TreatWarningsAsErrors` is on.** A warning fails the build, locally and in CI, the same way.
- **Central Package Management is on** (`Directory.Packages.props`). Add package *versions*
  there; a `.csproj`'s `PackageReference` never carries a `Version` attribute.
- **No `DateTime.Now` / `DateTime.UtcNow` / `DateTimeOffset.Now` anywhere outside a host's
  composition root.** Inject `TimeProvider`; tests use `FakeTimeProvider`. This project is
  saturated with time-dependent rules (reservation overlap windows, the "upcoming within 30
  minutes" flag, the offline grace period, retention expiry) and every one of them is
  untestable if the code reads the wall clock directly.
- **No `Thread.Sleep` / `Task.Delay` in any test project.** Wait on a `TaskCompletionSource`,
  use `FakeTimeProvider.Advance`, or a framework's own wait primitive (bUnit
  `WaitForAssertion`, Playwright `Expect(...)`).

## Module boundaries (enforced by `Mise.ArchitectureTests`, not just this doc)

Each of the four modules (Reservations, Tables, StaffIdentity, Scheduling) is four projects:
`{Module}.Domain`, `{Module}.Application`, `{Module}.Infrastructure`, `{Module}.Contracts`.

| From | May reference | Must never reference |
|---|---|---|
| `{M}.Domain` | `Mise.SharedKernel` only | Anything else — no EF, no ASP.NET, no other module |
| `{M}.Application` | own `.Domain`, `Mise.SharedKernel`, **other modules' `.Contracts`** | any other module's `.Domain`/`.Application`/`.Infrastructure`; EF Core; ASP.NET Core |
| `{M}.Infrastructure` | own `.Domain` + `.Application`, `Mise.SharedKernel*` | another module's anything |
| `{M}.Contracts` | `Mise.SharedKernel` only | its own module's `.Domain`/`.Application`/`.Infrastructure` |
| `Mise.UI.Components` | `Mise.UI.Abstractions` only | any module, any host project — this is what lets the same components render under both Blazor Server and MAUI |
| `Mise.Web` / the MAUI head | `Mise.UI.Components`, `Mise.UI.Abstractions` | any module project directly |

A new module gets registered in the architecture-test assembly registry in the same commit —
a rule fails if the registry and the host's real project references disagree, so a module can't
silently escape every other boundary rule.

## The testable seam

Command handlers depend on a **slice-specific gateway interface** (`IReservationsData`,
`ITablesData`, …) declared in `{M}.Application/Ports/`, implemented in
`{M}.Infrastructure/Persistence/`. This is **not** a generic `IRepository<T>` and **not** an
`IUnitOfWork` — those are anti-patterns here: `DbSet<T>` is already the abstraction, a generic
repository just gets mocked without ever exercising real persistence, and it pulls people
toward the EF InMemory provider, which cannot represent the things this project's correctness
depends on (a Postgres exclusion constraint, `xmin` concurrency, `pg_trgm` search).

- **Command handlers** mock the gateway with Moq. Fast, no database, no HTTP.
- **Query handlers and gateway implementations** are tested only against a real PostgreSQL
  (Testcontainers). Never `UseInMemoryDatabase` — an architecture rule forbids it in test code.

## Cross-cutting infrastructure (`Mise.SharedKernel.Infrastructure`)

`IAuditWriter` (and the `AuditLogEntry` shape it writes) lives here, not in any one module —
every mutating command handler takes it as a constructor dependency, enforced at the
architecture-test tier (`CrossCuttingTests.EveryCommandHandler_DependsOnSomethingImplementingIAuditWriter`,
matched by parameter-type-name substring so it works before the type is even loaded). Phase 9
adds the `SaveChangesInterceptor` guarantee that a handler holding the dependency actually
*called* it; until then, calling it is on the handler author, same as every other rule not yet
backed by a runtime check.

`shared.audit_log_entry` and `shared.processed_operation` (the `OperationId` idempotency
table — see the per-feature test contract's mutating-endpoint row) are **module-internal EF
mappings today**, owned by whichever module's `Infrastructure` happens to need them first
(Reservations, as of Phase 2) — not a shared DbContext or a reusable `IEntityTypeConfiguration`.
The second module that needs either table is what decides whether to extract a shared
implementation; don't build that abstraction speculatively before a second caller exists.

## Placeholder auth spine (Phase 2 — replaced by StaffIdentity in Phase 3)

`Mise.ApiService` runs a minimal JWT-bearer scheme (`Mise.ServiceDefaults.PlaceholderAuthDefaults`
for the issuer/audience/config-key constants) with a global fallback policy that requires
authentication by default — endpoints opt out with `AllowAnonymous` (`/health`, `/alive`) rather
than opting in one by one. There is no login, no StaffIdentity, no roles yet: `Mise.Web` mints
its own fixed system-identity token per outgoing call (`PlaceholderAuthTokenHandler`), so every
request from the website authenticates as "Mise.Web," not a real staff member — that name is
what ends up in `CreateReservationCommand.PerformedBy` and, from there, `AuditLogEntry.PerformedBySystemProcess`.
The signing key is a secret Aspire parameter (`jwt-signing-key`, set once via
`dotnet user-secrets set` from `src/Mise.AppHost` — see the README) shared by both hosts, never
hardcoded. When StaffIdentity lands, it replaces the token-minting side of this (real sign-in,
real per-staff claims) without needing to touch the validation side already wired here.

## No mediator library (ADR-005)

Cross-module domain events go through a ~40-line hand-rolled `IDomainEventPublisher` /
`IDomainEventHandler<T>` in `Mise.SharedKernel(.Infrastructure)`, not MediatR (which went
commercial-license after v12) or any other package. Handlers are registered directly in DI and
called directly — no pipeline behaviors, no reflection-based discovery.

## Logging

Structured logging is how this project gets debugged in production — there's no other
window into a running `Mise.ApiService`/`Mise.MigrationService` once it's deployed. This is a
project-wide standard, not a per-module choice.

- **`ILogger<T>` via constructor injection only** — never `Console.WriteLine`, never
  `Debug.WriteLine`/`Debug.Print`, never a static/ambient logger. Banned outright by an
  architecture rule (`CrossCuttingTests` via `IlCallSiteScanner`, same mechanism as the
  `DateTime.Now` ban) because a stray debug print is invisible until someone greps for it in
  production and finds nothing, since it never went to the log pipeline at all.
- **Use `[LoggerMessage]` source-generated partial methods**, not inline `_logger.LogX(...)`
  calls, for anything logged from a command handler, gateway, or hot path. Source generation
  avoids the allocation/boxing cost of a disabled log level actually being evaluated, and
  gives every log line a stable `EventId` worth querying on in production. One-off logging in
  a composition root's own `Program.cs` (startup diagnostics) can use the instance methods
  directly — it runs once, performance doesn't matter there.
- **Domain projects never log** — zero packages, same as the rest of Domain's isolation
  (CLAUDE.md's module boundary table). A Domain type that "needs" to log is a sign the
  decision belongs in the Application handler calling it, not the invariant itself.
- **Never log PII** (`CustomerName`, phone, email, `Notes` — the same fields the charter's
  retention/redaction corrections care about) **above `Debug`.** Production log aggregation is
  a PII sink the charter never budgeted for; log the aggregate's id, not the customer's name.
  `Debug` is acceptable since it's off in production by default — never `Information` or
  higher for anything customer-identifying.
- **Never log a secret** (a signing key, a token, a connection string, a password hash) at any
  level, including `Trace`.

### Level guidance

| Level | Use for | Example |
|---|---|---|
| `Trace` | Per-call detail nobody needs unless actively chasing a specific bug; expect this off even in staging | Token minted for an outgoing call (never the token itself) |
| `Debug` | Internal decision points worth seeing when reproducing a *reported* issue; off in production by default | A validation failure's field/rule; a gateway's idempotency-check outcome before it acts on it |
| `Information` | Business-significant events an operator watches by default | A reservation was created (log the id, not the customer) |
| `Warning` | Recoverable, but the kind of thing worth noticing a pattern in | An `OperationId` replay was detected (idempotent and correct, but if it's frequent, something upstream is retrying more than expected) |
| `Error` | An operation failed and the caller was affected. Always the exception-overload (`LogError(ex, "…")`) — never format the exception into the message string | An unhandled exception at a boundary |
| `Critical` | The process itself can't do its job | Startup failure: can't reach the database, required config missing |

## Per-feature test contract

| When you add… | You must add |
|---|---|
| A Domain entity/value object with an invariant | Unit test per invariant, including the boundary that violates it |
| An Application command handler | Unit test with the gateway mocked (Moq) — never a database. Failure cases also assert `Times.Never` on the gateway |
| A query handler or gateway implementation | Integration test against Testcontainers |
| A validator | One test per rule + one happy path, asserting the **exact** message string (it's what the UI shows) |
| An API endpoint | Integration test: happy path, validation rejection on the right field, permission boundary (wrong role → 403), unauthenticated → 401 |
| A mutating endpoint | All of the above **plus** an audit-entry assertion and an `OperationId` replay test (same id twice → one effect) |
| An RCL component | bUnit test per meaningful prop variant + per permission-gated control |
| A `data-testid` | Use it in the corresponding Playwright test in the same commit |
| A user-visible flow | One Playwright E2E test |
| A cross-module interaction | Integration test proving the event handler ran and the other module's state changed |
| Anything time-dependent | A `FakeTimeProvider` test pinning the boundary |

## Conventions

- Test naming: `Subject_Condition_ExpectedOutcome`. Strict Arrange-Act-Assert.
- `[Trait("Category", "Integration" | "E2E" | "Architecture")]` on the relevant test classes so
  CI can filter.
- `data-testid` is the **only** selector vocabulary in bUnit and Playwright —
  `{noun}-{id}` for rows, `btn-{action}` for buttons. Never select by visible text (the UI is
  localized nl-BE/en; text is not stable).
- Integration tests assert on raw `JsonDocument`, never typed DTOs, so the wire contract is
  locked against an accidental silent change.
- **Never `global using Bunit;`** in the bUnit test project. Add `using Bunit;` locally per
  file that needs it — originally because `Bunit.TestContext` collided with xUnit v3's own
  `Xunit.TestContext`; bunit 2.9+ renamed its type to `BunitContext` specifically to end
  that collision, but the project keeps usings local anyway rather than re-widen scope for
  no benefit. Component tests derive from `Bunit.BunitContext` and call `Render<T>(...)`,
  not the obsolete `TestContext`/`RenderComponent<T>`.
- `because:` strings on FluentAssertions calls are full sentences stating the rule being
  enforced, not a restatement of the assertion.
- **NetArchTest's `Types.InAssembly`/`InAssemblies`/`InCurrentDomain` silently ignore any
  type whose namespace starts with `System` or `Microsoft`** (a hardcoded exclusion list in
  the library itself, meant to keep BCL noise out of `InCurrentDomain()` scans) — this also
  hides `Mise.ServiceDefaults`' own `Microsoft.Extensions.Hosting.Extensions` type, which
  the Aspire template deliberately puts there. A rule that must actually see code in a
  `Microsoft.*`-namespaced project (or any future one) cannot be built on NetArchTest's
  fluent API — use `Mise.ArchitectureTests/IlCallSiteScanner.cs`'s direct Mono.Cecil scan
  instead, as `CrossCuttingTests` does for the DateTime.Now and SaveChangesAsync rules.
- **Mise.E2ETests runs Mise.Web on a real Kestrel socket**, not the in-memory `TestServer`
  `WebApplicationFactory` normally substitutes — Playwright needs an actual port to navigate
  to. This is .NET 10's `WebApplicationFactory<T>.UseKestrel(...)` / `.StartServer()` (see
  `PlaywrightWebAppFixture.cs`), not a hand-rolled `dotnet run` subprocess. `KestrelFactory<T>`
  generalizes this to boot **two** real hosts at once (Mise.ApiService + Mise.Web) for a test
  that needs the whole path, not just the UI — see `ReservationsE2EFixture.cs`. Point the
  second host's outgoing service-discovery lookups at the first via
  `UseSetting("services:{name}:{scheme}:0", url)`, the same config shape Aspire's own
  `WithReference(...)` would inject at runtime.
- **`WebApplicationFactory.WithWebHostBuilder(...)`'s `ConfigureAppConfiguration` callback
  applies too late to override a config value a minimal-hosting `Program.cs` reads
  synchronously before `builder.Build()`** (exactly what `Program.cs` does for the JWT signing
  key and the `misedb` connection string) — the read happens before that extra source is
  merged in, so the app still sees the missing/default value and throws. `UseSetting(key,
  value)` doesn't have this problem: it writes directly into the settings dictionary consulted
  from the start, and a later `UseSetting` call for the same key overwrites an earlier one
  (which is how `ReservationsApiFixture` layers a real Testcontainers connection string over
  `CustomWebApplicationFactory`'s own placeholder default). Prefer `UseSetting` over
  `ConfigureAppConfiguration` for anything a minimal-hosting `Program.cs` might read early.
- **A Blazor Web App page with `@rendermode InteractiveServer` (prerendering on) is briefly
  interactive-*looking* before it's interactive** — the initial response is static SSR markup;
  clicking a form's submit button in that window falls through to a native HTML form GET
  instead of the C# handler, because no circuit is attached yet to intercept it. A real
  Playwright click can land in that window (this is exactly what caught
  `CreateReservation_HappyPath_AppearsInDayList` the first time it ran). Any page with a form
  meant to be exercised by Playwright needs `@rendermode @(new InteractiveServerRenderMode(prerender: false))`
  instead of the bare `InteractiveServer` — the form then simply isn't in the DOM until the
  circuit is ready, which Playwright's own auto-waiting locators handle with no explicit wait.

## Keeping this file honest

Update this file in the same commit as any change that introduces a new pattern (a new kind of
seam, a new test base class, a new forbidden reference). A stale CLAUDE.md is worse than none —
it tells the next session things that are no longer true.
