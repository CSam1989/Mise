# Phase 2 Manual Test Checklist — Walking Skeleton: Create a Reservation

Companion to the automated suites, not a replacement for them. Everything below is **already
proven automatically** (54 tests: 16 architecture, 22 unit, 3 bUnit, 11 integration, 2 E2E —
all green, plus the tiered coverage gate and format check). This checklist is for *you* to look
at the actual code and click through the actual app before trusting that.

Check a box, or write a one-line note next to it if something looks off. Anything you flag,
bring back to the next session with the file/line and what you expected instead.

---

## 0. One-time setup

- [ ] `dotnet --version` prints a `10.0.x` version (not an `11.x` preview).
- [ ] Docker Desktop (or equivalent) is running.
- [ ] `dotnet tool restore` has been run once in the repo root.
- [ ] `dotnet user-secrets set Parameters:jwt-signing-key <any-random-string> --project src/Mise.AppHost`
      has been run once — `aspire run` fails fast with a clear message if this is missing
      rather than starting with a broken auth scheme.
- [ ] Playwright browsers installed: `tests/Mise.E2ETests/bin/Release/net10.0/playwright.ps1
      install --with-deps chromium`.

## 1. Re-run the automated suites yourself

```powershell
./scripts/test.ps1        # build + all five suites + format check
./scripts/coverage.ps1     # tiered coverage gate (90% on *.Domain/*.Application)
```

- [ ] All five suites report green.
- [ ] `dotnet format Mise.slnx --verify-no-changes` reports no changes.
- [ ] Coverage gate reports **pass** (Reservations.Domain/.Application should show in the
      high-90s% — the report prints per-assembly numbers).

If any of these are *not* green on your machine but I reported them green above, that is the
single most important discrepancy to flag — it usually means something environment-specific
(a stale `bin`/`obj`, a port conflict, Docker not actually running).

---

## 2. Code review — walking the layers

Not exhaustive line-by-line — this is a guided tour hitting the decisions most worth a second
opinion. File paths are relative to the repo root.

### Domain
- [ ] [`src/Modules/Mise.Modules.Reservations.Domain/Reservation.cs`](../src/Modules/Mise.Modules.Reservations.Domain/Reservation.cs)
      — private constructor, `Create(...)` factory, only invariant is `PartySize > 0`. Confirm
      this matches what you'd expect from "Phase 2 is a thin slice, not the full module."
- [ ] [`ReservationStatus.cs`](../src/Modules/Mise.Modules.Reservations.Domain/ReservationStatus.cs)
      — only `Confirmed` exists yet, on purpose.

