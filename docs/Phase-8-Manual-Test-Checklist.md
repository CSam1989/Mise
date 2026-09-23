# Phase 8 Manual Test Checklist — SignalR Hub & Live Propagation (FR-10, NFR-04, US-03)

Companion to the automated suites, not a replacement for them. Everything below is **already
proven automatically** by the suite counts in [Section 1](#1-re-run-the-automated-suites-yourself)
below — all green, plus the tiered coverage gate at ≥90% on `*.Domain`/`*.Application` and the
format check. This checklist is for *you* to look at the actual code and exercise the actual hub
before trusting that.

**Scope note, read first:** this phase makes every connected staff device learn about a
table/reservation status change in near-real-time, over a new SignalR hub
(`Mise.ApiService.Realtime.FloorPlanHub`, `/hubs/floorplan`) broadcasting four frozen events:
`TableStatusChanged`, `ReservationCreated`, `ReservationUpdated`, `ReservationCancelled`. Read
**CLAUDE.md's new "Real-time propagation (Phase 8)" section (ADR-008)** first — it's the map for
everything below: why those four names specifically (the charter's own sample contracts
disagreed with themselves), why the broadcast port (`IRealtimeNotifier`) is called directly by
command handlers instead of routed through the existing cross-module domain-event mechanism, why
a broadcast failure is swallowed rather than failing the mutation, and why Seat/NoShow notify as
`ReservationUpdated` rather than a distinct event. **This phase is API/hub-only** — same
deferral every phase since 4 has made: no Blazor screen renders a live floor plan yet, and even
`Mise.UI.Abstractions.IFloorPlanStream` (the frozen client-side contract, landing now) has no
concrete implementation until Phase 10. Section 3 below is a hub walkthrough using a small
PowerShell/`.NET` script, not a browser — there's no UI screen to click through yet.

Check a box, or write a one-line note next to it if something looks off. Anything you flag, bring
back to the next session with the file/line and what you expected instead.

---

## 0. One-time setup

Unchanged from Phase 3–7 — no new secrets or Aspire parameters this phase.

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

- [ ] All five suites report green (18 architecture, 242 unit, 5 bUnit — all three verified in
      this session; **138 integration** (131 Phase-7 + 7 new `FloorPlanHubTests`) and **4 E2E**
      — expected counts, but **not personally verified this session**, see the note below).
- [ ] `dotnet format Mise.slnx --verify-no-changes` reports no changes (verified).
- [ ] Coverage gate reports **pass** — verified this session at **99.7%** on
      `*.Domain`/`*.Application` against the 90% floor.
- [ ] Architecture tests still report the same 18 rules green — this phase adds a new
      constructor dependency (`IRealtimeNotifier`) to six existing command handlers and a new
      `Mise.ApiService.Realtime` namespace, but no new module and no new forbidden reference, so
      the rule count itself shouldn't change. If it did, that's worth flagging.

**Transparency note:** the integration and E2E tiers could not be run in the session that wrote
this phase — Testcontainers couldn't reach the Docker daemon from that sandboxed shell (a
`npipe://./pipe/docker_engine` connection failure that also hit every *pre-existing* integration
test identically, confirming it's an environment limitation of that session, not a Phase 8
regression). What *was* verified there instead: a clean Release build (0 warnings under
`TreatWarningsAsErrors`), all 18 architecture rules, all 242 unit tests (including every new
`IRealtimeNotifier` assertion), all 5 bUnit tests, the format gate, and a manual live-host check
(`dotnet bin/Release/net10.0/Mise.ApiService.dll` against a dummy connection string) confirming
`/hubs/floorplan/negotiate` returns 401 with no token and 200 with a valid FloorStaff JWT passed
via `?access_token=`. Running `./scripts/test.ps1` yourself is what actually closes this gap —
treat this run as the first real one for the integration/E2E tiers, not a repeat.

If any of these are *not* green on your machine, that is the single most important thing to flag.

