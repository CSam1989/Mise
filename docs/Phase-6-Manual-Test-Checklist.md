# Phase 6 Manual Test Checklist — Reservations Proper (BR-01 Overlap, BR-07 Capacity/Combinable, Search)

Companion to the automated suites, not a replacement for them. Everything below is **already
proven automatically** by the suite counts in [Section 1](#1-re-run-the-automated-suites-yourself)
below — all green, plus the tiered coverage gate at ≥90% on `*.Domain`/`*.Application` and the
format check. This checklist is for *you* to look at the actual code and exercise the actual API
before trusting that.

**Scope note, read first:** this is the biggest phase so far — it grows Reservations past
Phase 2's four-field walking skeleton into the real aggregate (phone/email/notes/table
assignment/timestamps/`xmin`), adds `Update`/`Cancel` (FR-03), adds search (US-02/FR-02), and
resolves a design question docs/plan.md had explicitly left blocking this phase: how "an
explicitly combinable set of tables" (BR-07) gets modeled. That resolution (**ADR-006**, in
**CLAUDE.md's new "Reservations proper (Phase 6)" section** — read that first, it's the map for
everything below) also means this is the **first phase with a real cross-module read**:
`Mise.Modules.Reservations.Application` now depends on `Mise.Modules.Tables.Contracts` at
request time, not just at compile time for a shared DTO. Two things stay explicitly out of
scope, matching how earlier phases deferred their own remainders: no validation against
Scheduling's service periods/closed days (FR-08 isn't in this phase's charter refs), and no
`Seated`/`Completed`/`NoShow` transitions (BR-04/BR-05, Phase 7). Section 3 below is mostly an
API walkthrough — the Blazor UI only gained the `CustomerPhone` field FR-01 requires, not the
full mockup screens.

Check a box, or write a one-line note next to it if something looks off. Anything you flag, bring
back to the next session with the file/line and what you expected instead.

---

## 0. One-time setup

Unchanged from Phase 3/4/5 — no new secrets or Aspire parameters this phase.

- [ ] `dotnet --version` prints a `10.0.x` version (not an `11.x` preview).
- [ ] Docker Desktop (or equivalent) is running.
- [ ] `dotnet tool restore` has been run once in the repo root.
- [ ] `dotnet user-secrets set Parameters:jwt-signing-key <any-random-string> --project src/Mise.AppHost`
- [ ] `dotnet user-secrets set Parameters:seed-manager-password <any-random-string> --project src/Mise.AppHost`
- [ ] Playwright browsers installed: `tests/Mise.E2ETests/bin/Release/net10.0/playwright.ps1
      install --with-deps chromium`.

## 1. Re-run the automated suites yourself

```powershell
./scripts/test.ps1        # build + all five suites + format check
./scripts/coverage.ps1     # tiered coverage gate (90% on *.Domain/*.Application)
```

- [ ] All five suites report green (300 tests: 18 architecture, 172 unit, 5 bUnit,
      101 integration, 4 E2E).
- [ ] `dotnet format Mise.slnx --verify-no-changes` reports no changes.
- [ ] Coverage gate reports **pass** at ≥90% on `*.Domain`/`*.Application`.

If any of these are *not* green on your machine but I reported them green above, that is the
single most important discrepancy to flag.

---

## 2. Code review — walking the layers

Not exhaustive line-by-line — a guided tour hitting the decisions most worth a second opinion.
File paths are relative to the repo root.

### The design question this phase had to resolve

- [ ] Read **CLAUDE.md's new "Reservations proper (Phase 6)" section** first — especially
      **ADR-006**.
- [ ] [`docs/plan.md`](../docs/plan.md)'s Open Questions table — confirm the "combinable set of
      tables" row is now marked resolved, pointing at ADR-006, rather than silently deleted (the
      history of *why* it was blocking is worth keeping).
- [ ] [`docs/Restaurant-Reservations-Project-Charter.md`](../docs/Restaurant-Reservations-Project-Charter.md)'s
      new §10 "Entity: TableGroup" — confirm you agree with the native `uuid[]` column over a
      join table (a restaurant's handful of combinable pairings never needs relational querying
      of its own), and that Create-only (no Update/Deactolete this phase) is an acceptable scope
      cut given no other module needs to change a group's membership yet.

