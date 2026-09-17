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
- **Every boundary that can fail unexpectedly catches it, logs it, and shows the user something
  short of a crash or a leaked stack trace.** Not just the expected-failure paths (a validation
  rejection, wrong credentials) — the *unexpected* ones (the API is unreachable, a bug, a DB
  outage). See "Error handling" below for what this looks like at each layer.

## Module boundaries (enforced by `Mise.ArchitectureTests`, not just this doc)

Each of the four modules (Reservations, Tables, StaffIdentity, Scheduling) is four projects:
`{Module}.Domain`, `{Module}.Application`, `{Module}.Infrastructure`, `{Module}.Contracts`.

| From | May reference | Must never reference |
|---|---|---|
| `{M}.Domain` | `Mise.SharedKernel` only | Anything else — no EF, no ASP.NET, no other module |
| `{M}.Application` | own `.Domain`, `Mise.SharedKernel`, **other modules' `.Contracts`** | any other module's `.Domain`/`.Application`/`.Infrastructure`; EF Core; ASP.NET Core |
| `{M}.Infrastructure` | own `.Domain` + `.Application`, `Mise.SharedKernel*`, `Mise.ServiceDefaults` (see below) | another module's anything |
| `{M}.Contracts` | `Mise.SharedKernel` only | its own module's `.Domain`/`.Application`/`.Infrastructure` |
| `Mise.UI.Components` | `Mise.UI.Abstractions` only | any module, any host project — this is what lets the same components render under both Blazor Server and MAUI |
| `Mise.Web` / the MAUI head | `Mise.UI.Components`, `Mise.UI.Abstractions` | any module project directly |

A new module gets registered in the architecture-test assembly registry in the same commit —
a rule fails if the registry and the host's real project references disagree, so a module can't
silently escape every other boundary rule.

`{M}.Infrastructure → Mise.ServiceDefaults` is a deliberate, narrow allowance added in Phase 3:
`Mise.ServiceDefaults` is a leaf shared library (no module, no EF, no host-specific state), and
whichever code mints or validates a JWT needs the same `Issuer`/`Audience`/`SigningKeyConfigKey`
constants (`JwtAuthDefaults`) regardless of which side of the request it's on — first
`Mise.Web`'s now-deleted placeholder token handler, now `StaffIdentity.Infrastructure`'s
`JwtTokenIssuer`. Duplicating those three literal strings per consumer is a silent-drift risk
(mint and validate disagree, tokens fail with a confusing 401); referencing the one place they
live is not.

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
table — see the per-feature test contract's mutating-endpoint row) moved to
**`Mise.SharedKernel.Persistence`** in Phase 3, when StaffIdentity became the second module
needing them (Reservations, as of Phase 2, was the first) — CLAUDE.md's own earlier text said
this is exactly when to decide, and the shape of the decision was forced by one constraint:
`Mise.SharedKernel.Infrastructure` (where `IAuditWriter`/`AuditLogEntry` live) **must stay
EF-free forever**, because every module's `Application` project references it directly for
`IAuditWriter` — adding EF Core there would leak EF onto every `Application` project
transitively, exactly what the boundary table forbids. `Mise.SharedKernel.Persistence` is the
new, separate EF-aware project that holds the entity (`ProcessedOperation`), its
`IEntityTypeConfiguration`s, an `ApplySharedKernelConfigurations(isOwner:)` `ModelBuilder`
extension, and a generic `AuditWriter<TDbContext> : IAuditWriter` — referenced only by module
`Infrastructure` projects, never `Application` (that reference stays to
`Mise.SharedKernel.Infrastructure` alone).

Exactly one module's migration may contain the `CreateTable` calls for these two physical
tables (`isOwner: true`, still Reservations); every other module maps the same tables with
`isOwner: false`, which sets EF's `ExcludeFromMigrations()` on them — its own migration then
only reads/writes rows, never tries to re-create a table that already exists. This makes
migration **order** load-bearing: `Mise.MigrationService` must run the owner's migration
before any non-owner's (see its `Program.cs` comment). A third module doing this differently
(or a fourth, non-owning module skipping the `isOwner: false` flag) is exactly the kind of
mistake that surfaces as "relation already exists" the first time all migrations run together
against a clean database — `MigrationHistoryTests.EachModule_HasItsOwnDistinctMigrationsHistoryTable`
is the regression test for the *symptom* (two distinct history tables), not a guarantee against
this specific mistake; there is no architecture-test guardrail for it yet.

## Staff auth spine (StaffIdentity, Phase 3 — real sign-in)