---

## 2. Code review — walking the layers

Not exhaustive line-by-line — a guided tour hitting the decisions most worth a second opinion.
File paths are relative to the repo root.

### The design questions this phase had to resolve

- [ ] Read **CLAUDE.md's new "Real-time propagation (Phase 8)" section** first — especially
      **ADR-008**'s two decisions: collapsing the charter's inconsistent event list down to four
      frozen names, and calling `IRealtimeNotifier` directly from command handlers rather than
      through `IDomainEventPublisher`/`IDomainEventHandler<T>` (ADR-005/007). Confirm you agree
      a generic "relay this over SignalR" concern has no natural owning module to react from,
      unlike Tables reacting to Reservations' `ReservationSeated`.
- [ ] Confirm you agree the realtime notify call being gated by `WasAlreadyProcessed` is the
      *opposite* of ADR-007's cross-module dispatch (which deliberately redispatches on replay)
      — and that this is the right call, not an inconsistency: nothing changed on a replay, so
      there's nothing new for a connected client to learn.
- [ ] [`SignalRRealtimeNotifier.cs`](../src/Mise.ApiService/Realtime/SignalRRealtimeNotifier.cs) —
      confirm every broadcast is wrapped in try/catch, logged at `Error`, and never rethrown.
      Confirm you agree this is correct: the triggering mutation already committed, so a failed
      push must not turn a successful write into a 500.

### `Mise.SharedKernel.Infrastructure` — the new cross-cutting port

- [ ] [`IRealtimeNotifier.cs`](../src/Mise.SharedKernel.Infrastructure/IRealtimeNotifier.cs) —
      confirm the interface and its two notification records (`TableStatusChangedNotification`,
      `ReservationChangedNotification`) are module-agnostic — no reference to any module's
      Contracts DTO, the same isolation `Mise.UI.Abstractions.ReservationDto`'s own doc comment
      already explains for a different layer.

### The hub itself

- [ ] [`FloorPlanHub.cs`](../src/Mise.ApiService/Realtime/FloorPlanHub.cs) — confirm
      `[Authorize(Policy = "FloorStaff")]` at the class level (server-to-client only, no
      client-invokable methods this phase — a live floor plan is a read).
- [ ] [`Program.cs`](../src/Mise.ApiService/Program.cs) — find the `OnMessageReceived` handler
      added to the JWT-bearer options. Confirm it only honors the `access_token` query-string
      value when the request path starts with `FloorPlanHub.RoutePattern`, leaving every other
      endpoint's `Authorization`-header requirement untouched. Confirm `AddSignalR()`,
      `AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>()`, and
      `app.MapHub<FloorPlanHub>(FloorPlanHub.RoutePattern)` are all present.

### Six command handlers, one new dependency each

- [ ] [`CreateReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/CreateReservation/CreateReservationCommandHandler.cs) —
      `NotifyReservationCreatedAsync` inside the `else` (not-already-processed) branch, after the
      audit write.
- [ ] [`UpdateReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/UpdateReservation/UpdateReservationCommandHandler.cs) —
      `NotifyReservationUpdatedAsync`, same placement.
- [ ] [`CancelReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/CancelReservation/CancelReservationCommandHandler.cs) —
      `NotifyReservationCancelledAsync`, same placement, sitting *above* the existing
      (differently-gated) `ReservationTableVacated` domain-event publish below it — the two
      gates side by side in one method is worth reading closely.
- [ ] [`SeatReservation/SeatReservationCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/SeatReservation/SeatReservationCommandHandler.cs) —
      confirm it notifies as `NotifyReservationUpdatedAsync` (ADR-008), not a `Seated`-specific
      event, and that this sits inside the audit's `else` block while the `ReservationSeated`
      domain-event publish two lines later is unconditional — same file, two different replay
      semantics, both deliberate.
