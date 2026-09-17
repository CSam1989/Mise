# Phase 4 Manual Test Checklist — Tables & Sections Module

Companion to the automated suites, not a replacement for them. Everything below is **already
proven automatically** (177 tests: 18 architecture, 99 unit, 4 bUnit, 52 integration, 4 E2E — all
green, plus the tiered coverage gate at 99.6% on `*.Domain`/`*.Application` and the format check).
This checklist is for *you* to look at the actual code and exercise the actual API before
trusting that.

**Scope note, read first:** FR-07/US-04 ask for Manager CRUD on tables and sections, plus a live
floor plan. This phase ships the full Domain/Application/Infrastructure/API surface for that,
proven at the Architecture/Unit/Integration tiers — but **no Blazor UI**. That mirrors Phase 3's
own precedent exactly ("registering staff is API-only ... a Manager-facing screen is deferred to
Phase 10"): Tables/Sections is the same shape (Manager-only config CRUD), so the same deferral
applies, and the RCL screens land with Phase 10's shared-component build-out. Section 3 below is
therefore an API walkthrough, not a browser one.

Check a box, or write a one-line note next to it if something looks off. Anything you flag, bring
back to the next session with the file/line and what you expected instead.

---

## 0. One-time setup

Unchanged from Phase 3 — no new secrets or Aspire parameters this phase.

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

- [ ] All five suites report green (177 tests total: 18 + 99 + 4 + 52 + 4).
- [ ] `dotnet format Mise.slnx --verify-no-changes` reports no changes.
- [ ] Coverage gate reports **pass** at ≥90% on `*.Domain`/`*.Application`. `Mise.Modules.Tables.Domain`
      should show 100%; `Mise.Modules.Tables.Application` should show ~99–100%.

If any of these are *not* green on your machine but I reported them green above, that is the
single most important discrepancy to flag.

---

## 2. Code review — walking the layers

Not exhaustive line-by-line — a guided tour hitting the decisions most worth a second opinion.
File paths are relative to the repo root.

### Two real gaps this phase had to close in the charter itself

- [ ] Read **CLAUDE.md's new "Tables & Sections" section** first — it's the map for everything
      below.
- [ ] [`docs/plan.md`](../docs/plan.md)'s corrections table, items **#12** and **#13** — FR-07
      requires deactivating a *section*, but the original data model gave `Section` no `IsActive`
      column at all; and US-04's "blocked if the table has an active reservation" edge case has
      no cross-module link to check yet (Reservations doesn't reference `Table` until Phase 6/7).
      Do the two fixes (adding `Section.IsActive`; proxying the reservation-check via `Table`'s
      own `Status` field) read as reasonable stand-ins, or would you rather see the edge case
      explicitly deferred as a "not implemented" gap instead of approximated?
- [ ] [`docs/Restaurant-Reservations-Project-Charter.md`](../docs/Restaurant-Reservations-Project-Charter.md)'s
      §10 "Entity: Section" — confirm the `IsActive` row and its note match what's actually built.

### Tables — Domain

- [ ] [`Section.cs`](../src/Modules/Mise.Modules.Tables.Domain/Section.cs) — `Deactivate()` is
      **unconditional** at the Domain level (no active-tables guard here). Confirm you agree that
      guard belongs in the Application handler instead (see below), not here.
- [ ] [`Table.cs`](../src/Modules/Mise.Modules.Tables.Domain/Table.cs) — `Deactivate()` **does**
      throw (`DomainRuleViolationException`) when `Status` is `Reserved`/`Occupied`. Compare this
      against `Section.Deactivate()`'s unconditional version right above it — same shape of
      problem ("don't deactivate something in use"), two different answers, because one check
      needs only the aggregate's own field and the other needs a database query. Does that
      distinction hold up, or does it feel like an inconsistency?
- [ ] [`TableStatus.cs`](../src/Modules/Mise.Modules.Tables.Domain/TableStatus.cs) — the five
      charter values; nothing sets a table's status away from `Available` until Phase 7.

### Tables — Application

- [ ] [`Ports/ISectionsData.cs`](../src/Modules/Mise.Modules.Tables.Application/Ports/ISectionsData.cs) /
      [`Ports/ITablesData.cs`](../src/Modules/Mise.Modules.Tables.Application/Ports/ITablesData.cs) —
      two gateways, not one, even though they're one module — `IStaffIdentityData` in Phase 3
      combined two things because they had to change together; `Section` and `Table` don't, so
      they stay separate. `TableWithVersion`/`TableSaveResult` are how the xmin version round-trips
      without Application ever touching EF Core.
  - [ ] [`DeactivateSection/DeactivateSectionCommandHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/DeactivateSection/DeactivateSectionCommandHandler.cs) —
        the "still has active tables" guard, routed through the same field-scoped
        `ValidationException` shape as Phase 3's taken-username check. No FluentValidation
        validator registered for this command at all (read the doc comment for why) — agree or
        would you want an empty validator anyway for consistency?
  - [ ] [`CreateTable/CreateTableCommandHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/CreateTable/CreateTableCommandHandler.cs) /
        [`UpdateTable/UpdateTableCommandHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/UpdateTable/UpdateTableCommandHandler.cs) —
        both check `SectionId` exists via `ISectionsData` **before** ever touching `ITablesData`,
        specifically so a bad `SectionId` is a clean 400 instead of a raw FK-violation 500. Was
        this worth catching, or is a 500 on a malformed request acceptable in your view?
  - [ ] [`UpdateTable/UpdateTableCommandHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/UpdateTable/UpdateTableCommandHandler.cs) /
        [`DeactivateTable/DeactivateTableCommandHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/DeactivateTable/DeactivateTableCommandHandler.cs) —
        both throw `Mise.SharedKernel.ConcurrencyConflictException` on a stale version, carrying a
        `Dictionary<string, object?>` of the table's current fields (`CurrentStateOf`) rather than
        a typed DTO — Application can't build a Contracts DTO without adding a dependency on its
        own `.Contracts` project it doesn't otherwise need; is a loose dictionary an acceptable
        trade here?

### Tables — Infrastructure

- [ ] [`Persistence/Configurations/TableConfiguration.cs`](../src/Modules/Mise.Modules.Tables.Infrastructure/Persistence/Configurations/TableConfiguration.cs) —
      read the doc comment on `builder.Property<uint>("Version").IsRowVersion()`. The charter's
      own ADR-001 Amendment 1 assumed a `UseXminAsConcurrencyToken()` helper method that **does
      not exist** in the actually-installed `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 — this
      is the standard, provider-portable EF Core API instead, and Npgsql's own convention silently
      remaps it to the physical `xmin` system column. Worth flagging: this is exactly the kind of
      thing that only surfaces once you actually try to build against a real package version
      rather than trusting a design doc written earlier against an assumed API surface.
  - [ ] Also on `TableConfiguration.cs`: the `HasOne<Section>().WithMany().HasForeignKey(...)`
        relationship with no CLR navigation property on either side — a real gap caught while
        writing this checklist's own integration tests (a `Table` with a nonexistent `SectionId`
        was silently accepted at the Domain/Application level with no FK to catch it before this
        was added). Confirm the generated migration
        ([`Persistence/Migrations/*_InitialCreate.cs`](../src/Modules/Mise.Modules.Tables.Infrastructure/Persistence/Migrations))
        actually creates `FK_table_section_section_id` with `ON DELETE RESTRICT`.
  - [ ] [`Persistence/TablesData.cs`](../src/Modules/Mise.Modules.Tables.Infrastructure/Persistence/TablesData.cs) —
        `SaveWithConcurrencyCheckAsync`'s `Property<uint>("Version").OriginalValue = expectedVersion`
        trick, and the `DbUpdateConcurrencyException` catch that reloads current state via a
        second, no-tracking query. Does the reasoning read clearly from the comments alone?
  - [ ] Confirm the migration creates **only** `tables.section` and `tables.table` — not
        `shared.processed_operation`/`shared.audit_log_entry` (Tables is a non-owner, same as
        StaffIdentity; Reservations still owns that DDL).

### The ETag/optimistic-concurrency pattern in Mise.ApiService — new this phase

- [ ] [`ETag.cs`](../src/Mise.ApiService/ETag.cs) — the only place the quoted-decimal-string wire
      format is formatted/parsed.
- [ ] [`PreconditionRequiredException.cs`](../src/Mise.ApiService/PreconditionRequiredException.cs) /
      [`PreconditionRequiredExceptionHandler.cs`](../src/Mise.ApiService/PreconditionRequiredExceptionHandler.cs) —
      thrown directly by the endpoint (not a handler) for a missing/malformed `If-Match` → 428.
- [ ] [`ConcurrencyConflictExceptionHandler.cs`](../src/Mise.ApiService/ConcurrencyConflictExceptionHandler.cs) —
      read the doc comment on why the current version travels in the JSON body's `currentVersion`
      field and **not** a response `ETag` header. This was a genuine surprise found while writing
      this phase's own integration tests: ASP.NET Core's `ExceptionHandlerMiddleware` registers an
      `OnStarting` callback that unconditionally strips `ETag` from *any* response it processes,
      specifically to stop an error page from being treated as a cacheable representation of the
      resource. Confirm you're comfortable with the body-only approach, or would you want a
      documented follow-up to find a way to preserve the header too?
- [ ] [`DomainRuleViolationExceptionHandler.cs`](../src/Mise.ApiService/DomainRuleViolationExceptionHandler.cs) —
      the third new `IExceptionHandler` this phase adds, alongside `ValidationExceptionHandler` and
      the two above. Confirm the registration order in
      [`Program.cs`](../src/Mise.ApiService/Program.cs) still puts `GlobalExceptionHandler` last.
- [ ] [`Tables/SectionsEndpoints.cs`](../src/Mise.ApiService/Tables/SectionsEndpoints.cs) /
      [`Tables/TablesEndpoints.cs`](../src/Mise.ApiService/Tables/TablesEndpoints.cs) — the two
      `GetActiveSectionsAsync`/`GetFloorPlanAsync` reads inject the gateway interface directly
      rather than going through a command-handler-like "query handler" class (there's no such
      pattern anywhere yet to mirror). Acceptable for a read with no logic beyond a projection, or
      would you rather see the shape established now even though nothing needs it yet?

### Architecture tests

- [ ] [`tests/Mise.ArchitectureTests/ModuleRegistry.cs`](../tests/Mise.ArchitectureTests/ModuleRegistry.cs) —
      `"Tables"` is now listed alongside `"Reservations"` and `"StaffIdentity"`.
- [ ] Rerun `dotnet test tests/Mise.ArchitectureTests` yourself if you want extra confidence — all
      18 rules now check three real modules, several non-vacuously for the first time with three
      modules instead of two (e.g. the module-boundary rules can now catch a module reaching
      *past* a sibling it has no business referencing, not just the one it does).

### New test files, if you want to read the tests themselves

Unit: `tests/Mise.UnitTests/Tables/*.cs` (10 files — 2 domain, 2 validator, 6 handler). Integration:
`tests/Mise.IntegrationTests/{SectionsEndpointTests,TablesEndpointTests}.cs` — the latter is worth
a closer read for the 428/409 flows and the deliberately-manufactured-Occupied-status test
(`PatchTableDeactivate_StatusOccupied_Returns409`, which mirrors Phase 3's
`GlobalExceptionHandlerTests` technique of driving an otherwise-unreachable state directly via SQL
rather than adding test-only production code).

---

## 3. Manual walkthrough — the API directly

With `aspire run` going, find `apiservice`'s URL from the Aspire dashboard. All bodies below use
PowerShell's `curl.exe` the same way Phase 2/3's checklists did.

- [ ] **Log in as the seeded Manager** (same as Phase 3):
  ```powershell
  $loginJson = curl.exe -s -X POST https://<apiservice-host>/api/auth/login `
    -H "Content-Type: application/json" `
    -d '{"username":"manager","password":"<your seed-manager-password>"}'
  $login = $loginJson | ConvertFrom-Json
  ```

- [ ] **Create a section:**
  ```powershell
  $sectionId = [Guid]::NewGuid()
  $section = curl.exe -s -X POST https://<apiservice-host>/api/sections `
    -H "Content-Type: application/json" -H "Authorization: Bearer $($login.token)" `
    -d "{`"operationId`":`"$([Guid]::NewGuid())`",`"name`":`"Patio`",`"displayOrder`":0}" | ConvertFrom-Json
  ```
  Expect `201 Created`, `isActive: true`.

- [ ] **List sections:** `curl.exe -s https://<apiservice-host>/api/sections -H "Authorization: Bearer $($login.token)"` —
      the new section appears.

- [ ] **Create a table in that section:**
  ```powershell
  $create = curl.exe -i -X POST https://<apiservice-host>/api/tables `
    -H "Content-Type: application/json" -H "Authorization: Bearer $($login.token)" `
    -d "{`"operationId`":`"$([Guid]::NewGuid())`",`"sectionId`":`"$($section.id)`",`"name`":`"T1`",`"minCapacity`":2,`"maxCapacity`":4,`"isCombinable`":false,`"positionX`":null,`"positionY`":null}"
  ```
  Expect `201 Created` **and an `ETag` response header** — copy its value for the next step.

- [ ] **View the floor plan:** `GET /api/tables/floor-plan?sectionId=$($section.id)` — the table
      appears with a `version` field.

- [ ] **Update the table without an `If-Match` header** — expect **`428 Precondition Required`**.

- [ ] **Update the table with the `ETag` you copied as `If-Match`** — expect `200 OK`, a **new**
      `ETag` in the response, and the fields you sent reflected back.

- [ ] **Retry the same update using the now-stale, original `ETag`** — expect **`409 Conflict`**
      with a JSON body containing `currentVersion` and `currentState` (the row's actual current
      fields), and confirm the response has **no `ETag` header** (see the CLAUDE.md note on why).

- [ ] **Deactivate the table** using its current `ETag` via
      `PATCH /api/tables/{id}/deactivate` with body `{"operationId": "<new guid>"}` — expect
      `200 OK`, `isActive: false`.

- [ ] **As a FloorStaff caller**, repeat the create-section or create-table call — expect
      `403 Forbidden` (mint one with `./scripts/mint-dev-token.ps1 -Role FloorStaff`).

- [ ] **Unauthenticated**, repeat any mutating call with no `Authorization` header — expect
      `401 Unauthorized`.

- [ ] **Create a second table in a fresh section, then try to deactivate that section** — expect
      `400 Bad Request` with a field error on `sectionId`: *"Cannot deactivate a section that
      still has active tables."* Deactivate the table first, then retry the section deactivation —
      expect `200 OK`.

## 4. Manual walkthrough — the database

```sql
select id, name, section_id, status, is_active from tables."table";
select id, name, display_order, is_active from tables.section;
select entity_type, action, performed_by_staff_id from shared.audit_log_entry
  where entity_type in ('Table', 'Section') order by occurred_at_utc;
select * from tables."__ef_migrations_history";
```

- [ ] `tables."__ef_migrations_history"` is a **distinct table** from `reservations."__ef_migrations_history"`
      and `staff_identity."__ef_migrations_history"` — recording its own one migration.
- [ ] `shared.audit_log_entry` shows a `"Created"` row for every section/table you created above,
      an `"Updated"` row for the successful PATCH, and a `"Deactivated"` row for the deactivation —
      each with `performed_by_staff_id` populated.
- [ ] `shared.processed_operation` has one row per `operationId` you sent, with `resource_type`
      `'Table'` or `'Section'` — confirm a replayed `operationId` (send the same create request
      twice) does **not** produce a second row here or a second row in `tables."table"`.

## 5. Known, deliberate gaps — not bugs

Don't file these — they're documented scope boundaries for Phase 4, not oversights:

- **No Blazor UI for Tables/Sections** — API-only this phase, same deferral Phase 3 made for
  staff management. The RCL screens (including the actual floor-plan board) land in Phase 10.
- **`Table.Deactivate()`'s "blocked if in use" guard is a self-contained proxy**, not a real
  cross-module reservation check — Reservations doesn't reference `Table` until Phase 6/7, so
  nothing production-real sets `Status` away from `Available` yet. The guard itself is genuine,
  tested logic; it's just inert in production until Phase 7 wires up real status transitions.
- **`Section` has no optimistic-concurrency token** — a scope decision (the charter only names
  `Table`/`Reservation` as concurrency-tracked), not an oversight. Revisit if two Managers editing
  sections concurrently turns out to be a real conflict source in practice.
- **No `GET /api/tables/{id}` single-resource endpoint** — the floor-plan list (with per-row
  `version`) covers the "get current version before a PATCH" need; a dedicated single-resource GET
  wasn't added to keep the endpoint surface minimal. Add one if a future consumer needs it.
- **No table status-change endpoint** (`PATCH /api/tables/{id}/status` from the charter's own
  sample contract) — that's FR-05/FR-06/Phase 7 scope (seating a reservation, marking a table
  occupied), not table *configuration*, which is all Phase 4 covers.
- **`IsCombinable` stays a plain boolean** — modeling *which* tables combine with which is an open
  question the plan explicitly defers to Phase 6 (BR-07), not something this phase should
  pre-decide.
- **No rate-limiting, no localization content for any new strings** — same standing gaps Phase 3's
  checklist already named; nothing new here changes that picture.

## 6. Sign-off

- [ ] I reviewed the code sections above and have no unresolved concerns, **or** I've listed
      concerns below with file/line references.
- [ ] I ran the manual API + database walkthroughs and everything matched what's described above,
      **or** I've listed discrepancies below.

**Notes / concerns:**

```
(space for you to fill in)
```
