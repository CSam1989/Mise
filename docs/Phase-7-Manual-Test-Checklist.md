# Phase 7 Manual Test Checklist — Seating & Table Status (FR-05, FR-06, BR-04, BR-05, RISK-01)

Companion to the automated suites, not a replacement for them. Everything below is **already
proven automatically** by the suite counts in [Section 1](#1-re-run-the-automated-suites-yourself)
below — all green, plus the tiered coverage gate at ≥90% on `*.Domain`/`*.Application` (this
phase actually landed at 99.7%) and the format check. This checklist is for *you* to look at the
actual code and exercise the actual API before trusting that.

**Scope note, read first:** this phase makes staff able to seat a reservation at a table (FR-05),
change a table's status directly during service (FR-06), and enforces BR-04 ("cannot be marked
Seated without an assigned table") and BR-05 ("cancelling or no-showing a reservation frees its
table unless another active reservation holds it"). It's also the **first phase with a real
cross-module *write***, not just Phase 6's cross-module *read*: Reservations now changes Tables'
state as a side effect of its own commands, via the first real use of the hand-rolled
`IDomainEventPublisher`/`IDomainEventHandler<T>` pair ADR-005 named but nothing had wired up
before now. That resolution (**ADR-007**, in **CLAUDE.md's new "Seating & table status
(Phase 7)" section**) is the map for everything below — read that first. Two things stay
explicitly out of scope, matching how earlier phases deferred their own remainders: no SignalR
broadcast of the state changes this phase makes real (Phase 8's concern), and no
`currentReservationId` on the floor-plan DTO (FR-04, not in this phase's charter refs). Section 3
below is an API walkthrough — no Blazor UI landed this phase, same deferral every phase since 4
has made.

Check a box, or write a one-line note next to it if something looks off. Anything you flag, bring
back to the next session with the file/line and what you expected instead.

---

## 0. One-time setup

Unchanged from Phase 3–6 — no new secrets or Aspire parameters this phase.

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

- [ ] All five suites report green (400 tests: 18 architecture, 242 unit, 5 bUnit,
      131 integration, 4 E2E).
- [ ] `dotnet format Mise.slnx --verify-no-changes` reports no changes.
- [ ] Coverage gate reports **pass** — this phase actually landed at 99.7% on
      `*.Domain`/`*.Application` against the 90% floor.

If any of these are *not* green on your machine but I reported them green above, that is the
single most important discrepancy to flag.

---

## 2. Code review — walking the layers

Not exhaustive line-by-line — a guided tour hitting the decisions most worth a second opinion.
File paths are relative to the repo root.

### The design question this phase had to resolve

- [ ] Read **CLAUDE.md's new "Seating & table status (Phase 7)" section** first — especially
      **ADR-007**: why the cross-module event types live in `Reservations.Contracts` and are
      constructed by the Application layer directly, *not* routed through
      `AggregateRoot.DomainEvents` (which stays unused even after this phase — the doc comment
      explains why that's a deliberate choice, not an oversight).
- [ ] Confirm you agree that replay semantics for the domain-event dispatch are *supposed* to
      differ from the audit write (dispatched on every `Saved` outcome, including a replay —
      never gated by `WasAlreadyProcessed` the way the audit write is) — the reasoning is in
      `ReservationSeated`'s and `ReservationTableVacated`'s own doc comments
      ([`ReservationSeated.cs`](../src/Modules/Mise.Modules.Reservations.Contracts/ReservationSeated.cs),
      [`ReservationTableVacated.cs`](../src/Modules/Mise.Modules.Reservations.Contracts/ReservationTableVacated.cs)).

### SharedKernel.Infrastructure — the domain event plumbing

- [ ] [`IDomainEventPublisher.cs`](../src/Mise.SharedKernel.Infrastructure/IDomainEventPublisher.cs) /
      [`IDomainEventHandler.cs`](../src/Mise.SharedKernel.Infrastructure/IDomainEventHandler.cs) /
      [`DomainEventPublisher.cs`](../src/Mise.SharedKernel.Infrastructure/DomainEventPublisher.cs) —
      confirm the generic `PublishAsync<TEvent>` call site is genuinely reflection-free (ordinary
      `IServiceProvider.GetServices<T>()`), matching ADR-005's "no reflection-based discovery."
      Also confirm a handler's exception is left to propagate uncaught — no try/catch swallowing
      it inside the publisher.

### Reservations — Domain

- [ ] [`Reservation.cs`](../src/Modules/Mise.Modules.Reservations.Domain/Reservation.cs) —
      `MarkSeated`'s doc comment explains a real bug found during development: the first cut
      guarded "only from Confirmed," which rejected an `OperationId` replay of an
      already-seated reservation. Confirm you agree the fix (idempotent no-op only when the
      *same* `tableId` is being re-applied; still rejects a genuinely different `tableId`) is the
      right one, not a workaround that papers over a deeper issue.
  - [ ] `MarkNoShow` — confirm it's deliberately as lenient as `Cancel` (no guard on the prior
        status, idempotent no-op if already NoShow) rather than mirroring `MarkSeated`'s guard.

### Tables — Domain

- [ ] [`Table.cs`](../src/Modules/Mise.Modules.Tables.Domain/Table.cs) — three new methods:
      `SetStatus` (FR-06, genuinely no transition guard at all), `MarkOccupied` and
      `ReleaseIfReservationHeld` (both return `bool` — confirm you understand *why*: the caller
      is an event handler, not a REST-triggered command handler, and uses the return value to
      skip a redundant persist/audit write on a replayed event rather than needing its own
      OperationId-based idempotency table).

### The cross-module event handlers (Tables.Application)

- [ ] [`ReservationSeatedTableOccupiedHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/ReservationSeatedTableOccupiedHandler.cs) —
      confirm the table-not-found case (logged, not thrown) and the version-mismatch case
      (thrown, propagates) are deliberately handled differently — the doc comment explains one is
      a genuinely unrecoverable race and the other is transient and retry-recoverable via the
      replay-redispatch mechanism.
- [ ] [`ReservationTableVacatedHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/ReservationTableVacatedHandler.cs) —
      confirm the order of checks: table exists → `IReservationLookup` (BR-05's "unless" clause)
      → `Table.ReleaseIfReservationHeld` (which itself re-checks the status is reservation-driven).
      All three must agree before a release happens.
- [ ] [`IReservationLookup.cs`](../src/Modules/Mise.Modules.Reservations.Contracts/IReservationLookup.cs) /
      [`ReservationLookup.cs`](../src/Modules/Mise.Modules.Reservations.Infrastructure/Persistence/ReservationLookup.cs) —
      the reverse-direction ADR-006 mirror. Confirm you agree "currently holds it" should be
      time-scoped (does another reservation's window cover *now*) rather than "is there ever
      another active reservation on this table" — CLAUDE.md's section explains why the latter
      reading is actually unreachable given BR-01.

### Reservations — Application

- [ ] [`SeatReservation/SeatReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/SeatReservation/SeatReservationCommandHandler.cs) —
      confirm it reuses `IReservationsData.UpdateReservationAsync` rather than a bespoke gateway
      method (the doc comment explains why that's correct here, unlike Cancel/NoShow which reuse
      a `checkOverlap: false` path).
- [ ] [`MarkReservationNoShow/MarkReservationNoShowCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/MarkReservationNoShow/MarkReservationNoShowCommandHandler.cs) —
      no FluentValidation validator, same reasoning as `CancelReservationCommandHandler`.
- [ ] [`CancelReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/CancelReservation/CancelReservationCommandHandler.cs) —
      confirm the new `ReservationTableVacated` publish was added *without* otherwise changing
      Cancel's existing Phase 6 behavior.

### Tables — Application

- [ ] [`ChangeTableStatus/ChangeTableStatusCommandHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/ChangeTableStatus/ChangeTableStatusCommandHandler.cs) —
      confirm `Status` travels as a validated string (exact-message 400 on an invalid value) built
      on `Enum.TryParse`, not relying on ASP.NET Core's default JSON enum-binding behavior.

### API

- [ ] [`Reservations/ReservationsEndpoints.cs`](../src/Mise.ApiService/Reservations/ReservationsEndpoints.cs) —
      `/seat` and `/no-show` are both `FloorStaff` policy, matching every other Reservations
      endpoint (FR-01–03's "no permission boundary" reasoning extends here).
- [ ] [`Tables/TablesEndpoints.cs`](../src/Mise.ApiService/Tables/TablesEndpoints.cs) —
      `/status` is `FloorStaff` policy, a deliberate departure from Create/Update/Deactivate's
      `Manager` policy. Confirm you agree with the reasoning (operational vs. configuration
      action) in that file's own doc comment and CLAUDE.md's Phase 7 section.
- [ ] [`Program.cs`](../src/Mise.ApiService/Program.cs) — the two `IDomainEventHandler<T>`
      registrations live here, not inside `AddTablesPersistence`. Confirm you agree this is the
      right place for a cross-module composition decision (see
      [`TablesPersistenceServiceCollectionExtensions.cs`](../src/Modules/Mise.Modules.Tables.Infrastructure/TablesPersistenceServiceCollectionExtensions.cs)'s
      own doc comment explaining why it *isn't* there).

### Architecture tests

- [ ] Rerun `dotnet test tests/Mise.ArchitectureTests` yourself — all 18 rules should stay green
      with the new cross-module references in place:
      `Mise.Modules.Reservations.Application → Mise.Modules.Reservations.Contracts` (own
      Contracts, new),
      `Mise.Modules.Reservations.Infrastructure → Mise.Modules.Reservations.Contracts` (own
      Contracts, new — mirrors Tables' Phase 6 pattern),
      `Mise.Modules.Tables.Application → Mise.Modules.Reservations.Contracts` (the mirror image of
      Reservations.Application's own Phase 6 reference to Tables.Contracts).

### New test files, if you want to read the tests themselves

Unit: `tests/Mise.UnitTests/Reservations/SeatReservationCommand{Handler,Validator}Tests.cs`,
`tests/Mise.UnitTests/Reservations/MarkReservationNoShowCommandHandlerTests.cs`,
`tests/Mise.UnitTests/Tables/ChangeTableStatusCommand{Handler,Validator}Tests.cs`,
`tests/Mise.UnitTests/Tables/Reservation{Seated,TableVacated}*HandlerTests.cs`,
`tests/Mise.UnitTests/SharedKernel/DomainEventPublisherTests.cs`, plus additions to the existing
`ReservationTests.cs`/`TableTests.cs`/`CancelReservationCommandHandlerTests.cs`.
Integration: the new "Seat"/"No-show"/"BR-05" sections in
`tests/Mise.IntegrationTests/ReservationsEndpointTests.cs` (worth a closer read for
`PatchReservationSeat_MarksTableOccupied_InSameRequest`, the RISK-01 proof, and
`PatchReservationNoShow_AnotherActiveReservationCurrentlyHoldsTheTable_TableStaysOccupied`, BR-05's
"unless" clause), and the new "Change status" section in `TablesEndpointTests.cs`.

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

- [ ] **Create a Section and a Table, then a Confirmed reservation against it:**
  ```powershell
  $section = curl.exe -s -X POST https://<apiservice-host>/api/sections -H "Content-Type: application/json" -H $auth `
    -d '{"operationId":"<guid>","name":"Patio","displayOrder":0}' | ConvertFrom-Json
  $tableResp = curl.exe -i -X POST https://<apiservice-host>/api/tables -H "Content-Type: application/json" -H $auth `
    -d "{`"operationId`":`"<guid>`",`"sectionId`":`"$($section.id)`",`"name`":`"T1`",`"minCapacity`":2,`"maxCapacity`":4,`"isCombinable`":false}"
  # capture the table id from the body and the table's ETag from the response headers
  $reservationResp = curl.exe -i -X POST https://<apiservice-host>/api/reservations -H "Content-Type: application/json" -H $auth `
    -d '{"operationId":"<guid>","customerName":"Jane Doe","customerPhone":"+32 470 00 00 00","partySize":4,"reservationDateTime":"2026-10-01T19:00:00Z"}'
  # capture the reservation id and its ETag
  ```

- [ ] **Seat the reservation at the table:**
  ```powershell
  curl.exe -i -X PATCH https://<apiservice-host>/api/reservations/<reservationId>/seat `
    -H "Content-Type: application/json" -H $auth -H "If-Match: <reservation ETag>" `
    -d '{"operationId":"<guid>","tableId":"<tableId>"}'
  ```
  Expect `200 OK`, `status: "Seated"`.

- [ ] **Immediately check the floor plan** — `GET /api/tables/floor-plan` — the same table now
      shows `"status": "Occupied"`. This is RISK-01's proof: no delay, no polling, it's already
      true by the time the seat response came back.

- [ ] **Try to seat it again at a different table** (same reservation id, a fresh If-Match, a
      *different* `tableId`) — expect `409 Conflict` (a genuine conflict, not a replay).

- [ ] **No-show a fresh reservation** (create a new one, don't seat it):
      `PATCH /api/reservations/{id}/no-show` with a fresh `If-Match` — expect `200 OK`,
      `status: "NoShow"`.

- [ ] **Change a table's status directly (FR-06), as FloorStaff** (mint one with
      `./scripts/mint-dev-token.ps1 -Role FloorStaff`):
      `PATCH /api/tables/{id}/status` with `{"operationId":"<guid>","status":"NeedsCleaning"}` and
      a fresh `If-Match` — expect `200 OK` (confirming FR-06's FloorStaff-or-Manager access,
      unlike Create/Update/Deactivate's Manager-only).

- [ ] **Cancel the seated reservation from earlier** (fresh `If-Match`) — expect `200 OK`, then
      re-check the floor plan: the table is back to `"status": "Available"` (BR-05).

- [ ] **Unauthenticated**, repeat any call above — expect `401 Unauthorized`.

## 4. Manual walkthrough — the database

```sql
select id, customer_name, status, table_id from reservations.reservation order by created_at_utc desc limit 10;
select id, name, status from tables.table order by name;
select entity_type, entity_id, action, performed_by_staff_id, occurred_at_utc from shared.audit_log_entry
  where action in ('Seated', 'NoShow', 'Occupied', 'Released', 'StatusChanged') order by occurred_at_utc desc limit 20;
```

- [ ] A seated reservation's row shows `status = 'Seated'` and the matching `table_id`.
- [ ] The corresponding table row shows `status = 'Occupied'` — set by the event handler, not by
      the reservation endpoint itself (two separate `UPDATE`s, same request).
- [ ] `shared.audit_log_entry` shows **two** rows for one seat action: one `entity_type =
      'Reservation', action = 'Seated'` and one `entity_type = 'Table', action = 'Occupied'` —
      the cross-module handler writes its own audit entry, attributed to the same
      `performed_by_staff_id` as the original seat request.
- [ ] Cancelling/no-showing a seated reservation adds an `entity_type = 'Table', action =
      'Released'` row alongside the reservation's own `'Cancelled'`/`'NoShow'` row.

## 5. Known, deliberate gaps — not bugs

Don't file these — they're documented scope boundaries for Phase 7, not oversights:

- **No SignalR broadcast** of any state change this phase makes real. The cross-module write
  itself is real and synchronous within the request; nothing pushes it to other connected
  clients yet. Phase 8's concern.
- **No `currentReservationId` on the floor-plan DTO.** FR-04 isn't in this phase's charter refs.
- **`TableStatus.Reserved` is still never auto-set** by any command — only reachable via FR-06's
  direct `SetStatus`. The "upcoming within 30 minutes → Reserved" flag (US-03/FR-04) is a
  read-time concern, not this phase's.
- **BR-01 doesn't extend across a `TableGroup`'s other members, and Phase 7 didn't change that.**
  Seating a reservation that's part of a T1+T2 combinable group only marks T1 Occupied — T2
  still shows Available. Known since Phase 6; still not revisited.
- **`Table.ReleaseIfReservationHeld` can't distinguish "Occupied by this reservation's own
  seating" from "Occupied by an unrelated walk-in seated via FR-06."** Both look identical in the
  `Status` field alone. A narrow, real gap — see CLAUDE.md's Phase 7 section for the full
  reasoning and why it's not being fixed preemptively.
- **No `Seated → Completed` transition.** `Completed` remains declared but unreachable.
- **No Blazor UI for seating or table-status changes** — API-only, deferred to Phase 10 same as
  every module since Phase 4.

## 6. Sign-off

- [ ] I reviewed the code sections above and have no unresolved concerns, **or** I've listed
      concerns below with file/line references.
- [ ] I ran the manual API + database walkthroughs and everything matched what's described above,
      **or** I've listed discrepancies below.

**Notes / concerns:**

```
(space for you to fill in)
```