`Mise.ApiService` runs a JWT-bearer scheme (`Mise.ServiceDefaults.JwtAuthDefaults` for the
issuer/audience/config-key constants — named `PlaceholderAuthDefaults` through Phase 2, when
there was no real sign-in yet to validate against) with a global fallback policy that requires
authentication by default — endpoints opt out with `AllowAnonymous` (`/health`, `/alive`,
`/api/auth/login`) rather than opting in one by one. Two policies sit on top of that:
`"FloorStaff"` (`RequireRole(FloorStaff, Manager)` — an OR, so a Manager satisfies it too,
ADR-001 §Auth's "Manager policy implies Floor Staff permissions") and `"Manager"`
(`RequireRole(Manager)` only). The validation side (the JWT-bearer scheme itself) is exactly
what Phase 2 already wired; only the **minting** side moved, from `Mise.Web`'s deleted
`PlaceholderAuthTokenHandler` (one fixed system identity for every request) to
`StaffIdentity.Infrastructure`'s `JwtTokenIssuer` (a real token per signed-in staff member, with
`NameIdentifier`/`Name`/`Role` claims). The signing key is still the same secret Aspire
parameter (`jwt-signing-key`, set once via `dotnet user-secrets set` from `src/Mise.AppHost` —
see the README), shared by both hosts, never hardcoded.

`RegisterStaffCommand`/`LoginCommand` follow the same handler shape as
`CreateReservationCommandHandler` (validate-then-throw, gateway mocked in unit tests, exactly
one `IAuditWriter` call per real effect) — see `Mise.Modules.StaffIdentity.Application`.
`RegisterStaffAsync`'s atomicity (the Identity user, the `StaffUser` profile row, and the
`ProcessedOperation` row all commit or fail together) needs one extra step beyond
`ReservationsData`'s own pattern: `UserManager.CreateAsync` normally calls `SaveChangesAsync`
itself, so `StaffIdentityData` resolves `IUserStore<StaffIdentityUser>` separately (not via
`UserManager.Store`, which is `protected`) and sets `AutoSaveChanges = false` on it right
before calling `CreateAsync`, so the later explicit `SaveChangesAsync` covers everything.

Nobody could ever reach the Manager-only register-staff endpoint without a Manager already
existing — `Mise.MigrationService` seeds exactly one bootstrap Manager
(`StaffIdentitySeeder.EnsureManagerExistsAsync`, idempotent) from two more Aspire parameters,
`seed-manager-username` (a safe default, `"manager"`) and `seed-manager-password` (secret, no
default — set the same way as `jwt-signing-key`).

### The Blazor Server split: a sign-in cookie and a server-side JWT, never both in the browser

`Mise.Web` authenticates its own browser sessions with an ordinary auth cookie
(`CookieAuthenticationDefaults`, `LoginPath = "/login"`) — that's what makes `[Authorize]`,
`AuthorizeView`, and `AuthorizeRouteView` work on Razor components. Separately, it holds the
API JWT a login actually returned, server-side only, in a singleton `IStaffSessionTokenCache`
keyed by staff id (never a cookie or `localStorage` value — ADR-004). Two consequences worth
knowing before touching this code:

- **`Login.razor` has no `@rendermode`.** Signing in calls `HttpContext.SignInAsync`, which
  needs to write a real `Set-Cookie` header on an ordinary HTTP response — impossible from
  inside an already-established SignalR circuit. This is the mirror image of this file's
  existing `@rendermode` gotcha below (that one needs interactivity *from the start*; this one
  must deliberately stay static SSR). Logout is a plain `GET /logout` minimal-API endpoint for
  the identical reason.
- **The per-user bearer token is attached inside the typed HTTP client itself
  (`HttpReservationsClient`), not a `DelegatingHandler` registered via
  `AddHttpMessageHandler<T>()`.** Handlers added that way are resolved from
  `IHttpClientFactory`'s own pooled, rotating internal scope — never the calling circuit's DI
  scope — so a scoped dependency like `AuthenticationStateProvider` injected into one would
  silently resolve the wrong instance. The typed client class itself, by contrast, **is**
  constructed fresh from the calling scope every time (only the underlying
  `SocketsHttpHandler` chain is pooled), so `AuthenticationStateProvider` resolves correctly
  there. Every future typed client that needs the caller's token follows
  `HttpReservationsClient`'s pattern, not a shared `DelegatingHandler`.

## Tables & Sections (Phase 4 — optimistic concurrency, and two charter corrections)

