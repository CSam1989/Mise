# Mise — Restaurant Reservations: Implementation Plan

**How to read this:** Context → Decisions come first. Then the architecture (ADR-004/005, repo governance, test tiers, CI, solution inventory) and the **Phased roadmap** — the part to execute from. **Corrections to the charter** and **Testing the hard subsystems** near the end are a deep-dive appendix: real defects found in the charter's own data model/SQL and the design for the five subsystems that don't fit ordinary CRUD testing. Read them before starting Phases 6–9 and 12–17, which is where they bite.

---

## Context

`E:\repos\personal\Mise` currently holds an untouched `dotnet new aspire-starter` template and a 63 KB project charter. The charter ([docs/Restaurant-Reservations-Project-Charter.md](docs/Restaurant-Reservations-Project-Charter.md)) is complete through the Architect / System Designer / Technical Analyst stages: it fixes the architecture (ADR-001), the offline-first sync design (ADR-002), the data model, the effort estimate, and a risk register. What it does **not** have is an execution plan — nothing maps its user stories onto projects, files, phases, and tests.

This plan is that missing layer. Its purpose is to get from "template + charter" to a repository where every feature that lands is proven by tests at every tier, and where the architecture's boundaries are enforced by the build rather than by discipline.

The explicit priority from the user: **test infrastructure is set up first and grows with every feature** — not retrofitted.

---

## Verified facts

Checked against the repo and machine before planning, not assumed.

### Repo state
| Fact | Evidence |
|---|---|
| Not a git repository | No `.git` anywhere in `E:\repos\personal\Mise` or its parents |
| Solution lives *inside* `src/` | `src/Mise.slnx` — flat, no solution folders, 5 projects |
| Untouched Aspire starter | `Mise.AppHost` wires only `apiservice` + `webfrontend`; no `AddPostgres`, no container, no parameter |
| API is the weatherforecast template | `src/Mise.ApiService/Program.cs` — `/weatherforecast` only |
| Web is a Blazor Web App, InteractiveServer only | `src/Mise.Web/Program.cs`; template Home/Counter/Weather pages; vendored Bootstrap 5 |
| `WeatherForecast` record is duplicated | Defined in both `Mise.ApiService/Program.cs` and `Mise.Web/WeatherApiClient.cs` — no shared contracts project exists |
| Health endpoints are Development-only | Stock `Mise.ServiceDefaults/Extensions.cs` maps `/health` + `/alive` inside `if (app.Environment.IsDevelopment())` |
| A test project already exists | `src/Mise.Tests` — xunit.v3 3.0.1 + `Aspire.Hosting.Testing` 13.5.3, one AppHost smoke test |
| No governance files | Missing: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitattributes`, `nuget.config`, `.github/`, `Dockerfile`, `CLAUDE.md` |

### Machine state
| Fact | Consequence |
|---|---|
| `dotnet --version` → **11.0.100-preview.7** | With no `global.json`, a .NET 11 **preview** SDK is building `net10.0` projects today. Must be pinned. |
| SDK 10.0.401 is installed | Pin target available. |
| `10.0.100` workload manifests exist on disk, incl. `microsoft.net.sdk.maui` | MAUI on .NET 10 is reachable, but currently-installed workloads resolve to net11-preview manifests — a workload install under the 10.0.100 band will be needed. |
| Docker 29.7.2 present | Testcontainers is viable for real-PostgreSQL integration tests. |

### UI mockups
`docs/Restaurant reservations UI mockups.zip` is a design-canvas prototype ("Lilshof Front of House") with a Tablet/Desktop toggle. Screens confirmed: PIN sign-in (tablet) / username+password sign-in (desktop), Live Floor Plan with table drawer and an "Unassigned — drag onto a table" tray, Reservations list + search with an explicit empty state, New Reservation modal with phone-match suggestion and inline party-size validation, Tables & Sections, **Sync Conflicts** queue (accept / reject both / reassign), and **Paired Devices**.

---

## Decisions (binding, user-confirmed)

1. **Keep Aspire** as the local orchestrator — one command brings up PostgreSQL + both hosts. Domain and Application layers stay Aspire-ignorant; Aspire is a dev/orchestration concern only. *(An addition to ADR-001, which does not mention Aspire — recorded as ADR-003.)*
2. **Walking skeleton first.** Phase 1 delivers one thin end-to-end slice (create a reservation) proven by a test at every tier, before any module is built out.
3. **All five test tiers scaffolded from day one** and wired into CI immediately: unit, integration (Testcontainers), bUnit component, Playwright E2E, architecture (NetArchTest).
4. **GitHub Actions with hard gates** — build + every suite + a coverage threshold that fails the build, `TreatWarningsAsErrors`.
5. **Two hosts: the Blazor Server website consumes the same REST API as MAUI.** This **amends ADR-001 Decision #4** (which had the website calling the Application layer in-process). Recorded as ADR-004. Rationale: one client implementation instead of two, and the shared RCL is exercised over the identical transport in both front-ends — which is the cheapest possible mitigation of the charter's RISK-03. Cost: an HTTP hop for the web UI, and the web host needs a way to authenticate to the API on the user's behalf.
6. **Coverage gating is tiered by layer**, not global: Domain + Application at 90% line coverage as a hard build failure; everything else reported to the job summary but ungated.
7. **MAUI and offline-first are planned in full now, executed after the web slice is green.** Charter Spike 1 (Outbox proof) runs early as a throwaway so nothing structural is decided late.
8. **This is a personal project, not a Coteng one.** Consequences: FluentAssertions 8.10.0 is fine (Xceed Community Licence covers non-commercial use), so the assertion idiom matches FamilySplit exactly; and GitHub Actions — not the charter's assumed Coteng GitLab/Docker/Nexus pipeline — is the CI platform.

## Decisions inherited from the charter (not re-litigated here)

Modular monolith with Clean Architecture per module · PostgreSQL + EF Core (Npgsql) · SignalR for live floor-plan updates · Blazor Server website in the same process as the API · shared Razor Class Library consumed by both hosts · MAUI Blazor Hybrid with offline-first Outbox sync and human-reviewed conflict resolution · ASP.NET Core Identity with PIN + device pairing for MAUI · audit trail on every mutation · nightly retention/anonymization.

---

## ADR-004 — Two hosts; the website is an API client (amends ADR-001 Decision #4)

**Status:** ACCEPTED (user-confirmed). **Supersedes:** ADR-001 Decision #4's "in-process" website.

ADR-001 put the Blazor Server website in the API's process so its components could call the Application layer directly. That would have produced two implementations of every client interface — one in-process for the web, one HTTP for MAUI — which is exactly the fork the charter's RISK-03 warns about. Instead, `Mise.Web` consumes the same REST API `Mise.ApiService` exposes to MAUI. One implementation of `IReservationsClient` / `ITablesClient` exists and both front-ends use it over the same transport.

**Consequence that must be designed, not glossed over:** the web host now needs to authenticate *to* the API on the signed-in user's behalf. The pattern to mirror is already proven in FamilySplit: the API owns Identity and issues a JWT; a `DelegatingHandler` attaches it as a bearer token and performs exactly one silent refresh on a 401 before surfacing it ([JwtAuthHandler.cs](../../../../E:/repos/personal/FamilySplit/src/FamilySplit.Client/Services/JwtAuthHandler.cs)). The one real difference: FamilySplit's client is Blazor **WASM**, where the token lives in the browser. Mise's web client is Blazor **Server**, where the token must live server-side in the circuit and survive circuit reconnects. This is a known-sharp-edged area and gets its own task with its own tests, rather than being assumed to fall out of the pattern.

**Also inherited from FamilySplit:** typed client interfaces per slice (`IExpenseClient`, `IGroupClient`, …) already are the `UI.Abstractions` pattern ADR-001's RISK-03 asks for. Mise adopts it directly.

**Auth flow across the two hosts, concretely:** Identity lives in `Mise.ApiService` (it owns every module's Infrastructure, including StaffIdentity). `Mise.Web`'s sign-in page posts credentials to the API's login endpoint, receives a JWT, and does two things with it: (1) signs the browser into the **website's own** cookie-auth scheme so Blazor Server's `AuthenticationStateProvider` and `[Authorize]` work normally on the web host, and (2) holds the JWT server-side, scoped to that user's circuit (a scoped service backing the `JwtAuthHandler`-equivalent `DelegatingHandler`), so it survives circuit reconnects but never reaches the browser as a cookie or local storage value. This is genuinely new relative to FamilySplit (whose Blazor **WASM** client keeps the JWT client-side) and gets its own task and its own tests in Phase 3 — not assumed to fall out of copying the pattern.

## ADR-005 — No mediator library (MediatR or otherwise)

ADR-001 says modules communicate via domain events "e.g. MediatR". **Decision: no mediator package.** MediatR's license moved commercial after v12 (the last MIT release is v12.4.1) — a real cost for a four-module monolith with a handful of cross-module events, and a bad trade for what a mediator buys here. FamilySplit already proves the alternative works: handlers registered directly in DI, called directly from the endpoint, with a ~40-line hand-rolled `IDomainEventPublisher`/`IDomainEventHandler<T>` pair in `Mise.SharedKernel` for the cross-module event case (§10 "Reservation Seated → Table Occupied"). Cheap, license-free, and the stack traces stay readable. Reversible later — `IDomainEventPublisher` is the seam, so swapping in MediatR is a DI change, not a rewrite.

---

## Repository governance (Phase 0)

The repo moves to FamilySplit's shape — solution at the root, `src/` and `tests/` as siblings. Today `Mise.slnx` sits *inside* `src/`, which leaves nowhere natural for `tests/` and breaks the convention already established in the sibling project.

| File | Decision |
|---|---|
| `git init` | First. Every phase below ends in one green commit; without a repo there is no "green commit" to speak of. |
| `Mise.slnx` | Move `src/Mise.slnx` → `Mise.slnx` (root), add solution folders `/src/`, `/src/Modules/`, `/src/UI/`, `/tests/`. |
| `global.json` | `{ "sdk": { "version": "10.0.401", "rollForward": "latestFeature", "allowPrerelease": false } }` — **this is urgent**: without it the .NET 11 preview SDK builds the solution. |
| `Directory.Build.props` | Copy FamilySplit's verbatim: net10.0, LangVersion latest, Nullable enable, ImplicitUsings enable, `TreatWarningsAsErrors`, `MSBuildTreatWarningsAsErrors`, `InvariantGlobalization false` (required — nl-BE localization). |
| `Directory.Packages.props` | Central Package Management on, `CentralPackageTransitivePinningEnabled` on, labelled ItemGroups, every CVE pin commented with its GHSA id. |
| `nuget.config` | `packageSourceMapping` block only (required with CPM — NU1507). |
| `.editorconfig` + `.gitattributes` | Copy FamilySplit's: LF endings, file-scoped namespaces at **warning** severity, `* text=auto eol=lf`. The pairing exists there because a CRLF incident once broke their format gate. |
| `.config/dotnet-tools.json` | Pin `dotnet-ef`, `dotnet-reportgenerator-globaltool`, `microsoft.playwright.cli`, all `rollForward: false`. |
| `CLAUDE.md` | Author one. FamilySplit's is what keeps its rules enforceable; Mise needs the equivalent from commit one, not retrofitted. |
| `scripts/` | Net-new vs FamilySplit (whose `scripts/` is empty): `test.ps1` and `coverage.ps1` so the CI commands are runnable locally without copy-pasting YAML. |

### Package versions (verified from FamilySplit's `Directory.Packages.props`)

xunit.v3 **3.2.2** · xunit.runner.visualstudio **3.1.5** · Microsoft.NET.Test.Sdk **18.8.1** · FluentAssertions **8.10.0** · Moq **4.20.72** · Testcontainers.PostgreSql **4.13.0** · Respawn **7.0.0** · Microsoft.AspNetCore.Mvc.Testing **10.0.10** · bunit **2.7.2** · AngleSharp **1.5.0** (transitive pin — bunit still pins 1.4.0, GHSA-pgww-w46g-26qg) · Microsoft.Playwright **1.61.0** · NetArchTest.Rules **1.3.2** · coverlet.collector **10.0.1**.

Net-new for Mise (versions to confirm at install): `Microsoft.Extensions.TimeProvider.Testing`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Aspire.Hosting.PostgreSQL`, `Microsoft.AspNetCore.SignalR.Client`, SQLCipher (`SQLitePCLRaw.bundle_e_sqlcipher`), and the MAUI workload packages.

