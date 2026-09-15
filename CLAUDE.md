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

## No mediator library (ADR-005)

Cross-module domain events go through a ~40-line hand-rolled `IDomainEventPublisher` /
`IDomainEventHandler<T>` in `Mise.SharedKernel(.Infrastructure)`, not MediatR (which went
commercial-license after v12) or any other package. Handlers are registered directly in DI and
called directly — no pipeline behaviors, no reflection-based discovery.

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
  `PlaywrightWebAppFixture.cs`), not a hand-rolled `dotnet run` subprocess.

## Keeping this file honest

Update this file in the same commit as any change that introduces a new pattern (a new kind of
seam, a new test base class, a new forbidden reference). A stale CLAUDE.md is worse than none —
it tells the next session things that are no longer true.