`Mise.Modules.Tables` follows `CreateReservationCommandHandler`'s shape exactly for its six
command handlers (`Create`/`Update`/`Deactivate` × `Section`/`Table`) — validate-then-throw,
gateway mocked in unit tests, `OperationId` idempotency on every one of them (not just Create;
CLAUDE.md's mutating-endpoint contract doesn't carve out an exception for Update/Deactivate, and
a client retrying a PATCH after a dropped response needs the same replay-safety a retried POST
gets). Two things this module had to decide that no earlier module needed to:

- **`Section.IsActive` (docs/plan.md correction #12) and `Table.Deactivate()`'s Reserved/Occupied
  guard (correction #13)** are the two gaps FR-07/US-04 forced — see those correction entries for
  the full reasoning. The load-bearing distinction between them: `Section`'s "still has active
  tables" check needs a cross-aggregate query (`ITablesData.AnyActiveTablesInSectionAsync`), so it
  lives in `DeactivateSectionCommandHandler` as a field-scoped `ValidationException` (same shape
  as `RegisterStaffCommandHandler`'s taken-username check, 400); `Table`'s guard only reads its
  own `Status` field, so it's a genuine Domain invariant that throws the new
  `Mise.SharedKernel.DomainRuleViolationException` instead (mapped to 409 by
  `DomainRuleViolationExceptionHandler`) — a business-rule violation self-contained within one
  aggregate is a Domain concern; one that needs a database query to answer is an Application
  concern. Don't conflate the two the next time this distinction comes up.
- **Optimistic concurrency (docs/plan.md correction #5) lands here for `Table`** (not `Section` —
  the charter never named it as concurrency-tracked, and last-write-wins is an accepted,
  documented scope decision for it) and is the first real implementation of the ETag/If-Match
  pattern the plan named back in Phase 0. The pieces:
  - **No `UseXminAsConcurrencyToken()` helper exists** in
    `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (removed/renamed since the charter's ADR-001
    Amendment 1 was written against an older version). Instead, declare an ordinary `uint` shadow
    property and call the **standard, provider-portable** `.Property<uint>("Version").IsRowVersion()`
    — Npgsql's own `NpgsqlPostgresModelFinalizingConvention.ProcessRowVersionProperty` convention
    silently detects any `uint`+`IsRowVersion()` property and maps it to the physical `xmin`
    system column regardless of the shadow property's own name (`TableConfiguration.cs`). No
    stored column is created — confirm this by reading the generated migration, which lists
    `xmin` as a column but the SQL generator no-ops it (Npgsql's own
    `NpgsqlMigrationsSqlGenerator.SystemColumnNames` special-case).
  - **`Mise.ApiService.ETag`** is the only place that formats/parses the wire value (a quoted
    decimal string, e.g. `"5"`) — Application/Domain never see anything but the raw `uint`
    (`ITablesData`'s `TableWithVersion`/`TableSaveResult`).
  - **Missing/malformed `If-Match` on `PATCH /api/tables/{id}` or `.../deactivate`** → the
    endpoint itself throws `PreconditionRequiredException` (an `Mise.ApiService`-internal type —
    unlike `ConcurrencyConflictException`, it never needs to cross out of the host, since parsing
    a request header is a pure HTTP-shape concern) → `PreconditionRequiredExceptionHandler` → 428.
  - **A stale `If-Match`** → the gateway's `SaveWithConcurrencyCheckAsync` sets the tracked
    entity's `Property<uint>("Version").OriginalValue` to the caller's claimed version before
    `SaveChangesAsync`; Postgres's own `UPDATE ... WHERE xmin = @original` affecting zero rows
    surfaces as `DbUpdateConcurrencyException`, caught and turned into
    `TableSaveOutcome.VersionMismatch` → the handler throws
    `Mise.SharedKernel.ConcurrencyConflictException` → `ConcurrencyConflictExceptionHandler` → 409.
  - **The current version on a 409 travels in the JSON body's `currentVersion` extension field,
    never a response `ETag` header.** ASP.NET Core's `ExceptionHandlerMiddleware` registers an
    `OnStarting` callback (`ClearCacheHeaders`) on **every** response it processes that
    unconditionally strips `ETag` (and sets `Cache-Control`/`Pragma`/`Expires`) as anti-caching
    hardening for error pages — it runs after any `IExceptionHandler`, so setting the header
    inside one is silently undone before the response is sent. This only affects
    exception-handler responses: the success path's `Results.Ok`/`Results.Created` ETag
    (`TablesEndpoints.cs`) is unaffected, since no exception was thrown for those. Don't
    rediscover this the hard way the next time a 4xx/5xx response needs a custom header.
  - **A non-existent `SectionId`/`TableId` on create/update is a field-scoped 400, not a raw
    FK-violation 500** — `CreateTableCommandHandler`/`UpdateTableCommandHandler` call
    `ISectionsData.GetSectionByIdAsync` before touching `ITablesData`, precisely because the DB's
    own FK constraint (`Table.SectionId → Section.Id`, `DeleteBehavior.Restrict`) is the only
    other thing that would catch it, and it would surface as an unhandled `DbUpdateException`.
- **`GetActiveSectionsAsync`/`GetFloorPlanAsync` are plain reads injected directly into their
  endpoints** (`ISectionsData`/`ITablesData`, no query-handler class) — no query-handler
  abstraction exists anywhere yet to mirror, and neither read has logic beyond a gateway call and
  a DTO projection. Introduce one for real once a future read actually needs it.
- **No Blazor UI for Tables/Sections yet** — same deferral Phase 3 made for staff management
  ("registering staff is API-only ... deferred to Phase 10"). Proven at the Architecture/Unit/
  Integration tiers only; the RCL screens land with Phase 10's shared-component build-out.

## Scheduling (Phase 5 — service periods & closed days)

`Mise.Modules.Scheduling` follows `CreateReservationCommandHandler`'s shape exactly for its
three command handlers (`Create`/`Update`/`Delete` `ServicePeriod`) — validate-then-throw,
gateway mocked in unit tests, `OperationId` idempotency on every one of them, exactly one
`IAuditWriter` call per real effect. FR-08's own charter text ("Managers can define service
periods/hours ... and mark days closed") got no acceptance criteria the way US-04 got them for
Tables/Sections — the charter's data model names one entity only (`ServicePeriod`: Date, Label,
StartTime, EndTime, IsClosed), so this phase had to resolve, and document, what US-04 didn't
need to:

- **One entity does both jobs, not two.** A row with `IsClosed = false` is a normal open window
  (Lunch, Dinner); a row with `IsClosed = true` is a closure — a whole day (`00:00`–`00:00`,
  `EndsNextDay: true`) or a partial one ("kitchen closed 14:00–17:00"). There is no separate
  "closed day" table or flag elsewhere.
- **docs/plan.md correction #9 (`EndsNextDay`) lands in `ServicePeriod`'s first commit**, not
  retrofitted — a plain Date+StartTime+EndTime can't represent "Dinner 18:00–01:00", and this is
  the phase where the entity is born. Domain invariant, self-contained within the aggregate
  (`ServicePeriod.Create`/`UpdateDetails`, `ArgumentOutOfRangeException` — same category as
  `Section.Create`'s guard clauses, not `Table.Deactivate()`'s `DomainRuleViolationException`,
  because it's a structural field-shape rule with no state or cross-aggregate query involved):
  a same-day window (`EndsNextDay: false`) must have `EndTime` strictly after `StartTime`; a
  next-day window (`EndsNextDay: true`) allows any combination, including `EndTime == StartTime`
  — which represents exactly 24 hours, so "closed all day" is just `00:00`–`00:00` with
  `EndsNextDay: true`, not a special-cased nullable start/end.
- **No `xmin`/`ETag` concurrency on `ServicePeriod`** — the same scope decision already made for
  `Section` (docs/plan.md correction #5 names only `Table`/`Reservation` as concurrency-tracked):
  infrequent, low-conflict Manager-config edits, last-write-wins.
- **Hard delete, not deactivate.** Unlike `Table`/`Section`, the charter's own data model gives
  `ServicePeriod` no `IsActive` column, so removing one is a real `DELETE`, not a soft flag —
  still `OperationId`-idempotent per the mutating-endpoint contract, which carves out no
  exception for delete. This forced one design point worth flagging for the next hard-delete
  endpoint that gets added: **a replay's idempotency check must run before the existence check**,
  not after. A soft-deactivate (`Section`/`Table`) can always find the row again on replay
  because it's still there; a hard delete makes the row disappear after the first successful
  call, so `ISchedulingData.DeleteServicePeriodAsync` checks `shared.processed_operation` by
  `OperationId` *first* and only falls through to a row lookup when that comes back empty —
  getting this order backwards was caught by `DeleteServicePeriod_SameOperationIdTwice_ReturnsNoContentBothTimesAndDeletesOnlyOnce`
  failing with a 404 on the second call during development, not by inspection.
- **The delete endpoint's `OperationId` travels as a query parameter (`DELETE
  /api/service-periods/{id}?operationId=...`), not a JSON body** — unlike POST/PATCH, a DELETE
  with a request body has no precedent anywhere else in this codebase and minimal APIs bind
  query parameters far more naturally than a DELETE body.
- **`Manager`-only mutations, `FloorStaff`-readable `GET /api/service-periods?date=...`** —
  mirrors `SectionsEndpoints` exactly. `GetServicePeriodsForDateAsync` is a plain read injected
  directly into its endpoint (`ISchedulingData`, no query-handler class), same reasoning as
  `SectionsEndpoints.GetActiveSectionsAsync`.
- **No cross-module wiring to Reservations yet.** BR-01/overlap validation against service hours
  is Phase 6's concern (Reservations proper) — Scheduling ships standalone, proven only at the
  Architecture/Unit/Integration tiers.
- **No Blazor UI for Scheduling yet** — same deferral Phase 4 made for Tables/Sections
  ("no query-handler pattern exists yet ... no Blazor UI ... deferred to Phase 10").

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

## Error handling

Logging an exception and a user actually seeing something other than a crash or a raw stack
trace are two different guarantees — this project had the first (the `Error` level row above)
without the second for a while, which is exactly the gap to not reintroduce. Every composition
root has its own version of the same two-part guarantee: **log it (once, at the boundary, with
the exception overload) and show the caller something short of the truth.** Never both silent.

- **`Mise.ApiService`**: `GlobalExceptionHandler` (`IExceptionHandler`) is the catch-all every
  other handler falls through to — registered *after* `ValidationExceptionHandler` (handlers run
  in registration order, first-to-return-true wins), it logs the full exception at `Error` and
  returns a generic RFC 7807 `ProblemDetails` 500 carrying only a `traceId`, never the exception's
  type or message. A validation failure (an *expected* outcome) still gets `ValidationException`'s
  own field-scoped 400 — the catch-all is for everything that isn't that: a bug, a DB outage, an
  invariant violation. Adding a new expected-outcome-as-exception type gets its own
  `IExceptionHandler`, registered before the catch-all, the same way `ValidationExceptionHandler`
  already is — don't grow `GlobalExceptionHandler` a `switch` over exception types.
- **`Mise.MigrationService`**: the one-shot startup logic is wrapped in try/catch that logs
  `Critical` with the exception before rethrowing — without it, a startup failure is only ever a
  raw, unstructured stderr dump from the runtime's default unhandled-exception handling, invisible
  to whatever's watching the structured log stream. It still rethrows afterward so the process
  still exits non-zero (`Mise.AppHost`'s `WaitForCompletion(migrations)` must keep seeing this as
  a failure).
- **`Mise.Web` (Blazor Server)**: every call from a component into a typed HTTP client
  (`HttpReservationsClient`, `HttpStaffAuthClient`) is wrapped where it's called
  (`ReservationForm.razor.cs`, `Login.razor`), not left to propagate — an unhandled exception in
  an interactive component is Blazor Server's cue to tear down the whole circuit
  (`MainLayout.razor`'s `#blazor-error-ui` banner is the generic fallback for anything that still
  gets that far), and `Login.razor` is static SSR, where an unhandled exception hits ASP.NET
  Core's generic `/Error` page instead of staying on the form. Each catch logs the exception and
  sets a **generic, user-facing message distinct from an expected failure's message** (e.g.
  `Login.razor`'s `"Something went wrong signing in."` vs. `"Incorrect username or password."` —
  conflating the two sends someone chasing a password reset for what was actually an outage).
  `AddInteractiveServerComponents(options => options.DetailedErrors = ...)` is set explicitly
  (Development-only) so a circuit-level crash never leaks a stack trace either, belt-and-suspenders
  with the per-call try/catch.
- **A known, real friction point when logging from Razor components**: `[LoggerMessage]`'s
  source generator requires a *field* of type `ILogger`; Blazor's `[Inject]` only ever populates
  a *property* (its component-property-injection reflects over `PropertyInfo`, not fields). The
  two are incompatible on the same member — `ReservationForm.razor.cs` and `Login.razor` use a
  plain `Logger.LogError(ex, "…")` instance call instead, which is fine here: neither is a hot
  path (each only runs when a user submits and the call fails), matching the same exception
  Logging's own bullet above already carves out for a composition root's `Program.cs`.
- **Never let a generic exception handler leak what the specific one already knows not to**: the
  same PII/secrets rules from "Logging" above apply to what gets logged here, and doubly to what
  gets returned to the caller — a generic 500 body has no business containing a customer name, a
  connection string, or a stack trace under any circumstances, dev environments included (that's
  what the *server-side* log line is for).

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
  that needs the whole path, not just the UI — see `MiseE2EFixture.cs`. Point the
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
  (which is how `MiseApiFixture` layers a real Testcontainers connection string over
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
