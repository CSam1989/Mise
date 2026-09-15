# Mise

Internal staff tool for Coteng's restaurant: phone reservations and live table/floor-plan
management for Floor Staff and Managers. Not a customer-facing app.

Full product spec: [docs/Restaurant-Reservations-Project-Charter.md](docs/Restaurant-Reservations-Project-Charter.md).
Execution plan (phases, test strategy, architecture decisions): see the plan history in
this repo's early commits, or ask for a copy of the working plan doc.

## Status

Phases 0-2 are done. The first real feature exists end to end: create a reservation, proven
at all five test tiers. `Mise.Modules.Reservations` (Domain/Application/Infrastructure/Contracts)
is the first registered module; `Mise.SharedKernel.Infrastructure` (`IAuditWriter`) and
`Mise.UI.Abstractions`/`Mise.UI.Components` (the shared RCL, starting with `ReservationForm`)
exist for the first time too. `Mise.MigrationService` runs the Reservations schema migration
before `Mise.ApiService` starts. A minimal placeholder JWT-bearer auth scheme protects every
endpoint by default (see CLAUDE.md's "Placeholder auth spine" section) — there is no real
sign-in yet; that is StaffIdentity's job in Phase 3. `OperationId`-based idempotency (`shared.processed_operation`)
is wired on the one mutating endpoint that exists. Phase 3 (StaffIdentity: real Identity, two
roles, policies) is next.

One-time setup this phase added: run `dotnet user-secrets set Parameters:jwt-signing-key
<any-random-string>` from `src/Mise.AppHost` before your first `aspire run` — the signing key
has no checked-in default (see CLAUDE.md).

## Prerequisites

- **.NET SDK 10.0.x** — pinned via [global.json](global.json) (`rollForward: latestFeature`, no
  prereleases). Run `dotnet --version` from the repo root; it must print a `10.0.x` version, not
  an 11.x preview.
- **Docker Desktop** (or another Docker engine), **running** — required for PostgreSQL via
  Aspire locally, and for the Testcontainers-based integration tests.
- **Aspire CLI** — `dotnet workload install aspire` if `aspire` isn't already on your PATH.
- **.NET tool manifest** — run `dotnet tool restore` once per clone to get the pinned
  `dotnet-ef`, `dotnet-reportgenerator-globaltool`, and Playwright CLI versions
  ([.config/dotnet-tools.json](.config/dotnet-tools.json)).
- **Playwright browsers** — run `tests/Mise.E2ETests/bin/<config>/net10.0/playwright.ps1
  install --with-deps chromium` once per machine before running the E2E suite.

## Running the app

```bash
aspire run
```

This starts the Aspire AppHost: PostgreSQL (with a persistent data volume and an
auto-generated password), `Mise.ApiService`, and `Mise.Web`. The Aspire dashboard shows
logs, traces, and each resource's endpoint.

## Building and testing

```bash
./scripts/test.ps1
```

Builds the solution in Release and runs all five test-tier suites plus the format check —
the same steps CI runs on every push. Run any tier directly with `dotnet test
tests/Mise.<Tier>Tests/Mise.<Tier>Tests.csproj -c Release`. `./scripts/coverage.ps1` runs
just the unit-tier suites with the tiered coverage gate (90% line coverage, hard-gated, on
every `*.Domain`/`*.Application` assembly — see `CLAUDE.md`).

## Solution layout

```
Mise.slnx
├── src/
│   ├── Mise.AppHost                Aspire orchestrator (dev-time only; never referenced elsewhere)
│   ├── Mise.ApiService              REST API host + placeholder auth (gains the SignalR hub + real Identity later)
│   ├── Mise.MigrationService        One-shot worker: migrates every module's DbContext, then exits
│   ├── Mise.Web                     Blazor Server UI — an API client, not an in-process caller
│   ├── Mise.ServiceDefaults          OpenTelemetry, health checks, service discovery, resilience
│   ├── Mise.SharedKernel              Entity/AggregateRoot, IDomainEvent, Result, guard clauses
│   ├── Mise.SharedKernel.Infrastructure  IAuditWriter / AuditLogEntry — cross-cutting, not module-owned
│   ├── Modules/
│   │   └── Mise.Modules.Reservations.{Domain,Application,Infrastructure,Contracts}
│   └── UI/
│       ├── Mise.UI.Abstractions      Transport-agnostic client interfaces (IReservationsClient, …)
│       └── Mise.UI.Components        The shared RCL both Blazor Server and MAUI render (ReservationForm, …)
├── tests/
│   ├── Mise.UnitTests             Domain/Application unit tests, gateway ports mocked
│   ├── Mise.IntegrationTests      Testcontainers PostgreSQL — rollback + committed-write harnesses, plus real endpoint tests
│   ├── Mise.Client.UnitTests      bUnit component tests (Microsoft.NET.Sdk.Razor)
│   ├── Mise.E2ETests              Playwright, against Mise.Web (and, where needed, Mise.ApiService too) on real Kestrel sockets
│   └── Mise.ArchitectureTests     NetArchTest — ADR-001's module boundaries as a build failure
└── docs/
    └── Restaurant-Reservations-Project-Charter.md
```

`Tables`, `StaffIdentity`, and `Scheduling` modules land starting Phase 3+, following the same
four-project shape Reservations established.

## Conventions

- **`TreatWarningsAsErrors` is on solution-wide.** A warning fails your local build the same way
  it fails CI.
- **Central Package Management is on** (`Directory.Packages.props`) — add package *versions*
  there, `PackageReference` entries in a `.csproj` carry no `Version` attribute.
- Line endings are LF everywhere (`.gitattributes` + `.editorconfig`); `dotnet format
  Mise.slnx --verify-no-changes` is what CI checks.
- See [CLAUDE.md](CLAUDE.md) for the architecture boundaries, the per-feature test contract, and
  naming conventions every later change follows.