### Reservations — Domain

- [ ] [`Reservation.cs`](../src/Modules/Mise.Modules.Reservations.Domain/Reservation.cs) —
      confirm `UpdateDetails` throws `DomainRuleViolationException` when `Status != Confirmed`
      (a self-contained invariant, same category as `Table.Deactivate()`'s guard) and that
      `Cancel` is an idempotent no-op when already `Cancelled` (same leniency as
      `Table.Deactivate()` re-deactivating an already-inactive table).
- [ ] [`ReservationStatus.cs`](../src/Modules/Mise.Modules.Reservations.Domain/ReservationStatus.cs) —
      confirm `Seated`/`Completed`/`NoShow` are declared but genuinely unreachable via any
      command yet (grep the Application layer for any code setting them — there should be none).

### Tables — the new TableGroup slice

- [ ] [`TableGroup.cs`](../src/Modules/Mise.Modules.Tables.Domain/TableGroup.cs) — the aggregate
      only enforces "≥2 distinct table ids"; every cross-aggregate check (exists, active,
      `IsCombinable`, not already grouped) lives in
      [`CreateTableGroupCommandHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/CreateTableGroup/CreateTableGroupCommandHandler.cs).
      Confirm you agree with that split.
- [ ] [`TableGroupConfiguration.cs`](../src/Modules/Mise.Modules.Tables.Infrastructure/Persistence/Configurations/TableGroupConfiguration.cs) —
      `TableIds` (a read-only `IReadOnlyList<Guid>` property, no public setter) maps to the
      column via `UsePropertyAccessMode(PropertyAccessMode.Field)` reaching the private
      `_tableIds` backing field. Confirm the generated migration
      ([`Persistence/Migrations/*_AddTableGroups.cs`](../src/Modules/Mise.Modules.Tables.Infrastructure/Persistence/Migrations))
      actually produces a `uuid[]` column, not a join table.
- [ ] [`TableAvailabilityLookup.cs`](../src/Modules/Mise.Modules.Tables.Infrastructure/Persistence/TableAvailabilityLookup.cs) —
      the cross-module contract implementation. Confirm the combined-capacity math (sum of every
      group member's Min/MaxCapacity) matches your mental model of BR-07.
- [ ] [`Mise.Modules.Tables.Infrastructure.csproj`](../src/Modules/Mise.Modules.Tables.Infrastructure/Mise.Modules.Tables.Infrastructure.csproj) —
      the new `ProjectReference` to its own `Mise.Modules.Tables.Contracts` project. Confirm this
      reads as "a module implementing its own public contract," not a boundary violation — the
      architecture-test run in Section 1 is the actual proof either way.

### Reservations — Application

- [ ] [`TableAssignmentGuard.cs`](../src/Modules/Mise.Modules.Reservations.Application/TableAssignmentGuard.cs) —
      shared by Create and Update so BR-07 isn't checked two different ways. Confirm the three
      outcomes (table missing → 400, table inactive → 400, capacity mismatch → 400 on
      `PartySize`) match the charter's own sample contract phrasing.
- [ ] [`Ports/IReservationsData.cs`](../src/Modules/Mise.Modules.Reservations.Application/Ports/IReservationsData.cs) —
      `ReservationSaveOutcome.TableOverlap` is BR-01's gateway-level signal. Confirm you're
      comfortable that Create/Update/Cancel all funnel through the same outcome enum rather than
      each having its own bespoke result shape.
- [ ] [`CreateReservation/CreateReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/CreateReservation/CreateReservationCommandHandler.cs) —
      confirm `DurationMinutes` falls back to `IOptions<ReservationDefaultsOptions>.Value.DefaultDurationMinutes`
      (90) only when the caller sends `null`, never overriding an explicit value.
- [ ] [`UpdateReservation/UpdateReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/UpdateReservation/UpdateReservationCommandHandler.cs) —
      confirm the Domain-level `DomainRuleViolationException` (already-cancelled) is checked
      *before* BR-01/BR-07 re-validate — there's nothing to re-validate against a reservation
      that can no longer be edited.
- [ ] [`CancelReservation/CancelReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/CancelReservation/CancelReservationCommandHandler.cs) —
      no FluentValidation validator (same reasoning as `DeactivateTableCommandHandler` — no
      user-typed fields to validate).

### Reservations — Infrastructure (the hard part)

- [ ] [`Persistence/Configurations/ReservationConfiguration.cs`](../src/Modules/Mise.Modules.Reservations.Infrastructure/Persistence/Configurations/ReservationConfiguration.cs) —
      `table_id`/`created_by_staff_id` have **no physical FK** to Tables'/StaffIdentity's schemas
      (each module owns its own schema; existence is checked via `ITableAvailabilityLookup`
      instead). Confirm you agree this is the correct call for a modular monolith rather than an
      oversight.
- [ ] [`Persistence/Migrations/*_AddReservationDetailsAndConcurrency.cs`](../src/Modules/Mise.Modules.Reservations.Infrastructure/Persistence/Migrations) —
      the hand-written raw SQL at the bottom of `Up()`. Three things worth reading closely:
  - [ ] `CREATE EXTENSION IF NOT EXISTS pg_trgm`/`btree_gist` run **inside this migration**, not
        `Mise.MigrationService`'s `Program.cs` (despite docs/plan.md's original text suggesting
        the latter) — because `MiseApiFixture`'s integration tests call
        `ReservationsDbContext.Database.MigrateAsync()` directly, bypassing `MigrationService`
        entirely. Confirm you'd have caught that gap, or whether it's subtle enough to flag for
        next time a "runs before any migration" instruction shows up in the plan again.
  - [ ] `reservations.reservation_end_time(...)` — a hand-written `IMMUTABLE` SQL function
        working around Postgres marking `timestamptz + interval` `STABLE`. Read the comment's
        argument for why declaring it `IMMUTABLE` is actually safe here (a pure minutes-only
        interval never depends on session timezone) rather than a lie that happens to work today.
  - [ ] The exclusion constraint itself — confirm the `WHERE (status NOT IN ('Cancelled',
        'NoShow'))` clause matches docs/plan.md correction #1 verbatim.
- [ ] [`Persistence/ReservationsData.cs`](../src/Modules/Mise.Modules.Reservations.Infrastructure/Persistence/ReservationsData.cs) —
      `HasOverlapAsync` (the friendly pre-check) vs. the `catch (DbUpdateException ex) when
      (IsExclusionViolation(ex))` clause (the authoritative fallback). Confirm you agree the
      pre-check's ±1-day bounding window is a reasonable performance/correctness tradeoff, not a
      correctness gap (the DB constraint is what's actually authoritative regardless).
  - [ ] `SearchReservationsAsync` — `EF.Functions.ILike` with a leading wildcard on
        `CustomerName`/`CustomerPhone`. Confirm you're comfortable that only `CustomerName` has a
        `pg_trgm` index backing it (per charter §10's Key Indexes list) and `CustomerPhone`
        search is currently an unindexed scan.

### API

- [ ] [`Reservations/ReservationsEndpoints.cs`](../src/Mise.ApiService/Reservations/ReservationsEndpoints.cs) —
      every endpoint uses the `"FloorStaff"` policy (both roles), not `"Manager"` — confirm this
      matches FR-01–03 naming no permission boundary between Floor Staff and Manager for
      reservations, unlike Tables/Scheduling's Manager-only mutations.
- [ ] [`ReservationOverlapExceptionHandler.cs`](../src/Mise.ApiService/ReservationOverlapExceptionHandler.cs) —
      registered before `GlobalExceptionHandler`, same pattern as `ConcurrencyConflictExceptionHandler`.
- [ ] [`Tables/TableGroupsEndpoints.cs`](../src/Mise.ApiService/Tables/TableGroupsEndpoints.cs) —
      `Manager`-only, Create-only, no ETag (no concurrency token on `TableGroup` this phase).
- [ ] [`Program.cs`](../src/Mise.ApiService/Program.cs) — `Configure<ReservationDefaultsOptions>`
      bound from the `"Reservations"` configuration section; confirm
      [`appsettings.json`](../src/Mise.ApiService/appsettings.json) documents the default
      explicitly (`DefaultDurationMinutes: 90`) rather than leaving it invisible.

### Architecture tests

- [ ] Rerun `dotnet test tests/Mise.ArchitectureTests` yourself — all 18 rules should stay green
      with the new `Mise.Modules.Reservations.Application → Mise.Modules.Tables.Contracts` and
      `Mise.Modules.Tables.Infrastructure → Mise.Modules.Tables.Contracts` references in place.
      This is the actual proof behind ADR-006's boundary-safety claim, not just the doc comment.

### New test files, if you want to read the tests themselves

Unit: `tests/Mise.UnitTests/Reservations/*.cs` (6 files) and the three new
`tests/Mise.UnitTests/Tables/*TableGroup*.cs` files.
Integration: `tests/Mise.IntegrationTests/ReservationsEndpointTests.cs` (rewritten — worth a
closer read for `CreateReservation_TwoConcurrentOverlappingInserts_ExactlyOneSucceeds`, the
hard-subsystem proof) and `tests/Mise.IntegrationTests/TableGroupsEndpointTests.cs` (new).

---

## 3. Manual walkthrough — the API directly

With `aspire run` going, find `apiservice`'s URL from the Aspire dashboard. All bodies below use
PowerShell's `curl.exe` the same way earlier phases' checklists did.

- [ ] **Log in as the seeded Manager:**
  ```powershell
  $loginJson = curl.exe -s -X POST https://<apiservice-host>/api/auth/login `
    -H "Content-Type: application/json" `
    -d '{"username":"manager","password":"<your seed-manager-password>"}'
  $login = $loginJson | ConvertFrom-Json
  $auth = "Authorization=Bearer $($login.token)"
  ```

- [ ] **Create a Section and two combinable Tables**, then a **TableGroup** combining them:
  ```powershell
  $section = curl.exe -s -X POST https://<apiservice-host>/api/sections -H "Content-Type: application/json" -H $auth `
    -d '{"operationId":"<guid>","name":"Patio","displayOrder":0}' | ConvertFrom-Json
  $tableA = curl.exe -s -X POST https://<apiservice-host>/api/tables -H "Content-Type: application/json" -H $auth `
    -d "{`"operationId`":`"<guid>`",`"sectionId`":`"$($section.id)`",`"name`":`"T1`",`"minCapacity`":2,`"maxCapacity`":4,`"isCombinable`":true}" | ConvertFrom-Json
  $tableB = curl.exe -s -X POST https://<apiservice-host>/api/tables -H "Content-Type: application/json" -H $auth `
    -d "{`"operationId`":`"<guid>`",`"sectionId`":`"$($section.id)`",`"name`":`"T2`",`"minCapacity`":2,`"maxCapacity`":4,`"isCombinable`":true}" | ConvertFrom-Json
  curl.exe -i -X POST https://<apiservice-host>/api/table-groups -H "Content-Type: application/json" -H $auth `
    -d "{`"operationId`":`"<guid>`",`"name`":`"T1+T2`",`"tableIds`":[`"$($tableA.id)`",`"$($tableB.id)`"]}"
  ```
  Expect `201 Created`.

- [ ] **Create a reservation for a party of 8 against T1 alone** — expect `400 Bad Request` on
      `PartySize` (T1's own capacity is 4).

- [ ] **Create the same party-of-8 reservation against T1 again, now that it's grouped with
      T2** — expect `201 Created` (combined capacity 4+4=8 fits — BR-07's combinable-set clause).

- [ ] **Create a second reservation on the same table for an overlapping time** — expect
      `409 Conflict` (BR-01).

- [ ] **Search** `GET /api/reservations/search?query=<partial name or phone>` and
      `GET /api/reservations/search?date=2026-10-01` — confirm both filters work independently.

- [ ] **Update the reservation** via `PATCH /api/reservations/{id}` with the ETag from the create
      response as `If-Match` — expect `200 OK` with a new ETag.

- [ ] **Try the update again with the old ETag** — expect `409 Conflict` with `currentState` in
      the body.

- [ ] **Cancel the reservation:** `PATCH /api/reservations/{id}/cancel` with a fresh `If-Match` —
      expect `200 OK`, `status: "Cancelled"`.

- [ ] **Try to edit the now-cancelled reservation** — expect `409 Conflict` (Domain rule
      violation, not a concurrency conflict).

- [ ] **As a FloorStaff caller**, repeat create/update/cancel/search — expect all to **succeed**
      (mint one with `./scripts/mint-dev-token.ps1 -Role FloorStaff`) — confirming FR-01–03's "no
      permission boundary between roles" for reservations.

- [ ] **Unauthenticated**, repeat any call — expect `401 Unauthorized`.

## 4. Manual walkthrough — the database

```sql
select id, customer_name, customer_phone, party_size, table_id, status, duration_minutes
  from reservations.reservation order by created_at_utc desc limit 10;
select conname from pg_constraint where conname = 'no_overlapping_reservations';
select id, name, table_ids, is_active from tables.table_group;
select entity_type, action, performed_by_staff_id from shared.audit_log_entry
  where entity_type in ('Reservation', 'TableGroup') order by occurred_at_utc desc limit 20;
```

- [ ] `no_overlapping_reservations` exists as a real exclusion constraint (confirms the migration
      actually applied, not just compiled).
- [ ] `tables.table_group.table_ids` is a genuine Postgres array column (`{...}` in `psql`'s
      default output), not a serialized string.
- [ ] `shared.audit_log_entry` shows `"Created"`/`"Updated"`/`"Cancelled"` rows for the
      reservation you exercised above, and a `"Created"` row for the `TableGroup`.
- [ ] `reservations."__ef_migrations_history"` and `tables."__ef_migrations_history"` each show
      one new row for this phase's migration, still distinct from the other two modules'.

## 5. Known, deliberate gaps — not bugs

Don't file these — they're documented scope boundaries for Phase 6, not oversights:

- **No validation against Scheduling's service periods/closed days** — a reservation can still
  be created outside any defined service period, or on a closed day. FR-08 isn't in this phase's
  charter refs; Scheduling's own Phase 5 checklist already named this as later-phase scope.
- **`Seated`/`Completed`/`NoShow` are unreachable** — BR-04/BR-05 and the cross-module
  table-status event are Phase 7's concern.
- **BR-01 doesn't extend across a `TableGroup`'s other members** — booking T1 as part of a
  T1+T2 combo doesn't mark T2 unavailable for an unrelated, independent booking. The charter's
  fixed `Reservation.TableId` schema has no representation for "this reservation occupies a set
  of tables." Revisit if Phase 7 makes it matter.
- **`TableGroup` is Create-only** — no Update/Deactivate endpoint yet, same minimalism Section
  had before Phase 4 forced its hand.
- **No `GET /api/reservations/{id}` single-resource endpoint** — Search and the Create/Update/
  Cancel responses themselves cover the only read need so far.
- **The Blazor UI only gained `CustomerPhone`** — no search/edit/cancel/table-assignment screens.
  Deferred to Phase 10, same as Tables/Scheduling's entire UI.
- **`CustomerPhone` partial search has no index** — only `CustomerName` has the `pg_trgm` GIN
  index; a phone substring search is an unindexed scan at today's data volume.
- **No rate-limiting, no localization content for any new strings** — same standing gaps earlier
  phases' checklists already named.

## 6. Sign-off

- [ ] I reviewed the code sections above and have no unresolved concerns, **or** I've listed
      concerns below with file/line references.
- [ ] I ran the manual API + database walkthroughs and everything matched what's described above,
      **or** I've listed discrepancies below.

**Notes / concerns:**

```
(space for you to fill in)
```
