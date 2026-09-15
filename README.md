# Mise

Internal staff tool for Coteng's restaurant: phone reservations and live table/floor-plan
management for Floor Staff and Managers. Not a customer-facing app.

Full product spec: [docs/Restaurant-Reservations-Project-Charter.md](docs/Restaurant-Reservations-Project-Charter.md).
Execution plan (phases, test strategy, architecture decisions): see the plan history in
this repo's early commits, or ask for a copy of the working plan doc.

## Status

Phase 0 (repo governance) and Phase 1 (test harnesses & CI) are done. No domain features
exist yet — `Mise.ApiService` and `Mise.Web` are still close to the Aspire starter template,
stripped of its sample weather-forecast code — but all five test tiers (unit, integration,
bUnit, Playwright E2E, architecture) are scaffolded and proven against that template, Postgres
is wired into the local Aspire graph, and CI (`.github/workflows/ci.yml`) runs all of it on
every push and pull request. Phase 2 (the first real feature — a walking-skeleton
"create a reservation" slice) is next.

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
│   ├── Mise.AppHost            Aspire orchestrator (dev-time only; never referenced elsewhere)
│   ├── Mise.ApiService          REST API host (gains the SignalR hub + Identity in later phases)
│   ├── Mise.Web                 Blazor Server UI — an API client, not an in-process caller
│   ├── Mise.ServiceDefaults      OpenTelemetry, health checks, service discovery, resilience
│   └── Mise.SharedKernel          Entity/AggregateRoot, IDomainEvent, Result, guard clauses
├── tests/
│   ├── Mise.UnitTests             Domain/Application unit tests, gateway ports mocked
│   ├── Mise.IntegrationTests      Testcontainers PostgreSQL — rollback + committed-write harnesses
│   ├── Mise.Client.UnitTests      bUnit component tests (Microsoft.NET.Sdk.Razor)
│   ├── Mise.E2ETests              Playwright, against Mise.Web on a real Kestrel socket
│   └── Mise.ArchitectureTests     NetArchTest — ADR-001's module boundaries as a build failure
└── docs/
    └── Restaurant-Reservations-Project-Charter.md
```

`src/Modules/` (Reservations, Tables, StaffIdentity, Scheduling — each split into
Domain/Application/Infrastructure/Contracts) and `src/UI/` (the shared Razor Class Library +
its transport-agnostic client interfaces) land starting Phase 2.

## Conventions

- **`TreatWarningsAsErrors` is on solution-wide.** A warning fails your local build the same way
  it fails CI.
- **Central Package Management is on** (`Directory.Packages.props`) — add package *versions*
  there, `PackageReference` entries in a `.csproj` carry no `Version` attribute.
- Line endings are LF everywhere (`.gitattributes` + `.editorconfig`); `dotnet format
  Mise.slnx --verify-no-changes` is what CI checks.
- See [CLAUDE.md](CLAUDE.md) for the architecture boundaries, the per-feature test contract, and
  naming conventions every later change follows.
