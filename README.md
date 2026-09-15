# Mise

Internal staff tool for Coteng's restaurant: phone reservations and live table/floor-plan
management for Floor Staff and Managers. Not a customer-facing app.

Full product spec: [docs/Restaurant-Reservations-Project-Charter.md](docs/Restaurant-Reservations-Project-Charter.md).
Execution plan (phases, test strategy, architecture decisions): see the plan history in
this repo's early commits, or ask for a copy of the working plan doc.

## Status

Phase 0 (repo governance + solution restructure) in progress. No domain features exist yet —
`Mise.ApiService` and `Mise.Web` are still close to the Aspire starter template, stripped of
its sample weather-forecast code.

## Prerequisites

- **.NET SDK 10.0.x** — pinned via [global.json](global.json) (`rollForward: latestFeature`, no
  prereleases). Run `dotnet --version` from the repo root; it must print a `10.0.x` version, not
  an 11.x preview.
- **Docker Desktop** (or another Docker engine) — required for PostgreSQL via Aspire locally, and
  for Testcontainers-based integration tests once Phase 1 lands.
- **Aspire CLI** — `dotnet workload install aspire` if `aspire` isn't already on your PATH.
- **.NET tool manifest** — run `dotnet tool restore` once per clone to get the pinned
  `dotnet-ef`, `dotnet-reportgenerator-globaltool`, and Playwright CLI versions
  ([.config/dotnet-tools.json](.config/dotnet-tools.json)).

## Running the app

```bash
aspire run
```

This starts the Aspire AppHost, which currently wires up `Mise.ApiService` and `Mise.Web`
(PostgreSQL is added to the resource graph in a later phase, once a module actually needs
persistence). The Aspire dashboard shows logs, traces, and each resource's endpoint.

## Building and testing

```bash
dotnet build Mise.slnx -c Release
dotnet test src/Mise.Tests/Mise.Tests.csproj -c Release
```

`Mise.Tests` currently holds one Aspire smoke test (`WebTests.cs`) asserting the AppHost boots
and `webfrontend` answers `GET /` with 200. It becomes `tests/Mise.IntegrationTests` alongside
four other test-tier projects (unit, bUnit component, Playwright E2E, architecture) in Phase 1.

## Solution layout

```
Mise.slnx
├── src/
│   ├── Mise.AppHost            Aspire orchestrator (dev-time only; never referenced elsewhere)
│   ├── Mise.ApiService          REST API host (gains the SignalR hub + Identity in later phases)
│   ├── Mise.Web                 Blazor Server UI — an API client, not an in-process caller
│   ├── Mise.ServiceDefaults      OpenTelemetry, health checks, service discovery, resilience
│   └── Mise.Tests                (temporary — see Phase 1 note above)
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
