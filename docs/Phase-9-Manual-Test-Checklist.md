# Phase 9 Manual Test Checklist — Audit Completeness (FR-09, NFR-02, US-05, ADR-009)

Companion to the automated suites, not a replacement for them. Everything below is **already
proven automatically** by the suite counts in [Section 1](#1-re-run-the-automated-suites-yourself)
below — all green, plus the format check. This checklist is for *you* to look at the actual code
and exercise the actual endpoints before trusting that.

**Scope note, read first:** this phase closes the gap every prior phase's audit write left open —
`IAuditWriter.WriteAsync` added an entry and saved it in its **own**, separate `SaveChangesAsync`
call from the mutation it audits, so a crash between the two could silently lose the entry. Read
**CLAUDE.md's new "Audit completeness (Phase 9)" section (ADR-009)** first — it's the map for
everything below: the `IAuditableEntity` marker + `AuditCompletenessInterceptor` design, why 19
handler call sites got reordered to stage the entry *before* the mutating gateway call instead of
after, and — worth reading closely — **two genuine pre-existing bugs this phase's own interceptor
caught while it was being wired up, neither of which was introduced by this phase**: `IAuditWriter`
had silently resolved to the wrong module's `DbContext` for three of the four modules since Phase 3
(an unkeyed DI registration collision, now fixed with keyed services), and
`StaffIdentityData.RegisterStaffAsync`'s `AutoSaveChanges = false` guard had never actually taken
effect since Phase 3 either (a generic-arity mismatch in a type check). Also new: a read side
(`GET /api/reservations/{id}/audit-history`, `GET /api/tables/{id}/audit-history`, US-05 AC #2) and
a PII-redaction regression test (charter correction #4's `AuditLogEntry` half). **No Blazor UI** —
same deferral every phase since 4 has made; the history screen lands in Phase 10.

Check a box, or write a one-line note next to it if something looks off. Anything you flag, bring
back to the next session with the file/line and what you expected instead.

---

## 0. One-time setup

Unchanged from Phase 3–8 — no new secrets or Aspire parameters this phase.

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

- [ ] All five suites report green — **19 architecture, 242 unit, 5 bUnit, 148 integration, 4
      E2E (418 total)**. Unlike some earlier phases' checklists, **all five tiers were personally
      run to green in the session that wrote this phase** — Docker/Testcontainers was reachable
      throughout, including the full integration suite twice (once mid-debugging, once clean at
      the end). Treat your own run as confirmation, not a first attempt at an untested tier.
- [ ] `dotnet format Mise.slnx --verify-no-changes` reports no changes (verified — clean, no
      output).
- [ ] Coverage gate reports **pass** against the 90% floor on `*.Domain`/`*.Application`.
- [ ] Architecture tests report **19** rules green — one more than Phase 8's 18: a new
      `SharedKernelInfrastructure_NeverReferencesEfCore` rule in `LayeringTests.cs` (see
      Section 2) enforces a claim CLAUDE.md already made but nothing previously checked.

**A note on how this phase's debugging actually went**, in case you re-derive the same symptoms
independently: the first full integration run after wiring up the interceptor failed **95 of 148
tests**, all with the same shape — every entity-creating endpoint returned 500. That traced to the
`IAuditWriter` DI-key bug named above, not a flaw in the interceptor's own logic. After fixing
that, a **second**, narrower failure (6 tests, all `StaffEndpointTests`/`AuthEndpointTests`)
traced to the separate `UserOnlyStore` type-check bug, also named above. Both are described in
full in CLAUDE.md's ADR-009 section — worth reading before assuming a red run means *this* phase's
new code is wrong; twice, the real fault was older code the interceptor simply had never been able
to see before.

If any of these are *not* green on your machine, that is the single most important thing to flag.

---

## 2. Code review — walking the layers

Not exhaustive line-by-line — a guided tour hitting the decisions most worth a second opinion.
File paths are relative to the repo root.

### The design questions this phase had to resolve

- [ ] Read **CLAUDE.md's new "Audit completeness (Phase 9)" section** first — especially
      **ADR-009**'s three decisions: the `IAuditableEntity` marker interface (why explicit opt-in
      per aggregate rather than every `AggregateRoot<TId>` automatically), staging the entry
      *before* the gateway call instead of writing it after, and keyed DI per module. Confirm you
      agree the marker-interface approach is what keeps ASP.NET Identity's own internal writes
      (password rehash, security stamps) from ever tripping the interceptor — they touch
      `StaffIdentityUser`, which deliberately does **not** implement the marker.
- [ ] Confirm you agree with the two bug writeups in that section — the DI keying issue and the
      `UserOnlyStore` generic-arity mismatch. Both predate this phase; both were only surfaced
      because the interceptor started checking something nothing checked before.
- [ ] [`AuditCompletenessInterceptor.cs`](../src/Mise.SharedKernel.Persistence/AuditCompletenessInterceptor.cs) —
      confirm the check is keyed off `IAuditableEntity`, not "any tracked change at all," and that
      it throws *before* calling `base.SavingChangesAsync` (i.e. before any SQL is generated).

### `Mise.SharedKernel` / `Mise.SharedKernel.Infrastructure` / `Mise.SharedKernel.Persistence`

- [ ] [`IAuditableEntity.cs`](../src/Mise.SharedKernel/IAuditableEntity.cs) — an empty marker
      interface in the EF-free `Mise.SharedKernel` project (not `.Infrastructure`), so Domain
      types can implement it without crossing the module-boundary rule.
- [ ] [`IAuditWriter.cs`](../src/Mise.SharedKernel.Infrastructure/IAuditWriter.cs) — confirm the
      new `Stage(AuditLogEntry entry)` method is synchronous (tracker-only, no `SaveChangesAsync`)
      and that `WriteAsync` (add + save immediately) is unchanged, still used by
      `LoginCommandHandler` alone.
- [ ] [`AuditWriterKeys.cs`](../src/Mise.SharedKernel.Infrastructure/AuditWriterKeys.cs) — four
      string constants, one per module. Confirm its doc comment's explanation of *why* a plain
      `DbContext` type couldn't be used as the keyed-service key (Application layer may not
      reference its own module's Infrastructure project).
- [ ] [`AuditWriter.cs`](../src/Mise.SharedKernel.Persistence/AuditWriter.cs) — `Stage` is one
      line (`dbContext.Set<AuditLogEntry>().Add(entry)`); `WriteAsync` is untouched from before
      this phase.
- [ ] [`IAuditReader.cs`](../src/Mise.SharedKernel.Infrastructure/IAuditReader.cs) /
      [`AuditReader.cs`](../src/Mise.SharedKernel.Persistence/AuditReader.cs) — the read side,
      registered once (not keyed, not per module) — confirm you agree a read has no atomicity
      requirement tying it to one module's `DbContext`.
- [ ] [`AuditLogEntryConfiguration.cs`](../src/Mise.SharedKernel.Persistence/Configurations/AuditLogEntryConfiguration.cs) —
      the new `HasIndex(a => new { a.EntityType, a.EntityId })` — confirm a matching migration
      exists (see below).

### Call-site reorder — 19 files, one repeated pattern

Every one of these moved its `auditWriter.WriteAsync(...)` call (after the gateway call, gated on
`!result.WasAlreadyProcessed`) to `auditWriter.Stage(...)` (before the gateway call, unconditional).
Rather than reading all 19, spot-check the pattern on a representative few and confirm the rest
follow it (the commit's diff is the authoritative list):

- [ ] [`CreateReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/CreateReservation/CreateReservationCommandHandler.cs) —
      the simplest case: `EntityId` now comes from the locally-created `reservation.Id`, not
      `result.Reservation.Id`.
- [ ] [`DeleteServicePeriodCommandHandler.cs`](../src/Modules/Mise.Modules.Scheduling.Application/DeleteServicePeriod/DeleteServicePeriodCommandHandler.cs) —
      the one Delete case: no domain object to build first, so `Stage` is called unconditionally
      before the *single* gateway call that also determines not-found/replay — read its comment
      on why that's still safe.
- [ ] [`CreateTableGroupCommandHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/CreateTableGroup/CreateTableGroupCommandHandler.cs) —
      the one exception to "always stages, even on replay": its own `FindExistingTableGroupIdAsync`
      idempotency check runs *before* `TableGroup.Create`/`Stage` are ever reached, so its replay
      test asserts `Stage` is never called at all, not just never flushed.
- [ ] [`ReservationSeatedTableOccupiedHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/ReservationSeatedTableOccupiedHandler.cs) —
      one of the two Phase-7 cross-module event handlers, not a `*CommandHandler` by name;
      confirm `Stage` is still called even though `CrossCuttingTests`' architecture rule doesn't
      check it by name (see that class's own doc comment for why it takes the dependency anyway).
- [ ] [`RegisterStaffCommandHandler.cs`](../src/Modules/Mise.Modules.StaffIdentity.Application/RegisterStaff/RegisterStaffCommandHandler.cs) —
      confirm `LoginCommandHandler.cs` in the same folder was **not** touched (still `WriteAsync`)
      — the one handler with no gateway mutation to piggyback a staged entry onto.

### Keyed DI — registration + consumption

- [ ] Pick any one of `Add{Module}Persistence` extension methods (e.g.
      [`TablesPersistenceServiceCollectionExtensions.cs`](../src/Modules/Mise.Modules.Tables.Infrastructure/TablesPersistenceServiceCollectionExtensions.cs))
      — confirm `AddKeyedScoped<IAuditWriter, AuditWriter<TDbContext>>(AuditWriterKeys.X)` replaced
      the old unkeyed `AddScoped`, and `.AddInterceptors(new AuditCompletenessInterceptor())` is
      chained onto that module's `UseNpgsql(...)` call.
- [ ] Confirm every handler constructor's `IAuditWriter` parameter carries
      `[FromKeyedServices(AuditWriterKeys.X)]` with the *matching* module key — a mismatched key
      here would reintroduce the exact bug this phase found and fixed, silently.

### `StaffIdentitySeeder` — the bootstrap fix

- [ ] [`StaffIdentitySeeder.cs`](../src/Modules/Mise.Modules.StaffIdentity.Infrastructure/StaffIdentitySeeder.cs) —
      confirm it now stages an `AuditLogEntry` with `PerformedBySystemProcess = "StaffIdentitySeeder"`
      (`PerformedByStaffId = null`) before its own `SaveChangesAsync` — the first real call site
      for `PerformedBySystemProcess`, declared on `AuditLogEntry` since Phase 2 but unused until
      now. Without this, marking `StaffUser` as `IAuditableEntity` would make the interceptor
      reject this exact save, permanently breaking `Mise.MigrationService`'s bootstrap (nobody
      could ever seed the first Manager). Confirm `TimeProvider` is now also registered in
      [`Mise.MigrationService/Program.cs`](../src/Mise.MigrationService/Program.cs) — the seeder
      needed it and `Mise.MigrationService` had never registered it before.

### Read side — endpoints and DTO

- [ ] [`ReservationsEndpoints.cs`](../src/Mise.ApiService/Reservations/ReservationsEndpoints.cs) /
      [`TablesEndpoints.cs`](../src/Mise.ApiService/Tables/TablesEndpoints.cs) — the new
      `GET .../audit-history` routes, `RequireAuthorization("Manager")` (not `FloorStaff`, unlike
      most reads — US-05 frames this as an accountability concern). Confirm both return `200` with
      `[]` for an id with no rows, never `404`.
- [ ] [`AuditHistoryEntryDto.cs`](../src/Mise.ApiService/AuditHistoryEntryDto.cs) — a root-level
      shared DTO (same placement convention as `ETag.cs`), not duplicated per endpoint file.
- [ ] [`Program.cs`](../src/Mise.ApiService/Program.cs) — confirm
      `AddScoped<IAuditReader, AuditReader<ReservationsDbContext>>()` is registered once, near
      `AddReservationsPersistence`, not per module.

### Migration

- [ ] `src/Modules/Mise.Modules.Reservations.Infrastructure/Persistence/Migrations/
      *_AddAuditLogEntryEntityTypeEntityIdIndex.cs` — confirm it does exactly one thing
      (`CreateIndex` on `(entity_type, entity_id)`), and that
      [`MigrationHistoryTests.cs`](../tests/Mise.IntegrationTests/MigrationHistoryTests.cs)'s
      exact-count assertion for Reservations was bumped from 2 to 3 in the same commit.

### PII redaction

- [ ] Confirm every `Details = ` assignment across the 19 staging call sites carries no
      `CustomerName`/`CustomerPhone`/`CustomerEmail`/`Notes` — grep for `Details = \$"` if you
      want to check by hand rather than trust the list. No production code changed for this
      phase; only the regression test below is new.

### Architecture tests

- [ ] [`LayeringTests.cs`](../tests/Mise.ArchitectureTests/LayeringTests.cs) — the new
      `SharedKernelInfrastructure_NeverReferencesEfCore` test, scanning
      `typeof(IAuditWriter).Assembly` directly (not via `ModuleAssemblies`, since
      `Mise.SharedKernel.Infrastructure` isn't a per-module assembly). Confirm you agree this
      closes a real gap: CLAUDE.md has claimed this project "must stay EF-free forever" since
      Phase 3, but nothing enforced it until now.
- [ ] Confirm `EveryCommandHandler_DependsOnSomethingImplementingIAuditWriter` still passes
      unmodified — it matches on the constructor parameter's type name only, so adding `Stage` to
      the interface and `[FromKeyedServices(...)]` to the parameter didn't need to touch it.

### New/changed test files, if you want to read the tests themselves

Unit: all ~19 existing `*CommandHandlerTests.cs`/event-handler test files changed their
`_auditWriter.Verify(a => a.WriteAsync(...), Times.X)` assertions to `a.Stage(...)` — most
`Times.Once`/`Times.Never` values stayed the same, but several `Times.Never` replay/rejection
assertions became `Times.Once` (staging now happens *before* the outcome is known) with a
`because:` string explaining why; `CreateSectionCommandHandlerTests`/`CreateTableCommandHandlerTests`/
`CreateServicePeriodCommandHandlerTests`/`RegisterStaffCommandHandlerTests` additionally switched
from asserting a mocked-echo `EntityId` to capturing the actual passed-in object via a Moq
`Callback` — worth reading one of these closely, it's a subtle but real distinction (the mocked
gateway doesn't echo the input the way `CreateReservationCommandHandlerTests` does).
Integration: new `tests/Mise.IntegrationTests/AuditCompletenessInterceptorTests.cs` (the negative
proof — a hand-built mutation with nothing staged, asserting the save is refused and nothing
persists), new audit-history sections in `ReservationsEndpointTests.cs`/`TablesEndpointTests.cs`
(happy path, empty history, `FloorStaff` → 403, unauthenticated → 401), and the redaction test
`Reservation_MutatedThroughFullLifecycle_AuditDetailsNeverContainCustomerPhoneOrEmail`.

---

## 3. Manual walkthrough — audit history end to end

With `aspire run` going, find `apiservice`'s URL from the Aspire dashboard.

- [ ] **Log in as the seeded Manager and capture the token**, same as every earlier phase's
      checklist:
  ```powershell
  $loginJson = curl.exe -s -X POST https://<apiservice-host>/api/auth/login `
    -H "Content-Type: application/json" `
    -d '{"username":"manager","password":"<your seed-manager-password>"}'
  $login = $loginJson | ConvertFrom-Json
  $token = $login.token
  ```

- [ ] **Create a reservation**:
  ```powershell
  $body = @{ operationId = [guid]::NewGuid(); customerName = "Audit Walkthrough"; customerPhone = "+32 470 00 00 00"; partySize = 2; reservationDateTime = (Get-Date).AddDays(1).ToString("o") } | ConvertTo-Json
  $create = curl.exe -s -X POST https://<apiservice-host>/api/reservations `
    -H "Authorization: Bearer $token" -H "Content-Type: application/json" -d $body | ConvertFrom-Json
  $id = $create.id
  $etag = $create.'@etag'  # or read the ETag response header directly if your client exposes it
  ```

- [ ] **Fetch its audit history immediately**:
  ```powershell
  curl.exe -s https://<apiservice-host>/api/reservations/$id/audit-history -H "Authorization: Bearer $token"
  ```
  - [ ] Expect exactly **one** entry: `"action":"Created"`, `"performedByStaffId"` equal to your
        Manager's id, `"details"` reading `"Party of 2."` — never the customer's name or phone.

- [ ] **Update the reservation** (PATCH with `If-Match: $etag`, any field change), then fetch
      `/audit-history` again.
  - [ ] Expect **two** entries, newest first: `"Updated"` then `"Created"`.

- [ ] **Fetch `/api/reservations/<a-random-guid-that-was-never-created>/audit-history`**.
  - [ ] Expect `200` with an empty array `[]`, not `404`.

- [ ] **Repeat the create + fetch against a Table** (`POST /api/sections`, `POST /api/tables`,
      then `GET /api/tables/{id}/audit-history`) — expect one `"Created"` entry.

- [ ] **Retry any of the GETs above with a `FloorStaff` token instead of `Manager`**.
  - [ ] Expect `403`, not `200` — confirm this matches US-05's Manager-only framing, distinct from
        FR-06's FloorStaff-reachable status-change endpoint.

- [ ] **Retry with no `Authorization` header at all**.
  - [ ] Expect `401`.

- [ ] **The interceptor's negative case is proven automatically**
      (`AuditCompletenessInterceptorTests.SaveChangesAsync_MutatesAnAuditableEntityWithNoStagedAuditEntry_ThrowsAndPersistsNothing`),
      not manually — there's no safe way to exercise "a handler forgot to call Stage" through the
      running API without temporarily breaking production code. Read that test if you want to see
      the guardrail fire for yourself.

## 4. Known, deliberate gaps — not bugs

Don't file these — they're documented scope boundaries for Phase 9, not oversights:

- **No Blazor UI of any kind.** No reservation/table history screen. Same deferral every phase
  since 4 has made — lands in Phase 10.
- **No queue, no async decoupling of the audit write from the mutation.** Discussed and settled
  with the user before implementation — see CLAUDE.md's ADR-009 "Scope boundaries" for the full
  reasoning (an in-memory queue can lose entries across a crash; even a durable external queue
  doesn't close the dual-write gap the way writing in the same DB transaction does).
- **`ConflictRecord.SubmittedPayloadJson` redaction stays deferred.** Charter correction #4 names
  two fields; only `AuditLogEntry.Details` is addressed this phase, since `ConflictRecord` doesn't
  exist until a later, offline/conflict-resolution phase.
- **Cross-module atomicity is still per-`DbContext`, not global.** `SeatReservationCommandHandler`'s
  audit write (Reservations' own `DbContext`) and `ReservationSeatedTableOccupiedHandler`'s audit
  write (Tables' own `DbContext`) are each individually atomic with their own module's mutation,
  but the two are still two separate transactions across two separate connections — unchanged from
  ADR-007's already-accepted "milliseconds within one request" cross-module design. Phase 9 doesn't
  attempt distributed-transaction atomicity across modules.
- **`PerformedBySystemProcess` has exactly one real caller** (`StaffIdentitySeeder`). No other
  system-initiated write exists yet — the retention job (Phase 17) will be the next.

## 5. Sign-off

- [ ] I reviewed the code sections above and have no unresolved concerns, **or** I've listed
      concerns below with file/line references.
- [ ] I ran the manual audit-history walkthrough and everything matched what's described above,
      **or** I've listed discrepancies below.

**Notes / concerns:**

```
(space for you to fill in)
```
