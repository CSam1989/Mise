# Phase 10: Shared RCL build-out + Blazor screens per the new mockups

## Context
Phases 4–9 kept every feature API-only and deferred all screens to Phase 10. The new design
(`docs/Mise UI mockups design.zip`, "Mise Prototype", 23 frames) replaces the old Lilshof mockup.
The current UI is one form (`ReservationForm`), stock template layout/nav, Bootstrap from the
template, no `TestIds`, and localization only on `Login.razor`. Goal: implement the design step by
step, one screen family per green commit, and stop for review after each step.

**Your decisions:** build every mockup screen now, with later-phase screens as shells; fill the API
gaps inside the step that needs them; use default Bootstrap as much as possible and custom CSS
only where Bootstrap can't express the design; one screen family per step.

## Assumptions (tell me if any are wrong)
1. **Bootstrap moves into the RCL** (`Mise.UI.Components/wwwroot/lib/bootstrap`, served as
   `_content/Mise.UI.Components/...`) so the Phase 14 MAUI head gets the same styling. Theming is
   done by overriding Bootstrap 5.3 CSS variables (`--bs-primary` = mockup navy, `--bs-body-font-family`
   = Instrument Sans, radii) in a single small `mise-theme.css`. Custom CSS is used only for what
   Bootstrap has no equivalent for: the 5 table-status and 5 reservation-status colour sets, the
   floor canvas and absolutely positioned tables, the timeline grid, the drag ghost, and the history
   timeline. Component-specific CSS goes in `.razor.css` isolation files.
2. **Web layout is responsive, not a `device` prop.** Bootstrap breakpoints handle tablet (1024px)
   vs desktop (1280px). The mockup's tablet-only behaviour (hiding Staff and Devices) applies only
   once MAUI exists.
3. **Shell screens** (PIN sign-in, lockout, revoked, pair device, Devices, Conflict queue, offline
   banner, sync dots):
   - Built as real RCL components against new `Mise.UI.Abstractions` interfaces
     (`IPinAuthClient`, `IDevicesClient`, `IConflictsClient`, `IConnectivityState`).
   - Backed by in-memory `Preview*` implementations in `Mise.Web/Services/Preview`.
   - Reachable only through a Development-only `/design` gallery, never the production nav.
   - Phases 13/15/16 replace the fakes with real clients. This will be documented in CLAUDE.md as a
     deliberate exception to "no premature production code".
4. **Strings:** every new string goes through `IStringLocalizer` with an English `.resx` in the RCL,
   using the mockup's `T.en` key names. The nl-BE values (already in the mockup's `T.nl`) and a
   culture switcher stay in Phase 11.
5. **"Today" means the restaurant's local day.** A new `RestaurantOptions.TimeZoneId`
   (`Europe/Brussels`) is added, and the reservation day filter uses the local midnight window, not
   UTC. `TimeProvider` is used everywhere.
6. **Moving an already-seated party** to another table (a drag in the mockup) is new domain
   behaviour. Today `MarkSeated` rejects a different `tableId`. It gets its own domain method and
   test coverage in step 10.2, including BR-05 release of the old table to NeedsCleaning.
7. **Cancel / no-show reasons** are a new optional `Reason` enum on those two requests. The reason
   is written to the audit `Details` field. It contains no PII.
8. **Audit history** in the reservation drawer is shown only to Managers, matching the existing
   Manager-only endpoint policy.
9. **Save layout** in the editor sends one existing `PATCH /api/tables/{id}` (with If-Match) per
   changed table. There is no bulk endpoint. On a 409, the UI shows a reload prompt.
10. **Drag and drop** uses Blazor pointer events plus one small JS module in the RCL for
    `elementFromPoint`, because Blazor has no hit-testing. That module is the only JS interop.
    Server-side rules stay authoritative; the client-side drop check only drives the visual hints.

## Skills and tools used per step
- `sdlc-ui-designer` + `design:design-handoff`: token and component spec at the start of 10.0.
- `sdlc-client-dev-blazor-components`: RCL components.
- `sdlc-client-dev-blazor-server`: Mise.Web pages, circuit, clients.
- `sdlc-backend-dev` + `sdlc-dba`: API gaps and the `Table.Shape` migration.
- `sdlc-testing`: bUnit, integration and E2E per the test contract.
- `design:accessibility-review`: once per step (touch targets, contrast, keyboard use of drawers/modals).
- `design:ux-copy`: strings the mockup lacks, such as the login error state.
- `code-review` + `simplify`: before each commit.
- `run`: to launch the app via Aspire.
- **Playwright MCP:**
  - Extract the zip to the scratchpad and open the prototype side by side with the running app at
    1280×800 and 1024×768, to compare visuals per step (screenshots).
  - Explore flows before writing each E2E test.
  - This complements the checked-in `Mise.E2ETests`; it does not replace it.

## Steps (each ends in one green commit + CLAUDE.md update + Phase-10 manual checklist section)

**10.0 Foundation and shell.**
- Move Bootstrap into the RCL and add `mise-theme.css`. Fonts come from Google Fonts: Instrument Sans/Serif.
- Add the `TestIds` constants class. It was planned for Phase 2 and never landed. It lives in the
  RCL, is public, and is used by bUnit and Playwright.
- Add primitives: `PageHeader`, `StatusPill`, `StatusDot`, `Drawer`, `Modal`, a `Toast` service,
  `EmptyState`, `Stepper`, `SegmentedControl`.
- New `MainLayout`: dark top bar (wordmark, date/service chips, clock via `TimeProvider`,
  connection pill, user block) and a grouped, role-aware sidebar (the Manager group sits behind
  `AuthorizeView Policy="Manager"`).