### Application
- [ ] [`Ports/IReservationsData.cs`](../src/Modules/Mise.Modules.Reservations.Application/Ports/IReservationsData.cs)
      — one method, atomic create-or-replay. Not a generic repository (CLAUDE.md's rule).
- [ ] [`CreateReservation/CreateReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/CreateReservation/CreateReservationCommandHandler.cs)
      — validates first (throws, never touches the gateway on failure), audits only when
      `WasAlreadyProcessed` is false. Does this order make sense to you?
- [ ] [`CreateReservation/CreateReservationCommandValidator.cs`](../src/Modules/Mise.Modules.Reservations.Application/CreateReservation/CreateReservationCommandValidator.cs)
      — one rule, exact string `"Party size is required and must be more than 0."` Check this
      string against the mockup in `docs/Restaurant reservations UI mockups.zip` yourself —
      I transcribed it from plan.md's quote of the mockup, not from the mockup file directly.

### Infrastructure
- [ ] [`Persistence/ReservationsDbContext.cs`](../src/Modules/Mise.Modules.Reservations.Infrastructure/Persistence/ReservationsDbContext.cs)
      and its `Configurations/` — schema `reservations` for the aggregate, schema `shared` for
      `processed_operation` and `audit_log_entry`. Snake_case columns, explicit types.
- [ ] [`Persistence/Migrations/20260915094422_InitialCreate.cs`](../src/Modules/Mise.Modules.Reservations.Infrastructure/Persistence/Migrations/20260915094422_InitialCreate.cs)
      — read the generated SQL-equivalent directly; this is the actual schema that will exist
      in Postgres. `EnsureSchema` for both `shared` and `reservations`.
- [ ] [`Persistence/ReservationsData.cs`](../src/Modules/Mise.Modules.Reservations.Infrastructure/Persistence/ReservationsData.cs)
      — the OperationId replay logic. Note the comment: genuinely concurrent duplicate
      submissions aren't hardened against yet (only sequential replay is), same trade-off
      BR-01's TOCTOU race gets a dedicated fix for in Phase 6.

### The placeholder auth spine
- [ ] [`src/Mise.ApiService/Program.cs`](../src/Mise.ApiService/Program.cs) — JWT-bearer setup
      + global fallback policy.
- [ ] [`src/Mise.Web/Services/PlaceholderAuthTokenHandler.cs`](../src/Mise.Web/Services/PlaceholderAuthTokenHandler.cs)
      — mints a fixed "Mise.Web" system identity on every outgoing call. Read CLAUDE.md's
      "Placeholder auth spine" section alongside this file — is the scope of what's
      temporary vs. permanent clear to you from the code alone, or only from the doc?

### UI
- [ ] [`src/UI/Mise.UI.Components/ReservationForm.razor(.cs)`](../src/UI/Mise.UI.Components/ReservationForm.razor)
      — `data-testid` on every field + the submit button; client-side party-size check before
      ever calling the transport.
- [ ] [`src/Mise.Web/Components/Pages/Reservations.razor`](../src/Mise.Web/Components/Pages/Reservations.razor)
      — read the `prerender: false` comment. This is a real gotcha (it's what caught the E2E
      test the first time it ran) — worth understanding even if you never hit it yourself.

### Architecture tests
- [ ] [`tests/Mise.ArchitectureTests/ModuleRegistry.cs`](../tests/Mise.ArchitectureTests/ModuleRegistry.cs)
      — `"Reservations"` is now listed.
- [ ] [`tests/Mise.ArchitectureTests/ModuleBoundaryTests.cs`](../tests/Mise.ArchitectureTests/ModuleBoundaryTests.cs)
      — the `Infrastructure_IsReferencedByNothingExceptTheCompositionRoot` exemption now
      covers `Mise.MigrationService` too. Is widening this exemption to a second host
      something you'd have approved, or does it deserve more scrutiny?

---

## 3. Manual walkthrough — the browser

```powershell
aspire run
```

- [ ] The Aspire dashboard opens. `postgres`, `migrations`, `apiservice`, `webfrontend` all
      show healthy/completed (migrations completes and exits — that's correct, not a crash).
- [ ] Open the `webfrontend` URL from the dashboard. The home page loads.
- [ ] Click **Reservations** in the nav menu (or go to `/reservations` directly).
- [ ] Leave **Party size** empty and click **Create reservation**.
  - [ ] An inline error appears under Party size reading exactly *"Party size is required and
        must be more than 0."* — no page reload, no navigation.
- [ ] Enter a customer name, a party size (e.g. `4`), pick a date/time, and submit.
  - [ ] The form clears.
  - [ ] The new reservation appears under "Today's reservations" immediately, with the
        customer name and party size.
- [ ] Refresh the page.
  - [ ] The list resets to "No reservations yet." — **this is expected, not a bug**: Phase 2
        only builds the *create* side; there's no query/list endpoint yet (that's Phase 6).
        The list you saw was populated client-side from the response of your own submission.
- [ ] Open browser dev tools → Network tab, submit again, and find the `POST
      /api/reservations` call. Confirm the request carries an `Authorization: Bearer …`
      header (Mise.Web attached it automatically — you didn't log in, because there's no
      login yet).

## 4. Manual walkthrough — the API directly

With `aspire run` still going, find `apiservice`'s URL from the Aspire dashboard. The examples
below use `curl.exe` explicitly — plain `curl` in Windows PowerShell is usually aliased to
`Invoke-WebRequest`, which doesn't take the same flags.

- [ ] **Unauthenticated request is rejected:**
  ```powershell
  curl.exe -i -X POST https://<apiservice-host>/api/reservations `
    -H "Content-Type: application/json" `
    -d '{"operationId":"11111111-1111-1111-1111-111111111111","customerName":"Curl Test","partySize":2,"reservationDateTime":"2026-10-01T19:00:00Z"}'
  ```
  Expect `401 Unauthorized`. (In PowerShell, single-quoted strings don't need the JSON's
  double quotes escaped — don't add backslashes, they'd end up literally in the body.)

- [ ] **Mint a token and retry, authenticated:**
  ```powershell
  $token = ./scripts/mint-dev-token.ps1
  curl.exe -i -X POST https://<apiservice-host>/api/reservations `
    -H "Content-Type: application/json" `
    -H "Authorization: Bearer $token" `
    -d '{"operationId":"22222222-2222-2222-2222-222222222222","customerName":"Curl Test","partySize":2,"reservationDateTime":"2026-10-01T19:00:00Z"}'
  ```
  Expect `201 Created`, a `Location` header, and a JSON body with `status: "Confirmed"`.

- [ ] **Replay the exact same request** (same `operationId`) a second time. Expect another
      `201 Created` with the **same** `id` in the body as the first response — not a new row.

- [ ] **Validation failure:** repeat with `"partySize": 0`. Expect `400` with a body shaped
      like `{"errors":{"PartySize":["Party size is required and must be more than 0."]}}`.

## 5. Manual walkthrough — the database

Find the Postgres connection details from the Aspire dashboard (`postgres` resource → connection string), then:

```sql
select id, customer_name, party_size, reservation_date_time, status from reservations.reservation;
select operation_id, resource_type, resource_id from shared.processed_operation;
select entity_type, entity_id, action, performed_by_system_process, occurred_at_utc from shared.audit_log_entry;
```

- [ ] Every reservation you created above (browser + curl) has exactly one row in
      `reservations.reservation`.
- [ ] `shared.processed_operation` has exactly one row per **distinct** `operationId` you
      used — the replayed curl request did not add a second row.
- [ ] `shared.audit_log_entry` has exactly one `Created` row per reservation, with
      `performed_by_system_process` set to `Mise.Web` (browser-created ones) or
      `manual-tester` (curl ones, or whatever `-Name` you passed to the mint script).
- [ ] `select * from reservations."__ef_migrations_history";` shows the `InitialCreate`
      migration. There is deliberately **no** `shared."__ef_migrations_history"` — `shared`'s
      two tables are mapped from this same `ReservationsDbContext`, not a DbContext of their
      own, so they ride on Reservations' one migration stream (CLAUDE.md's "Cross-cutting
      infrastructure" section explains why, and that a second module needing these tables is
      what forces a real decision here — not solved speculatively now).

---

## 6. Known, deliberate gaps — not bugs

Don't file these — they're documented scope boundaries for Phase 2, not oversights:

- No search, no list/query endpoint, no floor plan, no seating — Phases 4-7.
- No real login, no roles, no PIN, no StaffIdentity — Phase 3. Every caller today is
  functionally anonymous-but-token-holding.
- No audit UI, no retention job — Phases 9 and 17.
- The audit write is a second `SaveChangesAsync`, not atomic with the reservation insert —
  Phase 9's `SaveChangesInterceptor` closes that gap.
- Concurrent (not sequential) duplicate `OperationId` submissions aren't hardened against.
- `Table`/`Reservation` overlap/capacity rules (BR-01/BR-07) don't exist — any date/time and
  any party size (above 0) is accepted, with no table to actually seat at yet.

---

## 7. Sign-off

- [ ] I reviewed the code sections above and have no unresolved concerns, **or** I've listed
      concerns below with file/line references.
- [ ] I ran the manual browser + API + DB walkthroughs and everything matched what's
      described above, **or** I've listed discrepancies below.

**Notes / concerns:**

```
(space for you to fill in)
```
