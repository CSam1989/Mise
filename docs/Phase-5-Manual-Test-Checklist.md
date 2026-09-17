# Phase 5 Manual Test Checklist — Scheduling Module (Service Periods & Closed Days)

Companion to the automated suites, not a replacement for them. Everything below is **already
proven automatically** (217 tests: 18 architecture, 124 unit, 4 bUnit, 67 integration, 4 E2E — all
green, plus the tiered coverage gate at 99.7% on `*.Domain`/`*.Application`, `Mise.Modules.Scheduling.Domain`
and `Mise.Modules.Scheduling.Application` both at 100%, and the format check). This checklist is
for *you* to look at the actual code and exercise the actual API before trusting that.

**Scope note, read first:** FR-08 ("Managers can define service periods/hours ... and mark days
closed") got far less charter detail than US-04 did for Tables/Sections — no acceptance criteria,
just one data-model entity (`ServicePeriod`). This phase had to resolve that gap itself (documented
in **CLAUDE.md's new "Scheduling" section** — read that first, it's the map for everything below),
the same way Phase 4 had to resolve two charter gaps of its own (corrections #12/#13). Like Phase 4,
this ships the full Domain/Application/Infrastructure/API surface, proven at the
Architecture/Unit/Integration tiers, but **no Blazor UI** (Phase 10 deferral) and **no cross-module
wiring to Reservations** (that's Phase 6 — BR-01/overlap against service hours). Section 3 below is
an API walkthrough, not a browser one.

Check a box, or write a one-line note next to it if something looks off. Anything you flag, bring
back to the next session with the file/line and what you expected instead.

---

## 0. One-time setup

Unchanged from Phase 3/4 — no new secrets or Aspire parameters this phase.

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

- [ ] All five suites report green (217 tests total: 18 + 124 + 4 + 67 + 4).
- [ ] `dotnet format Mise.slnx --verify-no-changes` reports no changes.
- [ ] Coverage gate reports **pass** at ≥90% on `*.Domain`/`*.Application`. `Mise.Modules.Scheduling.Domain`
      and `Mise.Modules.Scheduling.Application` should both show 100%.

If any of these are *not* green on your machine but I reported them green above, that is the
single most important discrepancy to flag.

---

## 2. Code review — walking the layers

Not exhaustive line-by-line — a guided tour hitting the decisions most worth a second opinion.
File paths are relative to the repo root.

### The gap this phase had to close in the charter itself

- [ ] Read **CLAUDE.md's new "Scheduling" section** first.
- [ ] [`docs/Restaurant-Reservations-Project-Charter.md`](../docs/Restaurant-Reservations-Project-Charter.md)'s
      §10 "Entity: ServicePeriod" — one entity, `Date`/`Label`/`StartTime`/`EndTime`/`IsClosed`, no
      acceptance criteria for FR-08 anywhere else in the document. Do you agree with folding "define
      hours" and "mark days closed" into the same `IsClosed` flag on the same entity, or would you
      rather see a genuinely separate "closed day" concept even though the charter never asked for one?
- [ ] [`docs/plan.md`](../docs/plan.md)'s corrections table, item **#9** — `EndsNextDay` lands in
      `ServicePeriod`'s very first commit rather than being retrofitted later. Confirm the domain
      rule reads correctly: a same-day window needs `EndTime` strictly after `StartTime`; a
      next-day window (`EndsNextDay: true`) allows `EndTime == StartTime`, which represents exactly
      24 hours (the "closed all day" case) rather than a zero-length window.

### Scheduling — Domain

- [ ] [`ServicePeriod.cs`](../src/Modules/Mise.Modules.Scheduling.Domain/ServicePeriod.cs) —
      `Create`/`UpdateDetails` share one `ValidateFields` private method. Confirm the same-day vs.
      next-day branching in that method matches your mental model of "Lunch 12:00–14:30" vs.
      "Dinner 18:00–01:00" vs. "Closed all day (00:00–00:00, EndsNextDay: true)".
- [ ] Compare this against [`Section.cs`](../src/Modules/Mise.Modules.Tables.Domain/Section.cs) —
      both throw plain `ArgumentException`/`ArgumentOutOfRangeException` from `Guard.Against.*`
      rather than `Mise.SharedKernel.DomainRuleViolationException`. Confirm you agree this is a
      *structural field-shape* invariant (no state, no cross-aggregate query), the same category as
      `Section`'s name/`DisplayOrder` checks — not `Table.Deactivate()`'s state-dependent guard,
      which does throw `DomainRuleViolationException`.

### Scheduling — Application

- [ ] [`Ports/ISchedulingData.cs`](../src/Modules/Mise.Modules.Scheduling.Application/Ports/ISchedulingData.cs) —
      read the doc comment on `DeleteServicePeriodAsync` closely. This is the one genuinely new
      problem this phase ran into that Sections/Tables never had to solve: **a hard delete removes
      the idempotency check's only "is this a replay" signal (the row itself) after the first
      successful call.** The fix — check `shared.processed_operation` by `OperationId` *before* the
      row lookup, not after — was found by a failing test during development
      (`DeleteServicePeriod_SameOperationIdTwice_ReturnsNoContentBothTimesAndDeletesOnlyOnce`
      returned 404 on the second call with the naive ordering), not by inspection. Worth confirming
      you'd have caught this reading the code cold, or whether it's subtle enough to warrant a
      comment at the call site too (there already is one — judge whether it's clear enough).
  - [ ] [`DeleteServicePeriod/DeleteServicePeriodCommandHandler.cs`](../src/Modules/Mise.Modules.Scheduling.Application/DeleteServicePeriod/DeleteServicePeriodCommandHandler.cs) —
        notice this handler never loads a `ServicePeriod` at all (no `GetServicePeriodByIdAsync`
        call, unlike Update) — it delegates the whole existence-and-idempotency decision to the
        gateway in one round trip. Agree this is simpler than Update's "load, mutate via a Domain
        method, save" shape, given delete has no Domain-level invariant to enforce? Or would you
        prefer every handler follow the same load-then-act shape for consistency even where the
        load buys nothing?
  - [ ] [`CreateServicePeriod/CreateServicePeriodCommandValidator.cs`](../src/Modules/Mise.Modules.Scheduling.Application/CreateServicePeriod/CreateServicePeriodCommandValidator.cs) —
        the cross-field `RuleFor(c => c.EndTime).Must((c, endTime) => c.EndsNextDay || endTime >
        c.StartTime)` attaches its error to the `EndTime` field specifically. Confirm the exact
        message (*"EndTime must be after StartTime unless the period ends the next day."*) reads
        clearly to a Manager who'd see it in a future UI.

### Scheduling — Infrastructure

- [ ] [`Persistence/Configurations/ServicePeriodConfiguration.cs`](../src/Modules/Mise.Modules.Scheduling.Infrastructure/Persistence/Configurations/ServicePeriodConfiguration.cs) —
      no `xmin`/`IsRowVersion()` concurrency token, same scope decision as `Section` (not `Table`).
      `Date`/`StartTime`/`EndTime` map to plain `DateOnly`/`TimeOnly` — Postgres `date`/`time
      without time zone` columns, confirmed by the generated migration below.
- [ ] [`Persistence/SchedulingData.cs`](../src/Modules/Mise.Modules.Scheduling.Infrastructure/Persistence/SchedulingData.cs) —
      `DeleteServicePeriodAsync`'s ordering (idempotency check, *then* the row lookup) is the
      concrete fix for the bug described above. Confirm `CreateServicePeriodAsync`/
      `UpdateServicePeriodAsync` still check idempotency first too, for consistency, even though
      those two don't have the same "row disappears" problem.
- [ ] Confirm the generated migration
      ([`Persistence/Migrations/*_InitialCreate.cs`](../src/Modules/Mise.Modules.Scheduling.Infrastructure/Persistence/Migrations))
      creates **only** `scheduling.service_period` — not `shared.processed_operation`/
      `shared.audit_log_entry` (Scheduling is a non-owner, same as StaffIdentity/Tables;
      Reservations still owns that DDL).

### API

- [ ] [`Scheduling/ServicePeriodsEndpoints.cs`](../src/Mise.ApiService/Scheduling/ServicePeriodsEndpoints.cs) —
      read the doc comment on why `DELETE /api/service-periods/{id}` takes its `OperationId` as a
      **query parameter**, not a JSON body — no precedent anywhere else in this codebase for a
      DELETE with a body, and minimal APIs bind query parameters more naturally. Acceptable
      deviation from POST/PATCH's body-based `OperationId`, or would you rather see a body forced
      here for consistency even though it's non-standard REST?
  - [ ] `GetServicePeriodsForDateAsync` injects `ISchedulingData` directly into the endpoint, same
        "no query-handler class for a plain projection" pattern as `SectionsEndpoints.GetActiveSectionsAsync`.
- [ ] [`Program.cs`](../src/Mise.ApiService/Program.cs) — `AddSchedulingPersistence` registered
      after `AddTablesPersistence`; confirm `MapServicePeriodsEndpoints()` is called alongside the
      other module `Map*Endpoints()` calls.
- [ ] [`Mise.MigrationService/Program.cs`](../src/Mise.MigrationService/Program.cs) — `SchedulingDbContext`
      migrates after `ReservationsDbContext` (schema-ownership order), position relative to
      StaffIdentity/Tables is arbitrary — same reasoning already documented for Tables in Phase 4.

### Architecture tests

- [ ] [`tests/Mise.ArchitectureTests/ModuleRegistry.cs`](../tests/Mise.ArchitectureTests/ModuleRegistry.cs) —
      `"Scheduling"` is now listed alongside the other three modules.
- [ ] Rerun `dotnet test tests/Mise.ArchitectureTests` yourself if you want extra confidence — all
      18 rules now check four real modules.

### New test files, if you want to read the tests themselves

Unit: `tests/Mise.UnitTests/Scheduling/*.cs` (6 files — 1 domain, 2 validator, 3 handler).
Integration: `tests/Mise.IntegrationTests/ServicePeriodsEndpointTests.cs` — worth a closer read for
the `EndsNextDay` happy-path test and the two delete-replay tests (idempotency and the
FloorStaff-forbidden case).

---

## 3. Manual walkthrough — the API directly

With `aspire run` going, find `apiservice`'s URL from the Aspire dashboard. All bodies below use
PowerShell's `curl.exe` the same way Phase 2/3/4's checklists did.

- [ ] **Log in as the seeded Manager** (same as Phase 3/4):
  ```powershell
  $loginJson = curl.exe -s -X POST https://<apiservice-host>/api/auth/login `
    -H "Content-Type: application/json" `
    -d '{"username":"manager","password":"<your seed-manager-password>"}'
  $login = $loginJson | ConvertFrom-Json
  ```

- [ ] **Create a Lunch period:**
  ```powershell
  $lunch = curl.exe -s -X POST https://<apiservice-host>/api/service-periods `
    -H "Content-Type: application/json" -H "Authorization: Bearer $($login.token)" `
    -d "{`"operationId`":`"$([Guid]::NewGuid())`",`"date`":`"2026-10-01`",`"label`":`"Lunch`",`"startTime`":`"12:00:00`",`"endTime`":`"14:30:00`",`"endsNextDay`":false,`"isClosed`":false}" | ConvertFrom-Json
  ```
  Expect `201 Created`.

- [ ] **Create a Dinner period that crosses midnight:**
  ```powershell
  curl.exe -i -X POST https://<apiservice-host>/api/service-periods `
    -H "Content-Type: application/json" -H "Authorization: Bearer $($login.token)" `
    -d "{`"operationId`":`"$([Guid]::NewGuid())`",`"date`":`"2026-10-01`",`"label`":`"Dinner`",`"startTime`":`"18:00:00`",`"endTime`":`"01:00:00`",`"endsNextDay`":true,`"isClosed`":false}"
  ```
  Expect `201 Created` — confirms docs/plan.md correction #9 actually works end to end.

- [ ] **Mark a whole day closed:**
  ```powershell
  curl.exe -i -X POST https://<apiservice-host>/api/service-periods `
    -H "Content-Type: application/json" -H "Authorization: Bearer $($login.token)" `
    -d "{`"operationId`":`"$([Guid]::NewGuid())`",`"date`":`"2026-12-25`",`"label`":`"Closed`",`"startTime`":`"00:00:00`",`"endTime`":`"00:00:00`",`"endsNextDay`":true,`"isClosed`":true}"
  ```
  Expect `201 Created`.

- [ ] **List periods for the day:** `GET /api/service-periods?date=2026-10-01` — both Lunch and
      Dinner appear, **Lunch first** (ordered by `startTime`).

- [ ] **Try an invalid same-day period** (`startTime` after `endTime`, `endsNextDay: false`) —
      expect `400 Bad Request` with a field error on `endTime`.

- [ ] **Update the Lunch period** via `PATCH /api/service-periods/{id}` with a new `label` —
      expect `200 OK` reflecting the change.

- [ ] **Delete the Lunch period:** `DELETE /api/service-periods/{id}?operationId=<new guid>` —
      expect `204 No Content`.

- [ ] **Replay the exact same delete call** (same id, same `operationId`) — expect **`204 No
      Content` again**, not `404`. This is the idempotency fix described in Section 2 above —
      the single most important thing to verify by hand, since it's the one behavior that would
      have silently regressed without the fix.

- [ ] **Delete a nonexistent id** with a fresh `operationId` — expect `404 Not Found`.

- [ ] **As a FloorStaff caller**, repeat the create or delete call — expect `403 Forbidden` (mint
      one with `./scripts/mint-dev-token.ps1 -Role FloorStaff`). Confirm the **GET** still succeeds
      for a FloorStaff token.

- [ ] **Unauthenticated**, repeat any mutating call with no `Authorization` header — expect
      `401 Unauthorized`.

## 4. Manual walkthrough — the database

```sql
select id, date, label, start_time, end_time, ends_next_day, is_closed from scheduling.service_period;
select entity_type, action, performed_by_staff_id from shared.audit_log_entry
  where entity_type = 'ServicePeriod' order by occurred_at_utc;
select * from scheduling."__ef_migrations_history";
```

- [ ] `scheduling."__ef_migrations_history"` is a **distinct table** from the other three modules'
      history tables — recording its own one migration.
- [ ] `shared.audit_log_entry` shows a `"Created"` row for every period you created above, an
      `"Updated"` row for the PATCH, and a `"Deleted"` row for the delete — each with
      `performed_by_staff_id` populated.
- [ ] `shared.processed_operation` has one row per `operationId` you sent, `resource_type
      'ServicePeriod'` — confirm the replayed delete's `operationId` did **not** produce a second
      row here, and confirm `scheduling.service_period` has exactly the rows you expect (the
      deleted Lunch period is actually gone, not just flagged).

## 5. Known, deliberate gaps — not bugs

Don't file these — they're documented scope boundaries for Phase 5, not oversights:

- **No Blazor UI for Scheduling** — API-only this phase, same deferral Phase 4 made for
  Tables/Sections. The RCL screens land in Phase 10.
- **No cross-module check against Reservations** — a reservation can currently be created outside
  any defined service period, or on a day marked closed, with no rejection. That's BR-01/FR-08's
  enforcement point, and it's explicitly Phase 6 scope (Reservations doesn't reference Scheduling's
  `Contracts` yet).
- **No overlap validation between periods on the same date** — nothing stops a Manager from
  creating two overlapping "Lunch" periods on the same day. FR-08 never asked for this, and adding
  it now would be scope creep beyond what the charter specifies; revisit if it turns out to matter
  in practice.
- **`ServicePeriod` has no optimistic-concurrency token** — same scope decision as `Section`, not
  an oversight. Revisit if two Managers editing the schedule concurrently turns out to be a real
  conflict source.
- **No `GET /api/service-periods/{id}` single-resource endpoint** — the date-filtered list covers
  the only read need so far; add one if a future consumer needs it.
- **No rate-limiting, no localization content for any new strings** — same standing gaps Phase 3/4's
  checklists already named; nothing new here changes that picture.

## 6. Sign-off

- [ ] I reviewed the code sections above and have no unresolved concerns, **or** I've listed
      concerns below with file/line references.
- [ ] I ran the manual API + database walkthroughs and everything matched what's described above,
      **or** I've listed discrepancies below.

**Notes / concerns:**

```
(space for you to fill in)
```