### One inherited pattern that will NOT survive contact with this project

FamilySplit's integration harness gives every test a single shared `NpgsqlConnection` (`;Pooling=false`) with one open transaction that the API's `DbContext` joins via `UseTransaction`, rolled back on dispose ([IntegrationTestBase.cs](../../../../E:/repos/personal/FamilySplit/tests/FamilySplit.IntegrationTests/Infrastructure/IntegrationTestBase.cs)). It is fast and perfectly isolated — and it **cannot express the tests this project most needs**:

- **NFR-03 concurrency** ("two staff must not silently overwrite each other") needs two genuinely concurrent connections. One shared connection serializes them by construction, so an `xmin` optimistic-concurrency test written on this harness would pass without ever proving anything.
- **BR-01 enforced at the database level** (if a Postgres exclusion constraint is used) needs two competing transactions.
- **SignalR propagation** and anything observed by a second process needs *committed* data, which a rolled-back transaction never produces.

So Mise needs **two integration flavours**: the rollback harness for the bulk of endpoint tests, and a committed-writes harness (Respawn reset between tests — already referenced in FamilySplit as the documented fallback) for concurrency, constraint, and real-time tests. This is called out here because adopting the FamilySplit harness wholesale and only discovering the gap when writing the concurrency tests is the predictable failure mode.

---

## Test architecture — the five tiers

All five projects are created in Phase 0, **before any feature exists**, each with at least one real passing test so the harness itself is proven. FamilySplit's own plan does exactly this ("2.1 harness first, then the endpoint sub-tasks") and it is the reason its suites stayed trustworthy.

| # | Project | SDK | Proves | Runs |
|---|---|---|---|---|
| 1 | `tests/Mise.UnitTests` | `Microsoft.NET.Sdk` | Domain invariants, use-case handlers over mocked ports, validators, pure calculators. No database, no HTTP. Target <1 ms per test. | Every push |
| 2 | `tests/Mise.IntegrationTests` | `Microsoft.NET.Sdk` | Real PostgreSQL via Testcontainers + `WebApplicationFactory`. Endpoint contracts, EF mappings, migrations, auth policies, DB-level constraints. | Every push |
| 3 | `tests/Mise.Client.UnitTests` | **`Microsoft.NET.Sdk.Razor`** | bUnit component tests for the shared RCL — the components both Blazor Server and MAUI render, so a bug here is a bug in both. | Every push |
| 4 | `tests/Mise.E2ETests` | `Microsoft.NET.Sdk` | Playwright against the running Blazor host: PIN sign-in, create reservation, seat a party, resolve a conflict. | Every push (gated) |
| 5 | `tests/Mise.ArchitectureTests` | `Microsoft.NET.Sdk` | NetArchTest rules making ADR-001's module boundaries a build failure. | Every push, seconds |

Tier 5 is split out rather than living inside `Mise.UnitTests` (where FamilySplit keeps it) for one reason: it must be runnable on its own, in under a second, as the fastest possible feedback that a boundary was crossed.

**What happens to the existing `src/Mise.Tests`:** it becomes `tests/Mise.IntegrationTests`. Its one asset — the `Aspire.Hosting.Testing` smoke test that boots the whole AppHost — is kept as a single deliberate smoke test (does the real app graph start and answer `/health`?), not as the pattern for the suite. Booting the full distributed app per test is too slow to be the default; Testcontainers + `WebApplicationFactory` is.

### Conventions carried over from FamilySplit (non-negotiable, enforced)

