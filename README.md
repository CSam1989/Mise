# Mise

Internal staff tool for Coteng's restaurant: phone reservations and live table/floor-plan
management for Floor Staff and Managers. Not a customer-facing app.

Full product spec: [docs/Restaurant-Reservations-Project-Charter.md](docs/Restaurant-Reservations-Project-Charter.md).
Execution plan (phases, test strategy, architecture decisions): see the plan history in
this repo's early commits, or ask for a copy of the working plan doc.

## Status

Phases 0-6 are done. Four modules are registered: `Mise.Modules.Reservations` (Phase 2's walking
skeleton grown, in Phase 6, into the real aggregate — phone/email/notes/table assignment/
`xmin`, `Update`/`Cancel` alongside `Create`, US-02 search, and BR-01's Postgres exclusion
constraint for "no overlapping reservations on one table"), `Mise.Modules.StaffIdentity`
(Phase 3 — ASP.NET Core Identity, two roles (`FloorStaff`/`Manager`, Manager implying FloorStaff),
policy-based authorization, a real JWT issued per staff member), `Mise.Modules.Tables` (Phase 4 —
Manager-only CRUD on Sections and Tables, the first optimistic-concurrency pattern in the
codebase (`ETag`/`If-Match` backed by Postgres's `xmin`), plus Phase 6's `TableGroup` — an
explicit, Manager-created combinable-table concept backing BR-07's "or an explicitly combinable
set of tables"), and `Mise.Modules.Scheduling` (Phase 5 — Manager-only CRUD on `ServicePeriod`,
i.e. service hours like Lunch/Dinner and marking days closed; see CLAUDE.md's "Scheduling"
section for how a thin charter spec here got resolved). All four follow the same four-project
shape (`Domain`/`Application`/`Infrastructure`/`Contracts`).

Phase 6 is also this codebase's first real cross-module read at request time:
`Mise.Modules.Reservations.Application` calls `Mise.Modules.Tables.Contracts.
ITableAvailabilityLookup` directly to answer BR-07 (party size vs. a table's own or a
combinable group's capacity) — see CLAUDE.md's ADR-006 for the design and why it doesn't cross
any architecture-test boundary.

`Mise.Web` has a real `/login` page (cookie auth for the site itself, the API token held
server-side only — never a browser cookie or `localStorage` value) and every page but Home
requires sign-in; `Reservation`s created via the web UI are attributed to the real signed-in staff
member's id, and the form now also collects `CustomerPhone` (FR-01 names it explicitly).
`Mise.SharedKernel.Persistence` is the EF-aware home for `shared.processed_operation`/
`shared.audit_log_entry`, shared by every module's `Infrastructure` project (never `Application`
— see CLAUDE.md's "Cross-cutting infrastructure" section).

Tables/Sections, Scheduling, and Reservations' new search/edit/cancel/table-assignment surface
are all API-only so far — proven at the Architecture/Unit/Integration tiers, no Blazor UI beyond
the one field above. The shared RCL screens (including the live floor-plan board) land in
Phase 10. Reservations still has no cross-module link to Scheduling (a reservation can be made
outside any defined service period — FR-08 validation isn't in Phase 6's charter refs), and
BR-01 doesn't extend across a `TableGroup`'s other members (a known, documented gap — see
CLAUDE.md). Seating (`Seated`/`Completed`/`NoShow`, BR-04/BR-05, the cross-module table-status
event) is Phase 7.

One-time setup (unchanged since Phase 3 — Phase 4/5/6 added no new secrets): run `dotnet
user-secrets set Parameters:seed-manager-password <any-random-string>` from `src/Mise.AppHost`
before your first `aspire run` (alongside Phase 2's `jwt-signing-key`) — `Mise.MigrationService`
seeds exactly one bootstrap Manager account (username `manager` by default, overridable via
`Parameters:seed-manager-username`) so there's someone who can register further staff at all. See
[docs/Phase-6-Manual-Test-Checklist.md](docs/Phase-6-Manual-Test-Checklist.md) for the full
one-time setup and a guided walkthrough of what's landed most recently.

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
│   │   ├── Mise.Modules.StaffIdentity.{Domain,Application,Infrastructure,Contracts}
│   │   ├── Mise.Modules.Tables.{Domain,Application,Infrastructure,Contracts}
│   │   └── Mise.Modules.Scheduling.{Domain,Application,Infrastructure,Contracts}
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
    ├── Phase-3-Manual-Test-Checklist.md
    ├── Phase-4-Manual-Test-Checklist.md
    ├── Phase-5-Manual-Test-Checklist.md
    └── Phase-6-Manual-Test-Checklist.md
```

## Conventions

- **`TreatWarningsAsErrors` is on solution-wide.** A warning fails your local build the same way
  it fails CI.
- **Central Package Management is on** (`Directory.Packages.props`) — add package *versions*
  there, `PackageReference` entries in a `.csproj` carry no `Version` attribute.
- Line endings are LF everywhere (`.gitattributes` + `.editorconfig`); `dotnet format
  Mise.slnx --verify-no-changes` is what CI checks.
- See [CLAUDE.md](CLAUDE.md) for the architecture boundaries, the per-feature test contract, and
  naming conventions every later change follows.