- [ ] [`MarkReservationNoShow/MarkReservationNoShowCommandHandler.cs`](../src/Modules/Mise.Modules.Reservations.Application/MarkReservationNoShow/MarkReservationNoShowCommandHandler.cs) —
      same shape as Seat.
- [ ] [`ChangeTableStatus/ChangeTableStatusCommandHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/ChangeTableStatus/ChangeTableStatusCommandHandler.cs) —
      `NotifyTableStatusChangedAsync` with the new status string, same placement.

### The two Phase-7 cross-module handlers, extended

- [ ] [`ReservationSeatedTableOccupiedHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/ReservationSeatedTableOccupiedHandler.cs) —
      confirm the new `NotifyTableStatusChangedAsync` call sits after the audit write, inside the
      same early-return structure `MarkOccupied()`'s `false` result already short-circuits — a
      replayed event that was already a no-op still doesn't broadcast, with no extra gating code
      needed for that (the existing idempotency check already covers it).
- [ ] [`ReservationTableVacatedHandler.cs`](../src/Modules/Mise.Modules.Tables.Application/ReservationTableVacatedHandler.cs) —
      same shape, broadcasting the table's status *after* `ReleaseIfReservationHeld()` mutated it
      (so the payload reflects `Available`, not the pre-release `Occupied`).

### Architecture tests

- [ ] Rerun `dotnet test tests/Mise.ArchitectureTests` yourself — confirm all rules stay green
      with no new module registered (SignalR lives in `Mise.ApiService`/`Mise.SharedKernel.
      Infrastructure`/`Mise.UI.Abstractions`, none of which needed a new project reference to add
      this feature).