- Test names `Subject_Condition_ExpectedOutcome`. Strict Arrange-Act-Assert.
- `[Trait("Category", "Integration" | "E2E" | "Architecture")]` so CI can filter.
- `[CollectionDefinition]` lives in a `ProofTests.cs` per suite, alongside proof tests that validate the harness before any feature does.
- xUnit v3 idioms: `IAsyncLifetime` returns `ValueTask`; `protected static CancellationToken CT => TestContext.Current.CancellationToken;` on every base class.
- **Never `global using Bunit;`** — `Bunit.TestContext` collides with xUnit v3's `Xunit.TestContext`. Local `using Bunit;` per file, with the reason written in `GlobalUsings.cs`.
- Integration tests assert on raw `JsonDocument`, never typed DTOs, so the wire contract is locked against accidental change.
- **`data-testid` is the only selector vocabulary**, shared by bUnit and Playwright: `{noun}-{id}` for rows, `btn-{action}` for buttons. Never select by text — the UI is localized nl-BE/en and text is not stable.
- `because:` strings on assertions are written as full sentences stating the rule being enforced.
- No `Thread.Sleep`, anywhere, ever. Time is injected (see below); waiting is `Expect(...).ToBeVisibleAsync()`.

### Deterministic time

This domain is saturated with time: overlapping reservation windows, turn times, the "upcoming within 30 minutes" flag, service periods, the 12-hour offline grace period, 24-month retention, and Europe/Brussels DST. Every one of those is untestable if the code reads `DateTime.Now`.

`TimeProvider` is injected everywhere; tests use `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`). An **architecture test forbids `DateTime.Now` / `DateTime.UtcNow` / `DateTimeOffset.Now` outside the composition root** — otherwise the rule survives exactly until the first person in a hurry.

### Architecture rules (tier 5)

Adapted from FamilySplit's 14 NetArchTest rules ([ArchitectureTests.cs](../../../../E:/repos/personal/FamilySplit/tests/FamilySplit.UnitTests/Architecture/ArchitectureTests.cs)) to Clean-Architecture-per-module. Each is one `[Fact]`; each failure message names the offending types.

**Module boundaries (ADR-001's actual load-bearing claim):**
1. A module's Domain references no other `Mise.*` assembly at all.
2. A module's Application references only its own Domain, the SharedKernel, and *other modules' `Contracts`* — never another module's Domain, Application, or Infrastructure.
3. A module's Infrastructure is referenced by nothing except the composition root.
4. `Contracts` projects contain only interfaces, DTOs, and events — no EF types, no handlers.
5. The host's actual module references match a hand-maintained registry (so a new module cannot be silently forgotten — FamilySplit's Rule 6, which is the one that makes rules 1–4 trustworthy).

**Layering within a module:**
6. Domain types reference no EF Core type (`DbContext`, `DbSet<>`, `Microsoft.EntityFrameworkCore.*`).
7. Application handlers depend on port *interfaces*, never on `DbContext` — this is the seam that keeps the unit tier fast.
8. Only Infrastructure types call `SaveChangesAsync`.
9. Handlers are `sealed` and live in the namespace matching their use case; a validator lives in the same namespace as the command it validates.

**Cross-cutting:**
10. No `DateTime.Now` / `DateTime.UtcNow` / `DateTimeOffset.Now` outside the composition root — `TimeProvider` only.
11. No Aspire type is referenced from any Domain or Application assembly (keeps decision #1 honest).
12. Every mutating endpoint's handler writes an audit entry (see the hard-subsystem design for how this is actually detected rather than hoped for).

**Rules that police the tests themselves** — FamilySplit's Rule 11 is the sharpest idea in its suite and is adopted directly:
13. No `*HandlerTests` type references `DbContext`, `DbContextOptions`, or `UseInMemoryDatabase` — a unit test that quietly reaches for a database has stopped being a unit test.
14. No test assembly references `DateTime.Now`; tests that need time use `FakeTimeProvider`.

---

## CI design — `.github/workflows/ci.yml`

Mirrors FamilySplit's job graph, with the coverage gate added (FamilySplit reports coverage but never fails on it; you asked for hard gates).

| Job | Needs | Does |
|---|---|---|
| `build` | — | `dotnet restore` + `dotnet build Mise.slnx -c Release`, uploads `**/bin/Release/` + `**/obj/Release/` (1-day retention) |
| `architecture-tests` | build | `--filter Category=Architecture` — seconds; fails fast on a boundary violation before slower jobs burn minutes |
| `unit-tests` | build | `dotnet-coverage collect ... -f cobertura`, ReportGenerator → `$GITHUB_STEP_SUMMARY`, **plus the tiered coverage gate** |
| `client-unit-tests` | build | bUnit suite, same coverage treatment |
| `integration-tests` | build | `--filter Category=Integration` — Testcontainers starts Postgres itself, so no `services:` block and no secret |
| `e2e-tests` | build | Playwright: `playwright.ps1 install --with-deps chromium`, traces uploaded **on failure only** |
| `format-check` | — | `dotnet format Mise.slnx --verify-no-changes --no-restore --severity warn` |
| `dependency-audit` | — | Trivy fs scan, HIGH/CRITICAL → SARIF to the Security tab |
| `secret-scan` | — | Gitleaks with `fetch-depth: 0` (the `.dockerignore` already anticipates this: *"Keep .git so Gitleaks can scan commit history"*) |
| **`ci-gate`** | all of the above | `if: always()`, loops `join(needs.*.result, ' ')`, fails unless every result is `success` or `skipped`. The single required check. |

Two deliberate departures from FamilySplit:
- **Trigger on `pull_request` as well as `push: main`.** FamilySplit's CI only runs on pushes to `main`, which means its gate cannot actually block a bad merge. If the gate is meant to be hard, it has to run before the merge.
- **`architecture-tests` as its own early job** rather than folded into unit tests.

### The coverage gate (tiered — decision #6)

ReportGenerator has no gate, so the enforcement mechanism is explicit: run coverage per tier and fail the step on the number.

- **Hard gate, 90% line coverage:** every `*.Domain` and `*.Application` assembly. These hold BR-01 (overlap), BR-07 (capacity), the state machines, and the sync logic — the code where a gap is a real bug.
- **Reported, ungated:** hosts, Infrastructure, RCL, generated code, MAUI.
- **Ratchet:** raise the floor only when the actual figure has sat ≥3 points above it for a full phase. Never lower it silently — lowering is a commit with a reason.
- Implemented via ReportGenerator's assembly filters producing a per-tier cobertura, plus a check step that parses the line-rate and exits non-zero. (Coverlet's `/p:Threshold` is the fallback if the filter approach proves fiddly.)

### MAUI in CI

MAUI is **not** on the per-push gate: the workload install and Android/iOS toolchains cost far more CI time than the feedback is worth on every commit. It gets a separate **nightly** workflow that builds the MAUI head and runs the MAUI-specific unit tests (sync engine, Outbox, local store). The offline/sync logic itself is deliberately designed to be testable *without* a device — see the hard-subsystem section — so the fast gate still covers the risky logic.

---

## Solution & project inventory

**Decision #9 (user-confirmed): four projects per module** — the boundary is enforced by the compiler, not by a test that someone can mute. ~16 module projects plus hosts, UI, and tests. The architecture rules in tier 5 then guard the things the compiler *can't* see (namespace layering inside a project, `DateTime.Now`, audit coverage, test hygiene).

```
Mise.slnx                                    (moved to root)
├── /src/
│   ├── Mise.AppHost                         EXISTS — gains Postgres + parameters + migration wait (see below)
│   ├── Mise.ServiceDefaults                 EXISTS — health endpoints fixed (see below)
│   ├── Mise.MigrationService                NEW  — one-shot worker: pg_trgm + all four DbContexts' migrations, then exits
│   ├── Mise.SharedKernel                    NEW  — Entity/AggregateRoot base, IDomainEvent, Result,
│   │                                               guard clauses. Zero packages — must restore on a MAUI Android TFM.
│   ├── Mise.SharedKernel.Infrastructure     NEW  — DomainEventPublisher, AuditLogEntry + IAuditWriter,
│   │                                               IModule contract, ProblemDetails mapping, policy constants
│   ├── Mise.ApiService                      EXISTS — becomes the real API host + SignalR hub + Identity
│   └── Mise.Web                             EXISTS — Blazor Server host, API client (ADR-004)
│
├── /src/Modules/                            4 modules × 4 projects
│   ├── Mise.Modules.Reservations.{Domain,Application,Infrastructure,Contracts}
│   ├── Mise.Modules.Tables.{Domain,Application,Infrastructure,Contracts}
│   ├── Mise.Modules.StaffIdentity.{Domain,Application,Infrastructure,Contracts}
│   └── Mise.Modules.Scheduling.{Domain,Application,Infrastructure,Contracts}
│
├── /src/UI/
│   ├── Mise.UI.Abstractions                 NEW — IReservationsClient, ITablesClient,
│   │                                               IStaffClient, IConflictsClient, IFloorPlanStream + DTOs
│   ├── Mise.UI.Components                   NEW — the shared RCL (floor plan, table card,
│   │                                               reservation form, day timeline, conflict queue)
│   └── Mise.Maui                            NEW — MAUI Blazor Hybrid head (later phase)
│
└── /tests/
    ├── Mise.UnitTests
    ├── Mise.IntegrationTests                (was src/Mise.Tests)
    ├── Mise.Client.UnitTests                (Microsoft.NET.Sdk.Razor)
    ├── Mise.E2ETests
    └── Mise.ArchitectureTests
```

