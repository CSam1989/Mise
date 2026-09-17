# Mise

Internal staff tool for Coteng's restaurant: phone reservations and live table/floor-plan
management for Floor Staff and Managers. Not a customer-facing app.

Full product spec: [docs/Restaurant-Reservations-Project-Charter.md](docs/Restaurant-Reservations-Project-Charter.md).
Execution plan (phases, test strategy, architecture decisions): see the plan history in
this repo's early commits, or ask for a copy of the working plan doc.

## Status

Phases 0-3 are done. Staff can really sign in now: `Mise.Modules.StaffIdentity`
(Domain/Application/Infrastructure/Contracts) is the second registered module — ASP.NET Core
Identity for credentials, two roles (`FloorStaff`/`Manager`, Manager implying FloorStaff),
policy-based authorization, and a real JWT issued per staff member (see CLAUDE.md's "Staff auth
spine" section). `Mise.Web` has a real `/login` page (cookie auth for the site itself, the API
token held server-side only — never a browser cookie or `localStorage` value) and every page but
Home now requires sign-in. `Mise.SharedKernel.Persistence` is new too: the EF-aware home for
`shared.processed_operation`/`shared.audit_log_entry`, extracted once StaffIdentity became the
second module needing them (see CLAUDE.md's "Cross-cutting infrastructure" section for why it
couldn't live in `Mise.SharedKernel.Infrastructure` instead). `Reservation`s created via the web
UI are now attributed to the real signed-in staff member's id, not a fixed system identity.

Phase 2's walking skeleton (`Mise.Modules.Reservations`, create a reservation end to end) is
still the first registered module and hasn't changed shape. Phase 4 (Tables & Sections) is next.

One-time setup this phase added: run `dotnet user-secrets set Parameters:seed-manager-password
<any-random-string>` from `src/Mise.AppHost` before your first `aspire run` (alongside Phase 2's
`jwt-signing-key`) — `Mise.MigrationService` seeds exactly one bootstrap Manager account
(username `manager` by default, overridable via `Parameters:seed-manager-username`) so there's
someone who can register further staff at all. See
[docs/Phase-3-Manual-Test-Checklist.md](docs/Phase-3-Manual-Test-Checklist.md) for the full
one-time setup and a guided walkthrough.

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
│   ├── Mise.SharedKernel.Persistence  EF-aware home for ProcessedOperation + AuditWriter<T> (module Infrastructure only, never Application)
│   ├── Modules/
│   │   ├── Mise.Modules.Reservations.{Domain,Application,Infrastructure,Contracts}
│   │   └── Mise.Modules.StaffIdentity.{Domain,Application,Infrastructure,Contracts}
│   └── UI/
│       ├── Mise.UI.Abstractions      Transport-agnostic client interfaces (IReservationsClient, IStaffAuthClient, …)
│       └── Mise.UI.Components        The shared RCL both Blazor Server and MAUI render (ReservationForm, …)
├── tests/
│   ├── Mise.UnitTests             Domain/Application unit tests, gateway ports mocked
│   ├── Mise.IntegrationTests      Testcontainers PostgreSQL — rollback + committed-write harnesses, plus real endpoint tests
│   ├── Mise.Client.UnitTests      bUnit component tests (Microsoft.NET.Sdk.Razor)
│   ├── Mise.E2ETests              Playwright, against Mise.Web (and, where needed, Mise.ApiService too) on real Kestrel sockets
│   └── Mise.ArchitectureTests     NetArchTest — ADR-001's module boundaries as a build failure
└── docs/
    ├── Restaurant-Reservations-Project-Charter.md
    ├── Phase-2-Manual-Test-Checklist.md
    └── Phase-3-Manual-Test-Checklist.md
```

`Tables` and `Scheduling` modules land starting Phase 4+, following the same four-project shape
Reservations and StaffIdentity established.

## Conventions

- **`TreatWarningsAsErrors` is on solution-wide.** A warning fails your local build the same way
  it fails CI.
- **Central Package Management is on** (`Directory.Packages.props`) — add package *versions*
  there, `PackageReference` entries in a `.csproj` carry no `Version` attribute.
- Line endings are LF everywhere (`.gitattributes` + `.editorconfig`); `dotnet format
  Mise.slnx --verify-no-changes` is what CI checks.
- See [CLAUDE.md](CLAUDE.md) for the architecture boundaries, the per-feature test contract, and
  naming conventions every later change follows.