- [ ] Confirm `EveryCommandHandler_DependsOnSomethingImplementingIAuditWriter`/`...ILogger` still
      pass — the six touched handlers already had `IAuditWriter`/`ILogger`; `IRealtimeNotifier`
      itself has **no** equivalent forcing rule (see CLAUDE.md's "Scope boundaries" note on why
      that's deliberate, not an oversight).

### New/changed test files, if you want to read the tests themselves

Unit: every one of the six handlers' existing test files gained `Mock<IRealtimeNotifier>` plus
`Times.Once`/`Times.Never` assertions matching the audit-write gate (`tests/Mise.UnitTests/
Reservations/{Create,Update,Cancel,Seat,MarkReservationNoShow}*Tests.cs`, `tests/Mise.UnitTests/
Tables/{ChangeTableStatusCommandHandler,ReservationSeatedTableOccupiedHandler,
ReservationTableVacatedHandler}Tests.cs`).
Integration: the new `tests/Mise.IntegrationTests/FloorPlanHubTests.cs` — a real `HubConnection`
pinned to `HttpTransportType.LongPolling`, asserting arrival via `TaskCompletionSource` +
`WaitAsync` (never a sleep), covering unauthenticated rejection, a successful connect, and each
of the four events firing off a real REST mutation. `MiseApiFixture.CreateHubConnection(...)` is
the new helper backing all of them.

---

## 3. Manual walkthrough — connecting to the hub directly

There's no browser screen to click through yet (API/hub-only, per the scope note above). The
most direct way to *see* a live event land is a short PowerShell script using the SignalR .NET
client. With `aspire run` going, find `apiservice`'s URL from the Aspire dashboard.

- [ ] **Log in as the seeded Manager and capture the token**, same as every earlier phase's
      checklist:
  ```powershell
  $loginJson = curl.exe -s -X POST https://<apiservice-host>/api/auth/login `
    -H "Content-Type: application/json" `
    -d '{"username":"manager","password":"<your seed-manager-password>"}'
  $login = $loginJson | ConvertFrom-Json
  $token = $login.token
  $auth = "Authorization=Bearer $token"
  ```

- [ ] **Connect to the hub** (requires the SignalR client assembly — the simplest way is a tiny
      throwaway `dotnet run` script, or use `dotnet-script`/a scratch console app referencing
      `Microsoft.AspNetCore.SignalR.Client`):
  ```csharp
  var connection = new HubConnectionBuilder()
      .WithUrl("https://<apiservice-host>/hubs/floorplan", options =>
          options.AccessTokenProvider = () => Task.FromResult<string?>(token))
      .Build();
  connection.On<object>("TableStatusChanged", payload => Console.WriteLine($"TableStatusChanged: {payload}"));
  connection.On<object>("ReservationCreated", payload => Console.WriteLine($"ReservationCreated: {payload}"));
  connection.On<object>("ReservationUpdated", payload => Console.WriteLine($"ReservationUpdated: {payload}"));
  connection.On<object>("ReservationCancelled", payload => Console.WriteLine($"ReservationCancelled: {payload}"));
  await connection.StartAsync();
  Console.WriteLine("Connected. State: " + connection.State);
  ```
  - [ ] Expect `connection.State` to be `Connected` — no exception.

- [ ] **With the connection open, create a reservation** via `curl.exe` (same body shape as every
      earlier phase's checklist) — expect the console to immediately print a `ReservationCreated`
      line with the new reservation's id and `"status":"Confirmed"`.

- [ ] **Create a Section + Table, then seat that reservation at it** (`PATCH
      /api/reservations/{id}/seat` with a fresh `If-Match`) — expect **two** lines to print:
      `ReservationUpdated` (`"status":"Seated"`) and `TableStatusChanged` (`"status":"Occupied"`)
      — proving both the direct notify call and the Phase-7 cross-module handler's own notify
      call fire from the same request.

- [ ] **Cancel that seated reservation** — expect `ReservationCancelled` and a second
      `TableStatusChanged` (`"status":"Available"`), confirming BR-05's release path also
      broadcasts.

- [ ] **Change a table's status directly** (`PATCH /api/tables/{id}/status`,
      `{"operationId":"<guid>","status":"NeedsCleaning"}`, FloorStaff or Manager, fresh
      `If-Match`) — expect a third-shape `TableStatusChanged` (`"status":"NeedsCleaning"`), with
      no accompanying reservation event this time.

- [ ] **Try connecting a second `HubConnection` with no `AccessTokenProvider` at all** — expect
      `StartAsync()` to throw (an `HttpRequestException` about a non-success status code), never
      a silent connection.

## 4. Known, deliberate gaps — not bugs

Don't file these — they're documented scope boundaries for Phase 8, not oversights:

- **No Blazor UI of any kind.** No live floor-plan screen, no page subscribes to the hub. Same
  deferral every phase since 4 has made — lands in Phase 10.
- **`Mise.UI.Abstractions.IFloorPlanStream` has no concrete implementation yet.** The frozen
  contract lands now (MAUI's future sync work depends on the shape existing); its first
  implementation (and consuming screen) lands in Phase 10.
- **No nightly NFR-04 latency test.** The ~1–2s propagation budget is a separate, later
  performance-test concern per docs/plan.md, not part of this phase's per-push gate.
- **No "exactly once on replay" proof at the integration tier.** Proven at the unit tier
  (`Times.Never` on `IRealtimeNotifier` for every handler's replay branch) instead — a client
  delivery round-trip over long-polling has no reliable way to prove *absence* of a second event
  without a disguised sleep, which this project's testing conventions rule out.
- **`Reservation Created` has no test asserting its exact JSON payload shape beyond
  `reservationId`/`status`** in the integration suite — the unit tests already pin every field
  via Moq's `It.Is<ReservationChangedNotification>()`; the integration test's job is proving
  *arrival* over the wire, not re-verifying field mapping a second time.

## 5. Sign-off

- [ ] I reviewed the code sections above and have no unresolved concerns, **or** I've listed
      concerns below with file/line references.
- [ ] I ran the manual hub walkthrough and everything matched what's described above, **or**
      I've listed discrepancies below.

**Notes / concerns:**

```
(space for you to fill in)
```