**Two gotchas worth knowing before, not during, the first build:**

- **`Mise.UI.Components` must reference `Microsoft.AspNetCore.Components.Web` as a `PackageReference`, never a `FrameworkReference`.** `FrameworkReference` is how ASP.NET-hosted projects normally pull it in, and it's what you'd reach for by habit — but MAUI's `net10.0-android`/`net10.0-windows` targets have no ASP.NET Core shared framework on the device, so `FrameworkReference` there fails with `NETSDK1073`, and only once `Mise.Maui` is added weeks into the project. Get the RCL's reference right from the day it's created.
- **Four `DbContext`s sharing one Postgres database need per-module migration history tables**, or they'll fight over a shared `__EFMigrationsHistory` and one module's migration will appear to "already be applied" because of an id collision with another's. Every module's `Infrastructure` sets `npg.MigrationsHistoryTable("__ef_migrations_history", "<schema>")` explicitly. Add an integration test asserting four distinct history tables exist — this is the kind of thing that works fine until the second module ships and then fails in a confusing way.

`Mise.MigrationService` runs `CREATE EXTENSION IF NOT EXISTS pg_trgm` before any migration runs (Reservations' search index depends on it), then migrates all four contexts, then exits. Aspire's `AppHost.cs` waits on it (`WaitForCompletion`) before starting `Mise.ApiService` — schema-ready is a precondition of serving requests, not something the API host does for itself on boot (that doesn't scale past one replica and couples "can I serve traffic" to "can I alter the schema").

### Reference rules

| From | May reference | Must never reference |
|---|---|---|
| `X.Domain` | SharedKernel only | Anything else — no EF, no ASP.NET, no other module |
| `X.Application` | own Domain, SharedKernel, **other modules' `Contracts`** | any other module's Domain/Application/Infrastructure; EF Core |
| `X.Infrastructure` | own Domain + Application, SharedKernel | another module's anything |
| `X.Contracts` | SharedKernel only | its own module's Domain/Application/Infrastructure |
| `Mise.ApiService` | every module's Application + Infrastructure (composition root) | — |
| `Mise.UI.Components` | `Mise.UI.Abstractions` only | any module, any host — **this is RISK-03, enforced** |
| `Mise.Web` / `Mise.Maui` | `UI.Components`, `UI.Abstractions` | any module project |

The last two rows matter most: the shared RCL knowing nothing but `UI.Abstractions` is the whole reason the same components can render in both hosts. That is rule 2 of the architecture suite and it is checked on every push.

### The testable seam — a data gateway, deliberately not a repository

FamilySplit's fast unit tier works because command handlers depend on a mockable `I{Slice}Data` gateway instead of `DbContext`. Mise adopts the same shape: `IReservationsData`, `ITablesData`, `IAuditWriter`, `IOutboxStore` — declared in `X.Application/Ports/`, implemented in `X.Infrastructure/Persistence/`.

The distinction that matters: these are **intention-revealing, slice-specific gateways** (`AddReservationAsync(reservation, participants, audit, ct)`) returning plain records — **not** a generic `IRepository<T>` and **not** an `IUnitOfWork`. A generic repository interface here would be an anti-seam: `DbSet<T>` is already the abstraction, and mocking a generic repository produces tests that "verify persistence" that was never exercised, while pulling people toward the EF InMemory provider — which cannot represent the exclusion constraint, `xmin`, `timestamptz` strictness, or `pg_trgm`, i.e. exactly the four behaviours this project's correctness depends on.

So: **command handlers** mock the gateway (fast unit tier). **Query handlers and gateway implementations** go straight to `DbContext` and are tested only against Testcontainers. Architecture rule 7 keeps handlers on the gateway side of that line; rule 13 stops a unit test quietly reaching for a database.

### Fixes to existing scaffold code

- **`Mise.ServiceDefaults/Extensions.cs`** maps `/health` and `/alive` only when `IsDevelopment()`. Aspire's `WithHttpHealthCheck("/health")` and any real deployment probe both need them outside Development. Fix in Phase 0 with a test asserting `/health` answers in the Production environment.
- **The duplicated `WeatherForecast` record** across `Mise.ApiService` and `Mise.Web` is the template's own illustration of the problem `Mise.UI.Abstractions` solves. Both copies are deleted along with the Counter/Weather pages in Phase 0.

---

## Phased roadmap

Every phase is **one green commit**: build in Release + all suites pass before committing, and the repo is releasable between phases. Phases are sized to be executed one per session.

### Phase 0 — Repo governance & solution restructure

| Task | Acceptance |
|---|---|
| 0.1 `git init`, initial commit of the existing template | Repo exists; template state is commit #1 so everything after is a visible diff |
| 0.2 `global.json` pinning 10.0.401 | `dotnet --version` in the repo reports 10.0.x, **not** 11.0.100-preview |
| 0.3 Move `src/Mise.slnx` → root; add solution folders | `dotnet build Mise.slnx -c Release` succeeds from the root |
| 0.4 `Directory.Build.props`, `Directory.Packages.props` (CPM), `nuget.config`, `.editorconfig`, `.gitattributes`, `.config/dotnet-tools.json` | Solution builds with `TreatWarningsAsErrors`; `dotnet format --verify-no-changes` is clean |
| 0.5 Delete template cruft: Counter/Weather pages, both `WeatherForecast` records, `WeatherApiClient` | No reference to "weather" survives `grep -ri weather src/` |
| 0.6 Fix `ServiceDefaults` health endpoints outside Development | A test asserts `/health` returns 200 with `ASPNETCORE_ENVIRONMENT=Production` |
| 0.7 `CLAUDE.md` + real `README.md` runbook | Both exist and describe what actually works today, not what is planned |

**Exit:** clean Release build, format gate green, `/health` proven outside Development.

### Phase 1 — Test harnesses & CI (before any feature)

The five test projects are created and each proves itself with a real test, against the *template* app. No domain code yet — this is deliberate: if the harness only starts working once a feature exists, you can never tell which of the two is broken.

| Task | Acceptance |
|---|---|
| 1.1 `Mise.ArchitectureTests` + the first 3 boundary rules | Suite runs in <2s; deliberately adding a forbidden reference turns it red |
| 1.2 `Mise.UnitTests` skeleton + `GlobalUsings.cs` | One real test of a SharedKernel type passes |
| 1.3 `Mise.IntegrationTests`: `PostgresContainerFixture`, `CustomWebApplicationFactory`, `IntegrationTestBase`, `ProofTests.cs` | Two proof tests pass: anonymous `/health` → 200, and a seeded-row round-trip that is **gone** after the test (proving rollback) |
| 1.4 Second integration flavour: `CommittedWriteTestBase` + Respawn | A proof test writes, commits, is visible from a *second* connection, and is reset before the next test |
| 1.5 `Mise.Client.UnitTests` + `BunitTestContext` | One trivial RCL component renders; the `Bunit.TestContext` / `Xunit.TestContext` clash is documented in `GlobalUsings.cs` |
| 1.6 `Mise.E2ETests` + Playwright fixture | One test opens the Blazor host and asserts on a `data-testid` |
| 1.7 Aspire: add Postgres resource + persistent volume + migration strategy | `aspire run` brings up Postgres + API + Web; the app connects |
| 1.8 `.github/workflows/ci.yml` with all jobs + `ci-gate` + the tiered coverage gate | CI is green on a PR; deliberately breaking one suite turns `ci-gate` red |

**Exit:** all five suites green locally and in CI; `ci-gate` is the single required check.

### Phase 2 — Walking skeleton: create a reservation

The thin end-to-end slice that proves every seam. Deliberately **not** the full Reservations module — just enough to travel the whole path once.

**Minimum viable types:**

| Layer | Type |
|---|---|
| Domain | `Reservation` aggregate with `Id`, `CustomerName`, `PartySize`, `ReservationDateTime`, `Status` (`Confirmed` only for now); a factory method that rejects `PartySize <= 0` |
| Application | `CreateReservationCommand` + `CreateReservationCommandHandler` + `CreateReservationCommandValidator`; port `IReservationsData` (a slice-specific gateway, not a generic repository — see below) |
| Infrastructure | `ReservationsDbContext` (schema `reservations`), EF configuration, first migration, `ReservationsData : IReservationsData` |
| Contracts | `ReservationCreated` event, `ReservationDto` |
| API | `POST /api/reservations` → 201 + `Location`, 400 on validation failure |
| UI.Abstractions | `IReservationsClient.CreateAsync(...)` |
| UI.Components | `ReservationForm` component with `data-testid` on every field and the submit button |
| Web | A page hosting `ReservationForm`, wired to the HTTP client implementation |

**The test contract for this one slice — all five tiers, which is the entire point:**

| Tier | Test |
|---|---|
| Unit | `CreateReservation_PartySizeZero_ThrowsValidationException` · `CreateReservation_ValidCommand_PersistsViaGateway` (asserting `Times.Never` on `IReservationsData` in the failure case) |
| Integration | `PostReservation_ValidBody_Returns201WithLocation` · `PostReservation_PartySizeZero_Returns400WithFieldError` · `PostReservation_Unauthenticated_Returns401` |
| bUnit | `ReservationForm_PartySizeEmpty_ShowsInlineError` (the mockup's exact string: *"Party size is required and must be more than 0."*) · `ReservationForm_ValidInput_InvokesClientOnce` |
| E2E | `CreateReservation_HappyPath_AppearsInDayList` |
| Architecture | The boundary rules now have real assemblies to check rather than an empty set |

**Exit:** a reservation created in the browser lands in PostgreSQL and is proven at every tier. From here, every subsequent module repeats a pattern that is already known to work.

### Phases 3+ — module build-out

Sequenced to respect the charter's §12 dependency graph, with StaffIdentity early because it gates authorization everywhere else.

| Phase | Scope | Charter refs |
|---|---|---|
| 3 | StaffIdentity: Identity, two roles, policies, global fallback authorization | NFR-01, ADR-001 §Auth |
| 4 | Tables & Sections module (Manager-only CRUD, floor-plan layout) | FR-07, US-04 |
| 5 | Scheduling module (service periods, closed days) | FR-08 |
| 6 | Reservations proper: BR-01 overlap, BR-07 capacity/combinable, search | FR-01–03, US-01, US-02 |
| 7 | Seating & table status + cross-module domain event | FR-05, FR-06, BR-04, BR-05, RISK-01 |
| 8 | SignalR hub + live propagation | FR-10, NFR-04, US-03 |
| 9 | Audit logging (cross-cutting) | FR-09, NFR-02, US-05 |
| 10 | Shared RCL build-out + Blazor screens per the mockups | RISK-03 |
| 11 | Localization nl-BE / en | NFR-08 |
| 12 | **Spike 1** — Outbox/sync proof on a throwaway entity (timebox 2 days) | §12 Spike 1, RISK-07 |
| 13 | PIN + DeviceRegistration pairing | US-08, US-09, BR-11, RISK-09 |
| 14 | MAUI head + local encrypted SQLite | ADR-002 |
| 15 | Outbox + sync engine + server revalidation | ADR-002, US-07 |
| 16 | ConflictRecord + Manager conflict queue UI | US-06, BR-08, BR-09 |
| 17 | Retention/anonymization nightly job | BR-10 |
| 18 | Containerization & deployment | §7 Q2 — **blocked until a platform is chosen** |

Spike 1 sits at phase 12 rather than first (where charter §13 puts it) because decision #7 was to get the web slice green first — but it still lands *before* phases 14–16 commit to a design, which is the property the charter actually cares about.

### Things that must land early because retrofitting them is a project, not a refactor

This is the most important table in the plan. Each item is cheap now and expensive-to-impossible later, and several of them are only obvious once you look ahead to the offline phases.

| Must land in | What | Cost if retrofitted |
|---|---|---|
| Phase 0 | `global.json`, CPM, `TreatWarningsAsErrors`, `.editorconfig` + `.gitattributes` | Turning warnings into errors after 15 phases is a multi-day cleanup nobody ever schedules |
| Phase 0 | **Module-owned Postgres schemas + per-module migration history tables** | Splitting a shared schema once data exists is a migration project |
| Phase 1 | All five tiers + hard CI gates | Retrofitted harnesses don't happen; the coverage ratchet only works if it starts high |
| Phase 2 | **`OperationId` idempotency on every mutating endpoint** + a `shared.processed_operation` table | The single cheapest piece of insurance here. The offline Outbox (phase 15) *requires* exactly-once replay; adding it later is a wire-contract **and** schema break across every endpoint |
| Phase 2 | **`TestIds` constants class** shared by bUnit and Playwright | Otherwise a rename sweep across two test projects |
| Phase 3 | Auth spine + global fallback authorization policy | Rewrites the arrange step of every integration and E2E test written before it |
| Phase 3 | Password-hasher strategy incl. a rehash-on-login path | Once real users exist, changing the algorithm strands every stored hash. This is the charter's open Argon2id-vs-PBKDF2 question — the *strategy* is not deferrable even though the *choice* is |
| Phase 3 | Localization wiring (`IStringLocalizer`, `.resx`, culture provider) — content can come later | Retrofitting i18n across every screen is the classic project-sinking task |
| Phases 4–6 | **`xmin` concurrency tokens on `Table` and `Reservation` from the first migration** | Adding optimistic concurrency later invalidates every mutation test and every client's error handling (NFR-03) |
| Phase 8 | Frozen SignalR event names + an `IFloorPlanStream` abstraction | They are a client contract MAUI depends on; RISK-03's stated fix covers the request path but *not* the real-time path |
| Phase 9 | Audit **write** side, atomic with the mutation | Touching every write path a second time across four modules. (The audit *history UI* is freely deferrable.) |

The `OperationId` and `IFloorPlanStream` items are additions to the charter — neither appears in ADR-001 or ADR-002's task lists, but both are prerequisites for the offline work to be additive rather than a rewrite.

### One detail easy to miss in Phase 0

`src/aspire.config.json` contains `{"appHost": {"path": "Mise.AppHost/Mise.AppHost.csproj"}}` — a path relative to `src/`. Moving the solution to the repo root without correcting this file breaks `aspire run`. It must be fixed in the same commit as the move.

---

## The per-feature test contract

This goes into `CLAUDE.md` and is the rule every later session follows. It is FamilySplit's matrix, translated to Clean-Architecture-per-module. The hard rule it encodes, verbatim from their CLAUDE.md: *"Every new feature, endpoint, component, or business-rule change must ship with tests across all relevant projects. This is not optional."*

| When you add… | You must add |
|---|---|
| A Domain entity / value object with an invariant | Unit test per invariant, including the boundary that violates it |
| An Application command handler | Unit test with the port mocked (Moq) — never a database. Failure cases also assert `Times.Never` on the persisting port |
| An Application query handler or a port implementation | Integration test against Testcontainers |
| A new port interface | Mock it in the consuming handler's unit test **and** integration-test its real implementation |
| A validator | One test per rule + one happy path, asserting the **exact** message string (it is what the UI displays) |
| An API endpoint | Integration test: happy path, validation rejection on the right field, **permission boundary (wrong role → 403)**, unauthenticated → 401 |
| A mutating endpoint | All of the above **plus** an audit-entry assertion and an `OperationId` replay test (same id twice → one effect) |
| An RCL component | bUnit test per meaningful prop variant + per permission-gated control |
| A `data-testid` | Use it in the corresponding E2E test in the same commit |
| A user-visible flow | One Playwright E2E test |
| A cross-module interaction | Integration test proving the event handler ran and the other module's state changed |
| Anything time-dependent | A `FakeTimeProvider` test pinning the boundary — never a test that would pass at any time of day |
| A new module | Register it in the architecture-test assembly registry (a rule fails if the registry and the host's real references disagree) |

## Definition of Done

A feature is done when all of the following are true — not when it works on screen:

1. `dotnet build Mise.slnx -c Release` is clean with `TreatWarningsAsErrors`.
2. All five suites pass locally.
3. The test contract above is satisfied for every kind of thing the feature added.
4. The acceptance criteria of the charter user story it implements each map to at least one named test.
5. Domain + Application coverage for the touched assemblies is ≥90%.
6. `dotnet format --verify-no-changes` is clean.
7. No new architecture-rule suppressions.
8. `CLAUDE.md` is updated if the feature introduced a new pattern, and the README runbook still matches reality.
9. CI is green and `ci-gate` passes.
10. One commit, with a message stating what landed and the resulting test counts (FamilySplit's habit, and it makes regressions in the *number* of tests visible).

---

## Verification

### After every phase

```bash
dotnet build Mise.slnx -c Release
dotnet test tests/Mise.ArchitectureTests/Mise.ArchitectureTests.csproj --no-build -c Release
dotnet test tests/Mise.UnitTests/Mise.UnitTests.csproj --no-build -c Release
dotnet test tests/Mise.Client.UnitTests/Mise.Client.UnitTests.csproj --no-build -c Release
dotnet test tests/Mise.IntegrationTests/Mise.IntegrationTests.csproj --no-build -c Release
dotnet test tests/Mise.E2ETests/Mise.E2ETests.csproj --no-build -c Release
dotnet format Mise.slnx --verify-no-changes --no-restore --severity warn
```

### Phase-0 specific — prove the SDK pin actually took

```bash
dotnet --version
```

Must print `10.0.401`. If it prints `11.0.100-preview.7`, `global.json` is missing or malformed and every subsequent build is running on a preview SDK.

### Running the app

```bash
aspire run
```

Brings up PostgreSQL, the API host, and the Blazor Server host, with the Aspire dashboard for logs and traces. The README runbook written in Phase 0 documents this plus the migration workflow.

### Proving the harnesses actually work (Phase 1 — do this deliberately)

A test harness that has never failed is not yet known to work. Before declaring Phase 1 done:

- Add a deliberately forbidden project reference → the architecture suite must go red.
- Break one assertion in an integration proof test → CI must go red and `ci-gate` must fail.
- Write a row in an integration test and assert it is **absent** in the next test → proves rollback isolation.
- Drop coverage below the threshold on a Domain assembly → the coverage gate must fail the build.

---

## Risks & guardrails

Every risk the charter's Technical Analyst named (§12), plus the ones found while planning, mapped to the automated mechanism that actually catches it — not a process note or a reminder, a thing the build or CI does.

| Risk | Source | Automated guardrail |
|---|---|---|
| RISK-01 — Reservations/Tables eventual consistency lag | Charter | Fixed by charter-correction #10 (synchronous-within-request dispatch); proven by `SeatReservation_MarksTableOccupied_InSameRequest` |
| RISK-03 — Shared RCL couples to one transport | Charter | Architecture rule: `Mise.UI.Components` references only `Mise.UI.Abstractions`. A forbidden reference is a same-second build failure, not a code-review judgment call |
| RISK-05 — Simultaneous offline double-booking is inherent, not a bug | Charter | `Outbox_TwoDevicesOfflineBookSameTableOverlapping_OneSyncsCleanOneLandsInConflictQueue` proves the *designed* outcome (one clean, one conflicted) rather than trying to prevent the inherent case |
| RISK-06 — Local device data security (lost/stolen tablet) | Charter | SQLCipher encryption-at-rest verified by a test that opens the local `.db` file with the wrong key and asserts failure. MDM is an environment control outside code — named as an open question, not silently assumed |
| RISK-07 — Sync engine correctness (highest-risk new component) | Charter | The `VirtualDevice`/`TwoDeviceHarness` suite in "Testing the hard subsystems" above, run on every push (no container, no HTTP — real SQLite files only, so it's fast enough to gate) |
| RISK-09 — PIN brute-force if device binding erodes | Charter | Charter-correction #6 (server-issued device secret, not just a client GUID) + `PinLogin_ValidPinWrongDeviceSecret_Rejected`. Any future PR that weakens this binding fails that test, which is the point |
| New — BR-01 TOCTOU race | Found while planning | Postgres exclusion constraint (correction #1) + a genuine concurrent-insert test, not just an application-level check |
| New — retention job silently skips its own audit trail | Found while planning | CTE/`RETURNING` fix (correction #2) + `RetentionJob_AnonymizesExpiredReservation_WritesExactlyOneMatchingAuditEntry` |
| New — lost update on `Table`/`Reservation` edits (NFR-03) | Found while planning | `ETag`/`If-Match` round-trip (correction #5) + a deterministic two-context concurrency test — no real threading needed for this one |
| New — audit trail has a silent gap for a future endpoint | Found while planning | The `SaveChangesInterceptor` + architecture rule pair described under "Audit completeness" — the one guardrail explicitly designed to catch something *not yet written* |
| New — coverage gate flaps or gets gamed | This plan's own choice | Tiered by layer (Domain/Application only, 90%) rather than a global number, so it can't be satisfied by testing trivial code; ratchets up only after sustained headroom, never down silently |
| New — xUnit v3 / bUnit `TestContext` collision silently miscompiles a test file | Verified from FamilySplit | `GlobalUsings.cs` never carries `global using Bunit;`; documented inline so the next person (or the next session) doesn't reintroduce it |

---

## Open questions — and which phase each actually blocks

Separated into "blocks work" and "has a safe configurable default", because the charter leaves several open and only some of them matter to starting.

### Blocking

| Question | Charter ref | Blocks | Why it can't ride as a default |
|---|---|---|---|
| **Hosting platform** | §7 Q2 | Phase 18 (deployment) only | Explicitly deferred to DevOps. Every module is host-agnostic, so nothing before phase 18 is blocked. Neon is now on the table since the DB is PostgreSQL and you already run Neon for FamilySplit. |
| **How a "combinable set" of tables is modelled** | BR-07, §10 | Phase 6 | `IsCombinable` is a boolean on `Table`, which is not enough to express *which* tables combine with which. Either a self-referencing group entity or an explicit `TableGroup` is needed before BR-07 can be implemented or tested properly. ~6 sessions of runway. |
| **Argon2id or ASP.NET Identity's default PBKDF2** | ADR-001 §Security | Phase 3 | The charter flags this honestly rather than silently using the framework default. The *choice* can be late; the **rehash-on-login migration path** cannot, so phase 3 ships that path regardless. |

### Safe as configurable defaults (decision #7)

| Parameter | Default taken | Charter ref |
|---|---|---|
| Retention period | 24 months since last interaction | §7 Q5, BR-10 |
| Offline auth grace period | 12 hours (one shift) | ADR-002 |
| PIN lockout threshold | 5 attempts, then website password reset | ADR-001 Amendment 2 |
| Default turn time | 90 minutes | §10 `DurationMinutes` |

Each is a setting read from configuration, and each has a test that drives the behaviour *from* configuration rather than asserting the literal number — so changing it later is a config edit, not a test rewrite.

### Not a blocker, but worth an answer before phase 14

**MAUI vs Blazor device split** (§7 Q3 — "decide once we see the screens"). The mockups now exist and show a Tablet/Desktop toggle over the *same* screens, which suggests the split is about form factor rather than about different feature sets. Worth confirming, because if the tablet can simply run the Blazor site in a browser, the MAUI head's only real justification is offline (NFR-09) — and that in turn is what the ~6-week phase 14–16 block buys.

**MDM for the tablets** (RISK-06, status OPEN in the charter). Not a code decision, but it changes how much the SQLCipher-at-rest mitigation is actually worth.

---

## Corrections to the charter, found while planning

A plan-first process is supposed to catch this before code exists rather than after. These are concrete defects — not style preferences — in the charter's own data model and SQL. Each gets a fix and a named test that proves the fix.

| # | Problem | Consequence if shipped as specified | Fix |
|---|---|---|---|
| 1 | **BR-01 ("no two overlapping reservations on one table") is enforced only in application code** — a check-then-insert. Under concurrent requests this is a classic TOCTOU race: two staff members can each pass the check before either commits. `xmin` doesn't help here — there's no stale *row* to detect when the conflict is between two *new inserts*. | Goal #2 in the charter ("zero double-booked tables … target 0") is silently unachievable, and NFR-03's "must not silently overwrite" is unmet for this specific case. | Enforce BR-01 **at the database** with a Postgres exclusion constraint: `CREATE EXTENSION btree_gist;` then `ALTER TABLE reservations.reservation ADD CONSTRAINT no_overlapping_reservations EXCLUDE USING gist (table_id WITH =, tstzrange(reservation_date_time, reservation_date_time + (duration_minutes || ' minutes')::interval) WITH &&) WHERE (status NOT IN ('Cancelled','NoShow'));`. The application-level check stays, purely so a violation surfaces as a clean 409 with a friendly message rather than a raw constraint-violation exception. **Test:** `CreateReservation_TwoConcurrentOverlappingInserts_ExactlyOneSucceeds` — two real, deliberately-interleaved transactions (a `SemaphoreSlim` barrier so both `INSERT`s are in flight together), asserting one commits and one throws with SqlState `23P01`. |
| 2 | **The retention job's own SQL (charter §10) has a bug the charter's comment misdescribes.** Within one transaction, the `INSERT … SELECT … WHERE is_anonymized = FALSE` runs *after* the `UPDATE` has already set `is_anonymized = TRUE` on exactly the rows it's trying to select — Postgres always sees a transaction's own prior writes. The `SELECT` matches **zero** rows. | The nightly job silently anonymizes customer PII with **no audit entry** — a direct, shipped-on-day-one violation of NFR-02 and the charter's own stated intent ("never silent"). | Use a CTE with `RETURNING` so the audit insert is driven by exactly the rows that were changed, in one atomic statement: `WITH anonymized AS (UPDATE … RETURNING id) INSERT INTO shared.audit_log_entry (…) SELECT …, id, … FROM anonymized;`. **Test:** `RetentionJob_AnonymizesExpiredReservation_WritesExactlyOneMatchingAuditEntry`, plus `RetentionJob_RunTwice_SecondRunChangesNothing` (idempotency). |
| 3 | **BR-10 anonymizes `CustomerPhone`/`CustomerEmail` but not `Notes`**, even though the charter itself documents Notes as holding "allergies, occasion, VIP flag, seating preference." Allergy data is special-category health data under GDPR Art. 9 — stricter than the phone number being nulled next to it. | A "delete the PII" job that leaves the more sensitive field behind. | Extend BR-10 to null `Notes` on anonymization too; add it to the CTE fix above. **Test:** assert `Notes` is null post-anonymization alongside phone/email. |
| 4 | **`ConflictRecord.SubmittedPayloadJson` and `AuditLogEntry.Details` retain raw PII indefinitely** — including, after fix #2/#3 land, the exact phone number and notes the retention job just deleted from the `Reservation` row itself. | A retention job that deletes a phone number from one table while a permanent copy of it sits in two others. | Redact PII fields when writing `Details`/`SubmittedPayloadJson` (store an ID-shaped reference plus non-PII fields, not a raw copy of customer contact data). **Test:** an integration test asserting neither field ever contains a value matching the phone/email pattern of a real reservation. |
| 5 | **`xmin`-based optimistic concurrency has no client round-trip.** Nothing in the charter's API contracts (§10 Sample API Contracts) has the client send back the version it read. Without an `ETag`/`If-Match`, the server just re-reads current state and overwrites — the "protection" is vacuous, and NFR-03's "must not silently overwrite" is untested-as-true because there's nothing for a test to exercise. | Two managers editing the same table's capacity: the second save always wins silently — exactly what NFR-03 forbids. | Every mutating endpoint on a concurrency-tracked aggregate (`Table`, `Reservation`) requires an `ETag`/`If-Match` header carrying the `xmin` the client last read; missing header → `428 Precondition Required`; stale value → `409` with the current state. **Test:** `UpdateTable_StaleIfMatch_Returns409WithCurrentState`; `UpdateTable_MissingIfMatch_Returns428`. Deterministic concurrency test needs no real threading here — load the same row via two separate `DbContext` instances, save the first (succeeds, `xmin` advances), then save the second (must throw `DbUpdateConcurrencyException`). |
| 6 | **Device pairing's only factor is a client-generated GUID** (`DeviceId`, stored in `SecureStorage`). ADR-001 Amendment 2's whole argument for why a short PIN is safe is "bound to a specifically paired, revocable device" — but a client-generated, unauthenticated GUID is not a secret the server can trust; if it ever leaks (log line, backup, another app on the same device), the PIN reverts to being a bare, brute-forceable credential. This is RISK-09 made concrete. | The compensating control ADR-001 relies on for PIN safety doesn't actually hold up as a security property. | Pairing issues a **server-generated device secret** (not just an id), which must be presented (e.g. as a second bearer factor or HMAC) on every PIN-login call, not only the client's self-asserted `DeviceId`. **Test:** `PinLogin_ValidPinWrongDeviceSecret_Rejected` alongside the existing `PinLogin_UnpairedDevice_Rejected`. |
| 7 | **`DeviceRegistration.IsActive` is (implicitly) checked only at login**, not per subsequent request. US-09's own acceptance criterion says a deactivated device should reject "every *new* PIN login attempt … as soon as the device has connectivity" — but if the JWT issued at login stays valid for its full lifetime regardless of what happens to `IsActive` afterward, revocation does nothing until the token expires or the device tries to re-authenticate. | A "revoke this stolen tablet" action that doesn't actually revoke an already-issued session, contradicting what a Manager would reasonably expect "deactivate" to mean. | Carry `device_id` as a JWT claim and re-check `DeviceRegistration.IsActive` on **every** authenticated request from a MAUI client (a lightweight per-request check, cached briefly), not only at token issuance. **Test:** `AuthenticatedRequest_FromNowDeactivatedDevice_Returns401EvenWithUnexpiredToken`. |
| 8 | **The 12-hour offline grace period (ADR-002) is described in terms of wall-clock elapsed time on the device itself** — trivially defeated by rolling the tablet's clock back, which yields an indefinitely-extendable "offline" session. | The one stated bound on offline access has no real teeth. | The real enforcement point is the **server-signed token's `exp` claim**, computed at issuance from the *server's* clock — the grace period is "how long the token is valid for," not "how long the device believes it's been offline." **Test:** a token minted with `exp` 1 second in the past is rejected regardless of the device's reported local time. |
| 9 | **`ServicePeriod` (date + start/end time) can't represent a service that crosses midnight** — a "Dinner 18:00–01:00" period is unrepresentable, and therefore FR-08/closed-day validation is untestable for exactly the shift where it matters most in a restaurant. | Either dinner has to end artificially at 23:59, or the model silently mishandles the common case. | Add `EndsNextDay bool` (or model as a duration from `StartTime`) so a period can span midnight. **Test:** a reservation at 00:30 falls inside a Dinner period that started at 18:00 the prior calendar date. |
| 10 | **Cross-module consistency (RISK-01) is written as an async, fire-and-forget domain-event handoff** between Reservations and Tables. Fully async makes "Seat → table Occupied" fundamentally non-deterministic to test — you're pushed toward polling, and polling under a hard "no `Thread.Sleep`" rule pushes toward flaky tests. | Either the test suite quietly grows a polling/retry pattern that violates the project's own no-sleep rule, or the cross-module handoff goes untested. | Dispatch the domain event **after the triggering commit, but awaited within the same request**, before the response returns — RISK-01's accepted "milliseconds to low seconds" window survives (it's now milliseconds within one request), and the test becomes an ordinary synchronous integration test: `SeatReservation_MarksTableOccupied_InSameRequest`. |
| 11 | **`ConflictRecord`'s foreign keys, at EF's default cascade behavior, could delete a conflict row if the entity it references is later deleted** — quietly contradicting the charter's own explicit guarantee (US-06: "a conflict left unresolved … never silently expires"). | "Never auto-expires" passes every functional test right up until an unrelated deletion elsewhere silently removes the row. | `DeleteBehavior.Restrict` on every FK into `ConflictRecord`. **Test:** a model-level test asserting the configured delete behavior, plus an integration test that deleting the referenced entity throws rather than cascading. |

---

## Testing the hard subsystems

The five-tier setup above handles ordinary CRUD-shaped features well. These subsystems don't fit that mould and need a deliberate approach or they end up either untested or flaky.

### Offline sync / Outbox (ADR-002, RISK-07 — "the highest-risk new component")

Testable **without a real device or real network**, by putting three seams behind interfaces in `Mise.Mobile.Core` (Application layer): `ISyncTransport` (replaces `HttpClient`, lets a test inject failures), `IConnectivityMonitor` (replaces `Microsoft.Maui.Networking`), `ITrustedClock` (a monotonic-anchored clock for the offline grace period — see fix #8 above). A `VirtualDevice` test harness wraps a real local SQLite file (not mocked — the Outbox's own durability is exactly what's under test) with a `FaultInjectingSyncTransport` standing in for the network.

Cases the naive plan would miss: exactly-once replay under duplicate delivery (the `OperationId` from decision-table item "Phase 2" is what makes this provable); a simulated crash — kill and reopen the `VirtualDevice` mid-drain, between "server applied" and "local ack received" — and assert no double-apply and no loss; out-of-order arrival; an operation whose target was deleted server-side in the meantime; a full Outbox; partial-drain failure and resumption; replay after a long offline period. Two `VirtualDevice`s driven by one `TwoDeviceHarness` reproduce the charter's own Spike 1 acceptance test as a permanent, named test rather than a one-off prototype: `Outbox_TwoDevicesOfflineBookSameTableOverlapping_OneSyncsCleanOneLandsInConflictQueue`.

### Conflict detection & resolution (US-06, BR-08, BR-09)

Detection is just "run the same server-side revalidation the online path already runs" applied to a replayed operation — so it reuses BR-01/BR-07's tests rather than inventing new rules. Resolution needs: an audit entry attributing who resolved it and how; BR-07 re-checked on `Reassign` (accepting a resolution must not itself create a new conflict); a `ConflictRecord` that never disappears on its own (fix #11 above covers the DB side; add `ConflictRecord_NeverAutoResolves_RegardlessOfElapsedTime` using `FakeTimeProvider` advanced by months); and a conflict whose underlying entity changed again before a Manager acts on it (the resolution re-validates against *current* state, not the state at detection time).

### Real-time (FR-10, NFR-04, US-03)

Assert *arrival*, never *timing*, on every push: `TaskCompletionSource` set from the hub event handler, awaited with a generous timeout — never a literal "under 1.5 seconds" assertion in the gated suite (that's exactly the kind of test that gets quarantined, then ignored). The `HubConnection` in an integration test must be pinned to `HttpTransportType.LongPolling` against `WebApplicationFactory`'s `TestServer` — WebSockets don't survive it. Authorization on hub methods gets the same 401/403 treatment as REST endpoints. The actual ~1–2s NFR-04 budget is a **nightly** performance test (p95 over N events) plus a production OpenTelemetry histogram with an alert — not a per-push gate.

### PIN + device pairing (US-08/US-09, BR-11, RISK-09)

The security matrix: unpaired/deactivated device rejected regardless of PIN correctness; 5-attempt lockout scoped per (staff, device) pair, never auto-expiring via more guesses; only a Manager can pair (403 for Floor Staff, checked server-side — never only hidden in the UI); PIN change requires the full website password; PIN hash (`IPinHasher`, Argon2id) kept entirely separate from the website password hash; the 12-hour grace period enforced via the server-signed `exp` (fix #8); and the device-secret hardening from fix #6. Include the negative case the charter itself names honestly: a device deactivated while an offline session is already running keeps working until its token expires or it reconnects — write the test that proves *that specific, bounded* behavior, not a false guarantee of instant revocation.

### Retention job (BR-10)

Once fix #2's CTE lands: atomicity is structural (one statement), so the interesting tests are idempotency (`RunTwice_SecondRunChangesNothing`), the boundary (a reservation whose `RetentionExpiryDate` is one second in the future is untouched), and the exactly-one-of invariant on `AuditLogEntry` (`PerformedByStaffId` XOR `PerformedBySystemProcess`). Model it as `IRetentionJob.RunOnceAsync(TimeProvider, ct)` in the Application layer rather than raw SQL fired by `pg_cron` — that's what makes it callable directly from a test with a `FakeTimeProvider`, no cron scheduler involved.

### Shared RCL across two hosts (RISK-03)

bUnit tests run **once**, against `Mise.UI.Components` with a mocked `IReservationsClient`/`ITablesClient`/`IFloorPlanStream` — they never know or care which concrete transport is behind the interface, which is the entire point. What needs a *second* thing tested is that both concrete client implementations (there's only one in the two-host design — the HTTP+SignalR client used identically by `Mise.Web` and `Mise.Maui`) actually satisfy the interface's behavioral contract, not just its type signature: one shared `ClientContractTests` suite, run against the real HTTP client in an integration test, asserting things like "a 404 from the API surfaces as the client interface's documented not-found result, not an unhandled exception."

### Audit completeness (FR-09, NFR-02, US-05) — catching a *missing* audit entry, not just a wrong one

The hard part isn't asserting an audit entry is correct; it's catching that a **newly added** mutating endpoint forgot one entirely — the kind of gap a normal test plan has no mechanism to notice, because nobody writes a test for a thing they forgot to build. Two complementary mechanisms: an EF `SaveChangesInterceptor` that fails the save if the `DbContext` was flagged as having performed a mutating operation but no `AuditLogEntry` was added in that same `SaveChanges` call (a runtime guarantee, catches it the first time the endpoint is exercised by *any* test); and an architecture rule asserting every `*CommandHandler` type's constructor takes a dependency whose type implements `IAuditWriter` (a static guarantee, catches it at compile/architecture-test time, before the endpoint is ever called). Belt and suspenders — the interceptor catches a handler that has the dependency but forgets to call it; the architecture rule catches a handler that never took the dependency at all.

### Production-code seams this testing strategy requires

| Seam | Layer | Exists purely so that… |
|---|---|---|
| `TimeProvider` (BCL) | every composition root | all of the above — time is injectable everywhere |
| `ITrustedClock` | `Mise.Mobile.Core` (Application) | the offline grace period can't be defeated by rolling the device clock (fix #8) |
| `ISyncTransport` | `Mise.Mobile.Core` (Application) | sync tests inject faults without a real network |
| `IOutboxStore`, `ILocalFloorPlanStore` | `Mise.Mobile.Core` (Application port / Infrastructure adapter) | tests exercise a real local SQLite file, not a mock, for the thing whose durability is under test |
| `IConnectivityMonitor` | `Mise.Mobile.Core` (Application) | replaces `Microsoft.Maui.Networking` for deterministic offline/online transitions in tests |
| `IReservationsClient`, `ITablesClient`, `IFloorPlanStream`, `ISyncStateProvider` | `Mise.UI.Abstractions` | the RCL is testable once (RISK-03) and both hosts share one contract-tested implementation |
| `IAuditWriter` | `Mise.SharedKernel.Infrastructure` | a single choke point for the audit interceptor above to police |
| `IPinHasher`, `IPinAttemptCounter` | `Mise.Modules.StaffIdentity.Application` | Argon2id and lockout state stay out of unit tests entirely; both are mockable |
| `IReservationsData` / `ITablesData` / … | `{M}.Application` (port) | the fast unit tier — see "The testable seam" above |

**Deliberately not added** — these would be anti-seams here: a generic `IRepository<T>`/`IUnitOfWork` (already rejected above — `DbSet<T>` is already the abstraction, and a generic wrapper pulls toward the EF InMemory provider, which cannot represent the exclusion constraint, `xmin`, or `pg_trgm` this project's correctness actually depends on); a custom `IDateTimeProvider` (the BCL `TimeProvider` + `FakeTimeProvider` already does this, including `CreateTimer` for backoff tests — a custom interface would just lose that); a second layer of indirection over `IHubContext<T>` in addition to a realtime-notifier interface (pick one).

---