- Restyle `Login.razor` to the "Back office" card.
- Add RCL localization.
- Add the `/design` gallery page.

**10.1 Live floor plan + table drawer (FR-04, FR-06, US-03).**
- API additions:
  - The floor-plan DTO gains `NextReservation` and a computed Reserved-within-30-min flag
    (`FakeTimeProvider` boundary test).
  - The day list uses the local-day filter.
- `ITablesClient`/`HttpTablesClient`, following the `HttpReservationsClient` token pattern.
- A concrete `SignalRFloorPlanStream` against `IFloorPlanStream`.
- `FloorPlanBoard`, section filter, legend, next-arrivals aside, and a table drawer with status
  change (If-Match, 409 handling).

**10.2 Drag and drop (FR-05).**
- Drag a guest onto a table to assign or seat.
- Drag a table to move a seated party (new domain method, assumption 6).
- Client-side drop check and ghost; JS hit-test module.
- E2E via Playwright mouse drag.

**10.3 Reservations list, search, new/edit modal (FR-01–03, US-01/02).**
- Day chips, Lunch/Dinner grouping, empty state.
- Replace `ReservationForm` with the new modal: party stepper, duration, table select with group
  capacity, phone match via search, capacity warning. Keep the mockup's exact party-size message.
- Expand `IReservationsClient` with search and update.

**10.4 Reservation detail, audit history, cancel, no-show, mark seated (US-05).**
- API: `GET /api/reservations/{id}`, plus the cancel/no-show `Reason` field (assumption 7).
- History timeline from the audit endpoint, Manager only.

**10.5 Day timeline.**
- Tables × time grid, with service-band shading from service periods, the "Now" line, and
  Unassigned lanes.
- Clicking a bar opens the 10.4 drawer.

**10.6 Tables, sections, table groups (FR-07, US-04, ADR-006).**
- API: list all tables including inactive (Manager), `GET /api/table-groups`, and reactivate for
  table and section (Activate buttons).
- Section cards, groups aside, and the new-group modal with its picker rules.
- Add section / add table modals.

**10.7 Floor plan editor.**
- `Table.Shape` (Round/Rectangle) migration, with the DTO and request fields.
- Canvas drag to move, inspector panel, unsaved-changes pill, save/discard (assumption 9).

**10.8 Service hours and closures (FR-08).**
- API: `GET /api/service-periods?from=&to=`.
- Week grid with stepper, add/edit period, open/closed toggle, closures aside.

**10.9 Staff management.**
- API: `GET /api/staff`, `PATCH /api/staff/{id}/role`, deactivate/reactivate (audit + OperationId).
- Staff grid and add-staff modal (the register endpoint already exists).
- The PIN column and the Clear PIN lock action are shells (Phase 13).

**10.10 Later-phase shells (assumption 3).**
- PIN sign-in, lockout, revoked, pair steps 1–3, Devices, Conflict queue.
- Offline banner and sync-dot rendering driven by `IConnectivityState`.
- Full bUnit coverage for each component.
- Playwright E2E for every shell flow via the `/design` gallery:
  - `MiseE2EFixture` runs Mise.Web in the Development environment for these tests.
  - Flows covered: PIN keypad entry through to lockout, pair steps 1→3, and the conflict queue's
    reassign/keep/reject actions.

## Test rules for every step (non-negotiable)
- **Every component gets full bUnit tests.** This covers primitives, layout, pages and shells. At
  minimum, one test for each:
  - Parameter/prop variant
  - Visual state: empty, loading, error, disabled
  - Permission-gated control, with Manager and FloorStaff both rendered
  - Emitted `EventCallback`
  - Validation message, asserting the exact string
  - Client failure path (generic error shown, no crash)
  - Clients are mocked with Moq. Selectors use only `TestIds`.
- **Every new UI flow gets a full Playwright E2E test** in `Mise.E2ETests`, against the real two-host
  fixture, as part of the same step. This includes:
  - Happy path, the key rejection path (e.g. capacity warning, 409 on a stale edit), and the
    role-gated path (FloorStaff can't see the Manager nav or screens).
  - The live-update flow in 10.1: two browser contexts, a status change in one appears in the
    other via `Expect(...)` auto-wait, with no sleeps.
  - Drag and drop in 10.2 via `Mouse.MoveAsync`/`DownAsync`/`UpAsync`.
- Each step lists its bUnit and E2E test names in its manual-checklist section, so coverage can be
  reviewed at the stop point.

## Reuse
- The token pattern in `src/Mise.Web/Services/HttpReservationsClient.cs` is the template for every
  new typed client.
- `IFloorPlanStream` and its payloads in `src/UI/Mise.UI.Abstractions/IFloorPlanStream.cs`.
- `ETag.cs`, `ConcurrencyConflictExceptionHandler`, and `AuditHistoryEntryDto` in `Mise.ApiService`.
- `TableAssignmentGuard` / `ITableAvailabilityLookup` for server-side capacity checks.
- `MiseE2EFixture`, `KestrelFactory`, and `LoginSteps` for E2E tests.
- `ReservationFormTests` as the bUnit pattern.

## Verification (per step)
- `dotnet build Mise.slnx -c Release` is clean (TreatWarningsAsErrors).
- `dotnet test` passes for Unit, Integration, Client (bUnit), E2E and Architecture. The UI boundary
  rules must stay green: the RCL references Abstractions only.
- Every new endpoint gets the full contract: happy path, 400 field, 403, 401, plus audit and
  OperationId replay if it mutates.
- Every new `data-testid` is used in a Playwright test in the same commit.
- Manual: `aspire run` via the `run` skill, then Playwright MCP side-by-side screenshots against the
  extracted prototype at both viewport sizes, and walk through the step's manual checklist section.
- Stop after each step for your review before starting the next.