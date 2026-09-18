# Coteng Restaurant Reservations — Project Charter

**Author:** Claude (SDLC: Product Manager, Functional Analyst, Architect, System Designer roles)
**For:** Sam, Coteng BE
**Date:** 2026-09-14
**Status:** DRAFT — pending answers to Open Questions (Section 7)

---

## 1. Problem Statement

Front-of-house staff currently take reservations by phone with no shared system, and seat guests during service based on memory/paper — this makes it hard to see table availability at a glance, avoid double-booking, and hand off between shifts. This project builds an internal staff tool — **not a customer-facing app** — that lets staff log phone reservations and manage live table seating during service, from either a floor tablet or a desktop/back-office browser.

## 2. Goals & Success Metrics

- Staff can create/find/edit a reservation for an incoming phone call in under 30 seconds → measured informally by staff feedback post-launch.
- Zero double-booked tables caused by the system (the system, not memory, is now the source of truth) → tracked via a "table conflict" count, target 0.
- A manager can see the full floor status (which tables are free/reserved/occupied) at a glance during service.

## 3. Non-Goals (out of scope for v1)

- Customers booking their own reservations (explicitly excluded — staff-only).
- Automated SMS/email confirmations to customers (staff confirm verbally).
- Payment/deposit collection.
- Pre-ordering, loyalty, reviews.
- Integration with POS, phone systems, or any external system.
- Multi-restaurant/multi-location support (single restaurant only).

## 4. Personas

| Persona | Role | Needs |
|---|---|---|
| **Floor Staff** (host/server) | Takes phone calls, seats guests during service | Fast reservation entry, clear live floor view, quick table status changes |
| **Manager** | Runs the floor, configures the room | Everything Floor Staff can do, plus manage tables/sections/service hours, view history |

## 5. Requirements

### Functional

- **FR-01**: Staff can create a reservation with customer name, phone number, party size, date/time, and optional notes (allergies, occasion, VIP, seating preference).
- **FR-02**: Staff can search/find a reservation by customer name, phone number, or date.
- **FR-03**: Staff can edit or cancel an existing reservation.
- **FR-04**: Staff can view a live floor plan showing every table's current status (Available / Reserved / Occupied / Needs Cleaning / Blocked).
- **FR-05**: Staff can assign a reservation to a specific table and mark the party as seated.
- **FR-06**: Staff can change a table's status directly during service (e.g., mark occupied, mark needs cleaning, mark available) independent of a reservation.
- **FR-07**: Managers can create/edit/deactivate tables and sections (name, capacity, combinable flag, position on the floor plan).
- **FR-08**: Managers can define service periods/hours (e.g., Lunch, Dinner) and mark days closed.
- **FR-09**: Every create/edit/cancel action on a reservation or table is attributed to the staff member who performed it and timestamped (audit trail).
- **FR-10**: Both the mobile app (MAUI) and the website (Blazor) reflect table/reservation status changes made on the other in near-real-time.

### Non-Functional

- **NFR-01 — Security**: All endpoints require authenticated staff sessions; authorization enforced per-request based on role (Floor Staff vs Manager), not just at login. See ADR-001 §Security.
- **NFR-02 — Auditability**: Reservation and table changes are traceable to a staff member (supports accountability, dispute resolution, and the "assume breach" posture).
- **NFR-03 — Concurrency**: Two staff members editing the floor plan simultaneously must not silently overwrite each other's changes or double-assign a table.
- **NFR-04 — Latency**: Table status changes propagate to all connected staff devices within ~1–2 seconds during service, *when online*.
- **NFR-05 — Availability**: The system should be reliably available during service hours; brief overnight maintenance windows are acceptable.
- **NFR-06 — Data minimization & retention**: Customer records store only what's needed to run a reservation (name, phone, optional email, notes) — no payment or account data, since there's no self-service login. Contact fields are automatically anonymized after a configurable retention period of inactivity (BR-10, Section 10).
- **NFR-07 — Usability**: Core reservation entry and table status changes must be completable one-handed / quickly on a tablet at a busy host stand.
- **NFR-09 — Offline availability (HARD REQUIREMENT, added 2026-09-14)**: The MAUI app's floor plan view and core actions (create/edit reservation, change table status) remain fully usable with **zero network connectivity**. This supersedes the original "fail closed on disconnect" assumption in Section 8 — see **ADR-002**.
- **NFR-10 — Conflict surfacing**: Any offline action that cannot be reconciled with server state at sync time is visibly flagged to staff for manual resolution — never silently applied or silently dropped (see BR-08/BR-09, ADR-002).

## 6. User Stories (initial set — Functional Analyst)

### EPIC 1 — Reservation Management

```
US-01: As Floor Staff, I want to create a reservation while a customer is on the phone,
So that the booking is captured immediately and visible to the rest of the team.

Acceptance Criteria:
- GIVEN a caller requests a table WHEN I enter name, phone, party size, and date/time and save
  THEN the reservation appears in the day's list with status "Confirmed" immediately (AQ-01, resolved)
- GIVEN I enter a phone number already on file WHEN I start typing it THEN the system suggests the matching customer's past reservation details (name, notes) to avoid re-asking
- (edge case) GIVEN I submit with no party size THEN the system rejects the save with a clear inline validation message (never silently defaults to 1)

Business Rules:
- BR-01: A table cannot be linked to two reservations whose time ranges overlap.
- BR-07: Requested party size must fit within the assigned table's capacity, or an explicitly combinable set of tables.

Out of Scope: customer self-service booking, payment.
```

```
US-02: As Floor Staff, I want to search reservations by name, phone, or date,
So that I can find a booking quickly when a customer calls back or arrives.

Acceptance Criteria:
- GIVEN I type a partial name or phone number THEN matching reservations appear within 1 second
- GIVEN no results match THEN I see a clear "no reservations found" state, not a blank screen
```

### EPIC 2 — Floor Plan & Live Seating

```
US-03: As Floor Staff, I want to see every table's live status on a floor plan,
So that I can seat walk-ins and reservation parties without checking with other staff.

Acceptance Criteria:
- GIVEN the floor plan is open WHEN another staff member changes a table's status THEN my view updates within ~2 seconds without a manual refresh
- GIVEN a table has an upcoming reservation within the next 30 minutes THEN it's visually flagged as "Reserved" ahead of time

Business Rules:
- BR-04: A reservation cannot be marked "Seated" without an assigned table.
- BR-05: Cancelling or no-showing a reservation frees its table unless another active reservation holds it.
```

```
US-04: As a Manager, I want to configure tables and sections (capacity, combinable groups, layout),
So that the floor plan matches how the room is actually set up.

Acceptance Criteria:
- GIVEN I am a Manager WHEN I add/edit/deactivate a table THEN Floor Staff cannot perform this action (403 if attempted)
- (edge case) GIVEN a table has an active reservation WHEN I try to deactivate it THEN I'm blocked with a clear reason, not a silent failure
```

### EPIC 3 — Accountability

```
US-05: As a Manager, I want to see who created or changed a reservation or table status and when,
So that I can resolve disputes ("who double-booked this?") and maintain accountability.

Acceptance Criteria:
- GIVEN any reservation or table change THEN an audit entry records staff member, action, and timestamp
- GIVEN I open a reservation's history THEN I see a chronological list of every change
```

### EPIC 4 — Offline Resilience & Conflict Resolution (added 2026-09-14 — see ADR-002)

```
US-06: As a Manager, I want to review and resolve sync conflicts,
So that no double-booking or missed reservation caused by two offline devices goes unnoticed.

Acceptance Criteria:
- GIVEN two devices each created a booking for the same table while both were offline
  WHEN they reconnect THEN one syncs cleanly and the other appears in my Conflict queue
  showing both bookings' full details, not just an error code
- GIVEN a conflict is in the queue WHEN I choose to reassign it to a different table
  THEN the system re-validates capacity/availability before accepting my resolution (BR-07 still applies)
- GIVEN a conflict is left unresolved THEN it stays visible indefinitely — it never silently expires or auto-resolves

Business Rules:
- BR-08: An offline (Outbox) operation that fails server-side revalidation on sync is marked
  Conflict, never silently applied or discarded, and remains visible until a Manager resolves it.
- BR-09: Every table/reservation record shown in the MAUI app carries a visible sync state —
  Synced / Pending sync / Conflict — so staff always know whether what they're looking at is confirmed.

Out of Scope: automatic conflict resolution (e.g. "last write wins") — deliberately excluded,
see ADR-002 rationale.
```

```
US-07: As Floor Staff, I want to keep taking bookings and seating guests when the tablet has no signal,
So that a wifi drop never stops service.

Acceptance Criteria:
- GIVEN the tablet has no network connectivity WHEN I create/edit a reservation or change a table's status
  THEN the action succeeds locally and is visibly marked "Pending sync"
- GIVEN connectivity returns THEN pending actions sync automatically without staff having to do anything
- (edge case) GIVEN the app is closed/killed mid-sync WHEN it's reopened THEN pending actions are not lost or double-applied (idempotent replay via OperationId)
```

### EPIC 5 — Staff Authentication (added 2026-09-14, see ADR-001 Amendment 2)

```
US-08: As Floor Staff, I want to sign in on the host-stand tablet with just my PIN,
So that I can get to work quickly during a busy shift without typing a full password each time.

Acceptance Criteria:
- GIVEN the tablet is a paired device WHEN I enter my correct PIN THEN I'm signed in as myself,
  not a shared/generic account, and my subsequent actions are attributed to me (NFR-02)
- GIVEN I enter an incorrect PIN 5 times WHEN I try again THEN I'm locked out and told to use
  the website with my password to reset my PIN
- (edge case) GIVEN the tablet's pairing has been deactivated by a Manager WHEN I try to PIN in
  THEN I'm rejected regardless of PIN correctness — the device, not just the PIN, must be trusted

Business Rules:
- BR-11: Only a Manager can pair a new device.
- (device binding) PIN login is rejected outright on any device without an active DeviceRegistration.

Out of Scope: biometric login, PIN reset via the MAUI app itself (must go through the website).
```

```
US-09: As a Manager, I want to deactivate a lost or stolen tablet's pairing,
So that nobody can use it to sign in as staff anymore.

Acceptance Criteria:
- GIVEN a tablet is reported lost WHEN I deactivate its DeviceRegistration from the website
  THEN every *new* PIN login attempt from that device is rejected as soon as the device has
  connectivity to check
- (edge case, flagging honestly rather than overpromising) GIVEN the tablet is already signed in
  and currently offline WHEN I deactivate it THEN that existing session can still act locally
  until its 12-hour grace period (ADR-002) expires or it reconnects — deactivation stops new
  logins immediately once reachable, it cannot forcibly end a session already running with no
  network path to reach it. If instant revocation of an active offline session matters to you,
  that requires a shorter grace period or a push-based kill-switch, which is added complexity I
  haven't built in without you asking.
```

### Ambiguity Log

```
[AQ-01] Does a reservation need a "Requested → Confirmed" step, or is every staff-entered
        booking implicitly confirmed (since staff already spoke to the customer)?
Status: RESOLVED (2026-09-14)
Answer: Every staff-entered booking is Confirmed immediately — no Requested state.
        Reservation.Status's first value is now simply Confirmed (Section 10, US-01 updated).
Impact: Reservation state machine simplified — one fewer state to build and test.

[AQ-02] Retention period for customer contact data (name/phone) — GDPR requires a defined
        retention/deletion policy since this is real customer PII, even without self-service accounts.
Status: RESOLVED (2026-09-14) — mechanism defined by you; one parameter still open.
Answer: Partial anonymization via a nightly job — see BR-10 and the new "Data Retention &
        Anonymization" section under Section 10. Retention *period length* (how long after
        last interaction) isn't fixed yet — defaulting to 24 months, configurable, unless you
        say otherwise (see Section 7).
Impact: Data model (Section 10), DBA nightly job, audit trail design.
```

---

## 7. Open Questions

1. ~~**Localization**~~ — **RESOLVED**: staff UI supports **Dutch + English** (see NFR-08 below).
2. **Deployment target** — **explicitly deferred**: no infrastructure ADR exists yet for this project, and you've chosen to decide this with DevOps later rather than lock it in now. ADR-001 (Section 9) leaves the hosting platform open; every module is written host-agnostic (no provider-specific SDKs in Application/Domain layers) so this decision doesn't block backend/client development.
3. **MAUI vs. Blazor device split** — you said "decide once we see the screens." Still open — resolve at the UI Designer / wireframe step (Section 11, next steps).
4. ~~**AQ-01**~~ — **RESOLVED**: no Requested state, every booking is Confirmed on entry.
5. ~~**AQ-02**~~ — **RESOLVED (mechanism)**: partial anonymization via nightly job, see BR-10 / Section 10. **Still open**: the retention *period* itself — I've defaulted to **24 months since last interaction** (a common hospitality/GDPR-minimization baseline) as a configurable value, not a hardcoded one. Tell me if Coteng needs a different number.
6. ~~**Staff login model**~~ — **RESOLVED**: MAUI uses PIN entry, the website uses username/password. This has real security implications beyond "which screen shows which field" — see **ADR-001 Amendment 2** below, since a bare PIN is weak on its own and needs to be properly bound to a trusted device rather than treated as a full credential.

**NFR-08 — Localization**: Staff UI text is externalized via .NET resource files (`.resx`) / Blazor localization (`IStringLocalizer`), covering `nl-BE` and `en` at launch. Customer-entered free text (names, notes) is stored as-is, not translated. Date/time formatting follows Europe/Brussels conventions per locale.

## 8. Assumptions Made (flagging per your standing instructions)

- ~~Every staff member has an individual login~~ — **RESOLVED 2026-09-14**: confirmed, and specified further — MAUI uses PIN entry (bound to a paired device, not a bare credential), the website uses username/password. See ADR-001 Amendment 2.
- "Two-tier roles" = exactly two roles (Floor Staff, Manager) for v1; more granular roles (e.g., separate Admin) can be added later without a redesign since authorization is policy-based, not hardcoded.
- ~~No offline mode for the MAUI app~~ — **SUPERSEDED 2026-09-14**: you've confirmed offline availability of the live floor plan is a hard requirement. The MAUI app is now designed offline-first (local encrypted store + sync + human-reviewed conflict resolution) — see **ADR-002** below. This is real added complexity (roughly +6 weeks, Section 12) and introduces new security surface (PII cached on-device, offline auth) that I've flagged explicitly in ADR-002 rather than resolving silently.
- Single time zone (Europe/Brussels), no daylight-saving-transition edge cases handled specially beyond what .NET's `TimeZoneInfo` gives us by default.

---

## 9. ADR-001: Architecture & Technology Stack

**Date**: 2026-09-14
**Status**: PROPOSED
**Project**: Coteng Restaurant Reservations

### Context

Single-restaurant, staff-only reservation and live table-management system. Two front-ends requested: a MAUI mobile/tablet app and a Blazor website, with maximum code reuse. Backend must be a Modular Monolith with Clean Architecture (your explicit requirement). Multiple staff devices need to see near-real-time table status during service (NFR-03, NFR-04, FR-10).

### Decision

1. **Backend**: Single ASP.NET Core (.NET 10) host process implementing a **Modular Monolith**, where each module (Reservations, Tables, Staff/Identity, Scheduling) is internally structured with **Clean Architecture** (Domain → Application → Infrastructure), and modules communicate only through explicit `Contracts` projects — never by referencing another module's Domain or Infrastructure directly.
2. **Real-time sync**: A **SignalR hub** hosted in the same process, broadcasting table/reservation status changes to all connected clients (both Blazor Server and MAUI subscribe to it).
3. **UI reuse**: A shared **Razor Class Library (RCL)** — `UI.Components` — holds all Blazor components (reservation form, floor plan board, table cards, day timeline). Both front-ends render the same components.
4. **Website**: **Blazor Web App using Server interactivity**, hosted in the same ASP.NET Core process as the API. This lets the website's components call the Application layer in-process (no HTTP hop), while still getting SignalR-based live updates for consistency with the mobile client.
5. **Mobile**: **.NET MAUI Blazor Hybrid**, consuming the backend over a typed REST `HttpClient` + the same SignalR hub, rendering the shared RCL components inside `BlazorWebView`.
6. ~~**Database**: SQL Server~~ — **AMENDED, see Amendment 1 below: PostgreSQL**, accessed via EF Core (Npgsql provider), one schema per module (module-owned tables only) inside a single database.
7. **Auth**: ASP.NET Core Identity (cookie auth for Blazor Server, JWT bearer for the MAUI REST/SignalR calls), policy-based authorization with two policies to start: `FloorStaff` and `Manager` (Manager policy implies Floor Staff permissions).

### Rationale

- Modular Monolith + Clean Architecture per module gives you strict internal boundaries (so this can be split into services later if the business ever needs it) without the operational cost of real microservices for what is, today, a single small restaurant's internal tool — this is KISS: the simplest architecture that still won't paint you into a corner.
- Blazor Server for the website (rather than WASM) avoids building and maintaining a second public API surface just for the browser client, and gets you SignalR "for free" as its transport — appropriate since this is an internal tool on your network/cloud, not a public high-latency client.
- MAUI must talk over HTTP regardless (it's a separate process/device), so a typed REST API + SignalR client is the natural choice there, and it's the *same* API and hub the Blazor Server host calls into internally — one contract, two consumers.
- Sharing the RCL is what actually delivers on "reuse as much as possible" — the two apps differ in *hosting and transport*, not in UI code.

### Considered Alternatives

| Option | Pros | Cons | Reason rejected |
|---|---|---|---|
| Blazor WASM website | True client-side, works offline-ish | Needs a full public REST API from day one just for the browser; more auth complexity (token storage in browser) | Unnecessary for an internal staff tool; Blazor Server is simpler and sufficient |
| Microservices | Independent scaling/deploy | Massive operational overhead for a single restaurant | Wrong scale entirely — explicitly against your Modular Monolith requirement |
| No module boundaries (classic layered monolith) | Fastest to start | Boundaries erode fast, hard to reason about later | You explicitly asked for Modular Monolith + Clean Architecture |

### Consequences

**Positive**: Clear module ownership; easy to extend (e.g., add a Reporting module) without touching existing ones; one deployable artifact to operate; strong reuse between mobile and web.
**Negative**: Blazor Server ties the website's interactivity to a live SignalR connection to your server — if you later need the website usable over a very poor connection, that's a future ADR revision (switch that render mode to WASM or Auto).
**Risks**: If "reuse as much as possible" was meant to include public customer access later, revisit whether Blazor Server is still right before adding that (you'd likely want WASM or a separate customer-facing API surface at that point).

### Review Triggers

Revisit this ADR if: the restaurant becomes multi-location, or a public customer-facing booking flow gets added. (MAUI offline support was the third original trigger — it's since become a hard requirement; see ADR-002.)

### Amendment 1 (2026-09-14): Database — PostgreSQL instead of SQL Server

**Requested by**: Sam, questioning whether Decision #6 needed to be SQL Server given Clean Architecture keeps persistence in the Infrastructure layer.

**Answer to that question**: yes, largely — that's exactly what Clean Architecture buys you here. Domain and Application layers contain no SQL-Server-specific code (LINQ queries, not raw SQL), so this is legitimately an Infrastructure-layer swap: `UseNpgsql()` instead of `UseSqlServer()`, a different connection string, and EF Core migrations regenerated for the Npgsql provider (cheap right now, since nothing's built yet — this would be a much bigger deal to do *after* the first migration shipped to production). It's also a fully sanctioned choice, not a one-off exception: PostgreSQL is an approved per-project database alongside SQL Server in your organization's own architecture defaults.

**"Interchangeable" has two real, non-cosmetic exceptions worth knowing about, not just swapping a provider string:**

1. **Optimistic concurrency**: SQL Server's `rowversion` (Section 10's `RowVersion` columns on Table/Reservation) has no Postgres equivalent. The idiomatic Postgres approach is Npgsql's built-in `xmin` system column as the concurrency token (`.Property<uint>("xmin").IsRowVersion()` / `UseXminAsConcurrencyToken()`) — no extra stored column needed, EF Core just reads Postgres's own per-row version. I've updated Section 10 to reflect this.
2. **Case sensitivity**: SQL Server's default collation is case-insensitive, so `WHERE CustomerName = 'sam'` matches "Sam" for free. Postgres text comparison is case-sensitive by default. US-02 (search reservations by name) needs an explicit approach — `ILIKE`, a `citext` column, or a trigram index (`pg_trgm`) for performant partial-name search — flagged as a DBA implementation detail in Section 10, not a redesign.

**Non-functional upside worth naming**: Postgres's native `jsonb` is a better fit than SQL Server's JSON-as-text for the audit/conflict payload fields already in this schema (AuditLogEntry.Details, ConflictRecord.SubmittedPayloadJson) — genuinely nicer here, not just a lateral move.

**Consequence for Section 7's open hosting question**: PostgreSQL widens your hosting options beyond Azure SQL — managed choices include Neon (serverless Postgres — which you already operate for FamilySplit), Azure Database for PostgreSQL, AWS RDS for PostgreSQL, or self-hosted. Section 7 Q2 stays open per your instruction to decide with DevOps, but it's worth knowing Neon is now on the table if you want to reuse infrastructure you already run.

### Amendment 2 (2026-09-14): Dual Authentication Model — PIN (MAUI) vs Username/Password (Web)

**Requested by**: Sam — every staff member has an individual login; MAUI uses a PIN for fast access, the website uses username/password.

**Decision**: two layers, not one flat "PIN = password" swap, because a bare 4–6 digit PIN reachable from anywhere is trivially brute-forceable and would violate your own defense-in-depth and zero-trust principles if treated as a standalone credential:

1. **Website**: unchanged from ADR-001 — ASP.NET Core Identity, username + password, cookie auth.
2. **MAUI — device pairing (one-time, per physical tablet)**: a Manager authenticates with full username/password on a new tablet once. This creates a `DeviceRegistration` record (Section 10) binding that specific device (a GUID stored in MAUI `SecureStorage`) to the restaurant. **PIN login only ever works from a device with an active `DeviceRegistration` — never from an arbitrary client**, which is what makes a short PIN acceptable at all.
3. **MAUI — PIN login (per shift, per staff member, on an already-paired device)**: any staff member on that paired device signs in with their own PIN. The server checks both the device's registration is active *and* the PIN matches that staff member's `PinHash` (Argon2id, hashed separately from their website password — never shared or derived from it).

**Compensating controls** (this is exactly the kind of "business requirement forces an exception" case your standing instructions ask me to document explicitly, not smooth over):
- Rate limiting + lockout on PIN attempts, scoped per staff-member-per-device (e.g. 5 attempts, then require a website password reset of the PIN — never self-service unlock via more PIN guesses).
- A Manager can instantly revoke all PIN access from a specific tablet by deactivating its `DeviceRegistration` (`IsActive = false`) — this is the operational answer to RISK-06 (lost/stolen tablet, ADR-002): revoke the device, not just hope the local encryption holds.
- Only a Manager can pair a new device — a Floor Staff account can't mint new trusted devices (**BR-11**: `DeviceRegistration` creation requires the pairing user's `Role = Manager`, enforced server-side, not just hidden in the UI).
- PIN changes require the staff member's full website password, not just their current PIN — prevents a shoulder-surfed PIN from being silently rotated by an attacker to lock out the real owner.

**Consequence**: this is real, additional scope beyond "add a PIN field" — device pairing flow, a new entity, rate-limiting logic, and a revocation path. Sized in Section 12.

### Security Architecture (per your standing security requirements)

| Requirement | How it's met |
|---|---|
| Parameterized queries only | EF Core throughout; no raw SQL string concatenation anywhere; LINQ or parameterized `FromSqlInterpolated` only if raw SQL is ever unavoidable |
| Framework-native auth | ASP.NET Core Identity — no custom auth |
| Authorization on every request | Policy-based `[Authorize(Policy = ...)]` on every controller/endpoint and SignalR hub method; global fallback policy requires authentication by default (opt-out per endpoint, not opt-in) |
| Secrets in a secret manager | `dotnet user-secrets` locally; Azure Key Vault (or your platform's equivalent) in production — never in appsettings.json or source control |
| Approved cryptography | Identity's default Argon2id-class password hashing (ASP.NET Core Identity uses PBKDF2 by default — **flagging this**: if Argon2id specifically is a hard requirement, we'll need a custom `IPasswordHasher` implementation, which I'll call out explicitly when we build the Identity module rather than silently using the framework default) |
| Output encoding | Blazor encodes all rendered content by default (no `MarkupString` for user input) |
| Safe error handling | Global exception handler returns RFC 7807 Problem Details with generic messages; full details go to Serilog only |
| Rate limiting | `AddRateLimiter()` on the auth/login endpoint at minimum, and on the reservation-search endpoint given it's a lookup surface |
| No untrusted deserialization | All API DTOs are explicit, strongly-typed records — no dynamic/`object` binding |
| Security headers / cookie flags | HSTS, `Secure`, `HttpOnly`, `SameSite=Strict` on the Identity auth cookie (this is a session cookie for an internal staff tool — Strict is appropriate and compatible since there's no cross-site flow to support) |
| CSRF protection | Built-in ASP.NET Core antiforgery for the Blazor Server forms; MAUI's API calls use bearer tokens (not cookies), which are inherently not CSRF-vulnerable |
| Least privilege / fail closed | Authorization checks fail closed (deny by default); a table/reservation action always re-verifies the caller's role server-side, never trusts a client-sent role claim without validation against the current DB state |

**What you still need to configure in your environment**: the actual secret manager/Key Vault instance and its access policy; TLS certificates for whatever host you pick (Section 7, Q2); the Serilog sink destination (e.g., Seq, Application Insights) if you want centralized log search; CI enforcement of `TreatWarningsAsErrors`.

---

## 9b. ADR-002: Offline-First MAUI Sync Architecture

**Date**: 2026-09-14
**Status**: PROPOSED — supersedes the "fail closed on disconnect" assumption in ADR-001/Section 8
**Project**: Coteng Restaurant Reservations

### Context

You've stated that offline availability of the live floor plan on the MAUI host-stand device is a **hard requirement** — venue wifi cannot be assumed reliable, and staff must be able to keep booking and seating guests regardless. ADR-001 originally assumed connectivity loss should fail closed (reject the action) to avoid double-booking risk from stale data. That assumption is now replaced.

### Decision

1. The MAUI app maintains a **local encrypted SQLite database** (SQLCipher, AES-256) mirroring the current shift's floor plan (Tables, Sections) and reservations (today + near-term). This local store — not the network — is the MAUI UI's source of truth for rendering.
2. Writes (create/edit reservation, change table status) apply to the local store immediately (**optimistic UI**) and are appended to a local **Outbox**: a queue of pending operations, each with a client-generated `OperationId` (GUID) for idempotent replay and a `ClientTimestamp`.
3. Reservation/Table IDs are **client-generated GUIDs at creation time** (already true in Section 10's schema), so offline-created records never collide with anything else.
4. A background sync process — triggered by `IConnectivity.ConnectivityChanged` plus a periodic timer — drains the Outbox to the backend when online, in order, per entity.
5. **The backend remains authoritative.** Each synced operation is re-validated against current server state exactly as if it had been submitted online (BR-01 double-booking, BR-07 capacity, etc.). If it now conflicts with something another device did while both were offline, it is marked **Conflict** — never silently applied, never silently dropped — and surfaced in a Manager-facing resolution queue (BR-08, US-06).
6. On reconnect, after the Outbox drains, the app pulls a refresh of the shift's floor plan to reconcile changes from other devices, then resumes the SignalR connection for live updates.

### Rationale

This is the standard "local-first, server-authoritative reconciliation" pattern. It makes the floor plan always usable at the cost of a genuinely new conflict-resolution UX. I deliberately rejected automatic conflict resolution (e.g. last-write-wins): silently picking a winner between two real customer bookings is exactly the kind of "try to fix bad input instead of rejecting it" shortcut your security principles rule out. A small, human-reviewed conflict queue is proportionate for a single restaurant's table count.

### Considered Alternatives

| Option | Pros | Cons | Reason rejected |
|---|---|---|---|
| Fail closed on disconnect (original ADR-001 assumption) | Simple, no conflict UX needed | Floor plan unusable exactly when wifi is worst — the scenario you need it most | Directly contradicts your hard requirement |
| Last-write-wins auto-merge | No conflict UI needed | Can silently overwrite a real booking with no human check — unacceptable for a business-critical record and against your "never try to fix bad input" principle | Rejected on security/correctness grounds |
| CRDT-based real-time multi-device merge | No conflicts ever, fully automatic | Significant engineering complexity, overkill for a handful of devices in one restaurant | Disproportionate to project scale — revisit only if conflict volume proves painful in practice |

### Consequences

**Positive**: floor plan is always usable, no single point of failure from wifi; audit trail is preserved even for offline actions (tied to the authenticated staff member locally).
**Negative**: new conflict-resolution UX and Manager workflow; new local-data security surface (see below); larger test matrix (partition, reconnect, replay, multi-device conflict scenarios).
**Risks**: if two devices go offline simultaneously and double-book the same table, one booking **will** land in the conflict queue for a human to sort out — this is inherent to offline-first, not a bug, and staff need to understand it can happen.

### Security Consequences — Explicit Exception & Mitigation (per your standing security requirements)

Your rules require documenting any exception explicitly and proposing the safest alternative, rather than quietly working around it. Two exceptions are unavoidable here:

- **"Verify on every request" is impossible while offline, by definition.** Mitigation: offline actions are *provisional*, not authoritative — server-side revalidation at sync time (Decision #5) is the real enforcement point. Locally, staff must still authenticate (see below), and every offline action is tied to that authenticated staff member and timestamped for the audit trail (NFR-02) even before it syncs.
- **A JWT/session can't be validated against the server while offline.** Proposed default: cache a token in MAUI `SecureStorage` (Keychain/Keystore-backed — never plain preferences) with a bounded **offline grace period**, defaulting to **12 hours** (covers one shift), after which the app requires reconnecting and re-authenticating — even the PIN-based quick re-entry described in **ADR-001 Amendment 2** is bounded by this window, not a permanent bypass of it. **This is a real security/availability tradeoff and I'm flagging it rather than choosing silently** — tell me if 12 hours doesn't match how your shifts run.

New asset introduced: the local SQLite database now holds customer PII (name, phone) and floor-plan data on a device that can be lost or stolen. Mitigations: SQLCipher encryption at rest (AES-256, consistent with your approved-crypto list); if Coteng has an MDM solution for these tablets, enable remote wipe — that's an environment-level control I can't configure from here, so I'm naming it rather than assuming it exists.

### Review Triggers

Revisit if conflict frequency in practice is high (wifi worse than expected) — at that point a local-network peer-to-peer sync between devices becomes worth the added complexity; today it isn't.

---

## 10. Data Model (System Designer)

### Entity: Table

| Field | Type | Nullable | Notes |
|---|---|---|---|
| Id | uuid | NO | PK |
| SectionId | uuid | NO | FK → Section |
| Name | varchar(50) | NO | e.g. "T12" |
| MinCapacity / MaxCapacity | int | NO | |
| IsCombinable | boolean | NO | must be `true` for a table to be added to a `TableGroup` (below) |
| PositionX / PositionY | double precision | YES | floor plan layout coordinates |
| Status | enum (text) | NO | Available / Reserved / Occupied / NeedsCleaning / Blocked |
| IsActive | boolean | NO | soft-deactivate instead of hard delete (preserves reservation history) |
| *(concurrency)* | `xmin` system column | — | Postgres's built-in row version, used via Npgsql `UseXminAsConcurrencyToken()` — no stored column needed (see ADR-001 Amendment 1) |

### Entity: TableGroup (added by CLAUDE.md ADR-006 / docs/plan.md, Phase 6)

Resolves BR-07's "or an explicitly combinable set of tables": `IsCombinable` alone is a boolean,
not enough to say *which* tables combine with which. The charter's own `Reservation.TableId`
stays a single nullable FK (no multi-table-per-reservation support) — a reservation is still
assigned to one physical table; if that table belongs to an active `TableGroup`, BR-07's
capacity check uses the group's summed capacity instead of the one table's own.

| Field | Type | Nullable | Notes |
|---|---|---|---|
| Id | uuid | NO | PK |
| Name | varchar(50) | NO | e.g. "T1+T2" |
| TableIds | uuid[] | NO | native Postgres array, not a join table (ADR-006) — every member must have `IsCombinable = true`, be active, and belong to no other active group |
| IsActive | boolean | NO | Create-only in Phase 6 — no Update/Deactivate endpoint yet |

### Entity: Section

| Field | Type | Nullable | Notes |
|---|---|---|---|
| Id | uuid | NO | PK |
| Name | varchar(50) | NO | e.g. "Patio", "Main Room" |
| DisplayOrder | int | NO | |
| IsActive | boolean | NO | soft-deactivate, same pattern as `Table.IsActive` — added by charter correction #12 (plan.md); FR-07 requires deactivating sections but the original data model omitted the column |

### Entity: Reservation

| Field | Type | Nullable | Notes |
|---|---|---|---|
| Id | uuid | NO | PK |
| CustomerName | varchar(100) | NO | |
| CustomerPhone | varchar(20) | NO | validated format at the API boundary |
| CustomerEmail | varchar(200) | YES | |
| PartySize | int | NO | > 0 |
| ReservationDateTime | timestamptz | NO | stored UTC; Postgres requires explicit `DateTime.Kind = Utc` from EF Core (Npgsql is strict about this, unlike SQL Server) |
| DurationMinutes | int | NO | default turn time, e.g. 90 |
| Status | enum (text) | NO | Confirmed / Seated / Completed / NoShow / Cancelled — always starts at Confirmed (AQ-01, resolved) |
| TableId | uuid | YES | assigned on/after creation |
| Notes | varchar(500) | YES | allergies, occasion, VIP flag, seating preference |
| CreatedByStaffId | uuid | NO | FK → StaffUser |
| CreatedAt / UpdatedAt | timestamptz | NO | |
| LastInteractionDate | timestamptz | NO | bumped only by real staff actions on this reservation (create/edit/status change) — deliberately *not* touched by system/background writes, so the retention clock only resets on genuine interaction |
| RetentionExpiryDate | timestamptz | NO | computed = LastInteractionDate + retention period (Section 7 Q5 — defaulting to 24 months, configurable, not hardcoded) |
| IsAnonymized | boolean | NO | default false; set true by the nightly retention job (see "Data Retention & Anonymization" below) |
| *(concurrency)* | `xmin` system column | — | see Table entity note above |

### Entity: StaffUser

| Field | Type | Nullable | Notes |
|---|---|---|---|
| Id | uuid | NO | PK, 1:1 with ASP.NET Identity user |
| FullName | varchar(100) | NO | |
| Role | enum (text) | NO | FloorStaff / Manager |
| IsActive | boolean | NO | |
| PinHash | varchar(200) | YES | Argon2id hash of the staff member's MAUI PIN — separate from their website password hash; null until they set a PIN (ADR-001 Amendment 2) |

### Entity: DeviceRegistration (added by ADR-001 Amendment 2)

| Field | Type | Nullable | Notes |
|---|---|---|---|
| Id | uuid | NO | PK |
| DeviceId | uuid | NO | client-generated, stored in MAUI `SecureStorage`, unique per physical tablet |
| PairedByStaffId | uuid | NO | FK → StaffUser — must be a Manager (BR-11) |
| PairedAt | timestamptz | NO | |
| IsActive | boolean | NO | a Manager can deactivate this to instantly revoke PIN login from a lost/stolen tablet |
| LastSeenAt | timestamptz | YES | updated on each successful PIN login — helps spot a device that's gone quiet |

### Entity: ServicePeriod

| Field | Type | Nullable | Notes |
|---|---|---|---|
| Id | uuid | NO | PK |
| Date | date | NO | |
| Label | varchar(30) | NO | "Lunch", "Dinner" |
| StartTime / EndTime | time | NO | |
| IsClosed | boolean | NO | |

### Entity: AuditLogEntry

| Field | Type | Nullable | Notes |
|---|---|---|---|
| Id | uuid | NO | PK |
| EntityType | varchar(50) | NO | "Reservation" / "Table" |
| EntityId | uuid | NO | |
| Action | varchar(50) | NO | Created / Updated / Cancelled / StatusChanged / Anonymized |
| PerformedByStaffId | uuid | **YES** | FK → StaffUser — **nullable, changed for the retention job below**: a system process has no StaffUser |
| PerformedBySystemProcess | varchar(50) | YES | e.g. "RetentionAnonymizationJob" — exactly one of this or PerformedByStaffId is set, never both, never neither |
| Timestamp | timestamptz | NO | |
| Details | jsonb | YES | JSON diff of changed fields — native Postgres type, nicer fit than SQL Server's JSON-as-text |

### Entity: ConflictRecord (server-side, added by ADR-002)

| Field | Type | Nullable | Notes |
|---|---|---|---|
| Id | uuid | NO | PK |
| EntityType | varchar(50) | NO | "Reservation" / "Table" |
| EntityId | uuid | NO | |
| OperationId | uuid | NO | the Outbox operation that failed revalidation |
| SubmittedPayloadJson | jsonb | NO | what the offline device tried to apply |
| ConflictReason | varchar(200) | NO | e.g. "Table already booked for overlapping time" |
| DetectedAt | timestamptz | NO | |
| ResolvedByStaffId | uuid | YES | FK → StaffUser, set on resolution |
| ResolvedAt | timestamptz | YES | |
| Resolution | enum | YES | Accepted / Rejected / Reassigned |

### Entity: OutboxOperation (MAUI-local SQLite only — not part of the server schema)

| Field | Type | Notes |
|---|---|---|
| OperationId | GUID | client-generated, primary key, used for idempotent replay |
| EntityType / EntityId | text | what this operation targets |
| OperationType | text | Create / Update / StatusChange / Cancel |
| PayloadJson | text | the change to apply |
| ClientTimestamp | datetime | when the staff member performed the action, offline or not |
| SyncStatus | text | Pending / Synced / Conflict |
| CreatedByStaffId | GUID | locally authenticated staff member |

### Data Retention & Anonymization (added 2026-09-14, per your instruction)

**BR-10**: A Reservation whose `RetentionExpiryDate` has passed and which is not yet `IsAnonymized` has its contact fields partially anonymized by a nightly job — `CustomerName` is kept, `CustomerPhone` and `CustomerEmail` are set to `NULL`, and `IsAnonymized` is set `true`. This is per-reservation, not per-customer: if the same person books again later, that *new* reservation has its own fresh contact fields and its own retention clock — an active regular customer's recent bookings are never affected by an old, unrelated booking aging out.

**Nightly job** (DBA-owned; Postgres — `pg_cron` or a Hangfire recurring job, whichever fits the DevOps platform once Section 7 Q2 is decided):

```sql
BEGIN;

UPDATE reservations.reservation
SET customer_phone = NULL,
    customer_email = NULL,
    is_anonymized = TRUE,
    updated_at = now()
WHERE retention_expiry_date < now()
  AND is_anonymized = FALSE;

-- Every anonymized row gets a system-attributed audit entry — never silent
INSERT INTO shared.audit_log_entry
  (id, entity_type, entity_id, action, performed_by_system_process, "timestamp")
SELECT gen_random_uuid(), 'Reservation', id, 'Anonymized', 'RetentionAnonymizationJob', now()
FROM reservations.reservation
WHERE retention_expiry_date < now()
  AND is_anonymized = FALSE;  -- evaluated before the UPDATE above commits, in the same transaction

COMMIT;
```

Run inside a single transaction so a partial failure never leaves rows anonymized without a matching audit entry, or vice versa (fail closed, not partially applied).

**Open parameter**: the retention period itself (currently defaulted to 24 months since `LastInteractionDate`, stored as a configurable setting, not hardcoded — see Section 7 Q5). `RetentionExpiryDate` is recalculated by the application whenever `LastInteractionDate` changes, so a change to the configured period only affects future recalculations, not retroactively re-dating already-anonymized rows.

### Relationships

```
[Section]    1──────* [Table]
[TableGroup] *──────* [Table]      (Table.Id ∈ TableGroup.TableIds — a native array column, not a join table; added Phase 6/ADR-006)
[Table]      1──────* [Reservation]   (nullable FK — a reservation may be unassigned initially)
[StaffUser] 1────* [Reservation]   (CreatedBy)
[StaffUser] 1────* [AuditLogEntry] (PerformedBy, nullable — system processes populate PerformedBySystemProcess instead)
[StaffUser] 1────* [ConflictRecord] (ResolvedBy, nullable until resolved)
[StaffUser] 1────* [DeviceRegistration] (PairedBy)
```

### Key Indexes

- `Reservation.ReservationDateTime` — day/time range queries (the day list, conflict checks)
- `Reservation.TableId` — table history lookups
- `Reservation.CustomerPhone` — customer search (US-02)
- `Reservation.CustomerName` — a **trigram index** (`CREATE EXTENSION pg_trgm`) rather than a plain B-tree, so partial/case-insensitive name search (US-02) stays fast — Postgres text comparison is case-sensitive by default, unlike SQL Server's default collation (see ADR-001 Amendment 1)
- `Table.SectionId`, `Table.Status` — floor plan rendering
- Unique constraint: `(SectionId, Name)` on `Table`

### Module Map

```
                    ┌─────────────────────────────────────────┐
                    │         ASP.NET Core Host Process         │
                    │  (REST API + Blazor Server + SignalR Hub) │
                    └───────────────────┬───────────────────────┘
                                        │ in-proc calls via each module's Contracts
        ┌────────────────┬─────────────┼─────────────────┬────────────────┐
        ▼                ▼                                ▼                ▼
┌───────────────┐ ┌───────────────┐               ┌───────────────┐ ┌───────────────┐
│ Reservations   │ │ Tables         │               │ StaffIdentity  │ │ Scheduling     │
│ (Domain/App/   │ │ (Domain/App/   │◀── domain ───▶│ (Domain/App/   │ │ (Domain/App/   │
│  Infra/Contracts)│ Infra/Contracts)│   events      │  Infra/Contracts)│ Infra/Contracts)│
└───────┬────────┘ └───────┬────────┘               └───────┬────────┘ └───────┬────────┘
        │                  │                                │                  │
        └──────────────────┴──────────── EF Core / PostgreSQL (module-owned schemas) ───┘

        MAUI Blazor Hybrid  ──HTTP (REST) + SignalR──▶  Host Process  ◀── in-proc ── Blazor Server (same Host)
              │                                                                          │
              └──────────────────── shared UI.Components (Razor Class Library) ──────────┘
```

Cross-module coordination example: when a Reservation is marked **Seated**, the Reservations module raises a domain event; the Tables module's handler (subscribed via the in-process mediator, e.g. MediatR) sets the table's status to Occupied. Neither module references the other's Domain/Infrastructure directly — only through published events and each other's `Contracts` interfaces.

### Sample API Contracts

```
POST /api/reservations
Auth: Bearer (FloorStaff or Manager)
Body: { customerName, customerPhone, customerEmail?, partySize, reservationDateTime, durationMinutes?, notes? }
201 → { id, status: "Confirmed", ... }
400 → validation errors (e.g. partySize <= 0)
409 → if explicitly assigned a table that's already booked for an overlapping time (BR-01)

PATCH /api/reservations/{id}/seat
Auth: Bearer (FloorStaff or Manager)
Body: { tableId }
200 → updated reservation, status: "Seated"; publishes TableOccupied event over SignalR
400 → if tableId capacity < partySize and table isn't part of a combinable group (BR-07)
404 → reservation not found

GET /api/tables/floor-plan?sectionId=
Auth: Bearer (FloorStaff or Manager)
200 → [{ tableId, name, sectionId, capacity, status, positionX, positionY, currentReservationId? }]

PATCH /api/tables/{id}/status
Auth: Bearer (FloorStaff or Manager)
Body: { status }
200 → updated table; broadcasts TableStatusChanged over SignalR to all connected clients

SignalR Hub: /hubs/floorplan
Server → Client events: TableStatusChanged, ReservationCreated, ReservationUpdated, ReservationCancelled
```

---

## 12. Technical Feasibility & Effort Estimate (Technical Analyst)

### Verdict: FEASIBLE WITH CHANGES

### Summary

Everything in Section 5/6 is buildable with the stack in ADR-001 and your existing GitLab CI/CD + Docker + Nexus pipeline (no new CI infrastructure needed). Two design points need to be pinned down *before* the RCL/API build starts, or they'll cause rework — see "What complicates it" below.

### What makes it feasible

- Every requirement maps to well-understood ASP.NET Core / EF Core / Blazor / MAUI patterns — nothing here is R&D.
- Your existing GitLab CI/Docker/Nexus setup can host this pipeline without new tooling decisions.
- Single restaurant + two roles keeps the domain small; no complex permission matrix.

### What complicates it

- **RCL components need a transport-agnostic abstraction.** ADR-001 has Blazor Server calling the Application layer in-process while MAUI calls the same logic over HTTP. If the shared RCL components call services directly, they can't run unmodified in both hosts. **Recommended fix**: components depend on small client interfaces (e.g. `IReservationsClient`, `ITablesClient`) defined in a shared `UI.Abstractions` project; the Blazor Server host registers an in-process implementation, the MAUI host registers an HTTP+SignalR implementation. This is a small addition to ADR-001, not a rework — cheapest to bake in now.
- **Cross-module consistency on "mark Seated."** Reservations and Tables are separate modules communicating via domain events (ADR-001). Marking a reservation Seated updates the Table status via an event handler, not the same transaction — there's a small window (milliseconds to low seconds) where they're inconsistent. Acceptable for this use case, but it's a real simplification worth your explicit sign-off rather than silent acceptance (see RISK-01).
- **Argon2id vs. ASP.NET Core Identity's default hasher** — flagged already in ADR-001; a custom `IPasswordHasher<T>` is a small (~0.5 day) addition if Argon2id is a hard requirement rather than "approved crypto in general."
- **PostgreSQL swap (ADR-001 Amendment 1)**: mechanically small since nothing's built yet, but two implementation details need the DBA's attention when the schema is actually created — the `xmin`-based concurrency token (replacing SQL Server's `rowversion`) and a trigram/`ILIKE` strategy for case-insensitive name search. Folded into the existing schema estimate below, not additional scope.
- **PIN + device pairing (ADR-001 Amendment 2)**: this is genuinely new scope, not a UI-only change — a `DeviceRegistration` entity, a pairing flow, PIN hashing/rate-limiting, and a revocation path all need to exist before MAUI login can work at all. Sized as its own line below.

### Pre-conditions / Blockers

- None hard-blocking. Section 7 Q2 (hosting) can stay open without blocking backend/client development, since ADR-001 keeps modules host-agnostic.

### Effort Estimate

| Component | Estimate | Confidence |
|---|---|---|
| Solution scaffold, SharedKernel, DI/module wiring | 3–4 days | High |
| StaffIdentity module (Identity, 2 roles, policies) | 3 days | High |
| PIN + device pairing (DeviceRegistration, pairing flow, PIN hashing/rate-limit/revocation — ADR-001 Amendment 2) | 4 days | Medium |
| Tables & Sections module | 4 days | High |
| Reservations module (incl. conflict/capacity rules BR-01/BR-07) | 6 days | Medium |
| Scheduling module (service periods) | 2 days | High |
| SignalR hub + cross-module event wiring | 3 days | Medium |
| Audit logging (cross-cutting) | 2–3 days | High |
| EF Core schema + migrations (all modules) | 3 days | High |
| Shared RCL UI components (reservation form, floor plan board, table card, day timeline) | 7–8 days | Medium |
| Blazor Server host wiring | 2 days | High |
| MAUI Blazor Hybrid app (client interfaces, HTTP+SignalR client, packaging) | 6–7 days | Medium |
| Localization (nl-BE/en wiring; translation content is a non-dev dependency) | 2–3 days dev + translation time | Medium |
| Automated tests (xUnit per module, bUnit for RCL, integration tests for conflict rules) | 5–6 days | Medium |
| CI/CD pipeline (reusing existing GitLab CI/Docker/Nexus) | 2 days | High |
| Retention/anonymization nightly job + configurable retention-period setting (BR-10) | 1–2 days | High |
| **Subtotal — online-only design** | **~55–61 days (~11–12 weeks)** | |
| Local encrypted SQLite store + repository layer in MAUI (ADR-002) | 5–6 days | Medium |
| Outbox pattern + sync engine (queue, retry, idempotent replay, delta reconciliation) | 7–8 days | Low |
| Server-side sync endpoint(s) + revalidation + conflict detection | 4–5 days | Medium |
| Conflict resolution UI (Manager conflict queue: accept/reject/reassign) | 4 days | Medium |
| Local DB encryption (SQLCipher) + secure token caching with offline grace period | 3 days | Medium |
| Additional testing: offline/reconnect/multi-device conflict scenarios | 5–6 days | Low |
| **Subtotal — offline-first addition (ADR-002)** | **~28–32 days (~6 weeks)** | |
| **Total (dev effort, single engineer, sequential)** | **~83–93 days (~17–19 weeks)** | |

**With Backend Dev + Client Dev working in parallel**, calendar time compresses to roughly **10–13 weeks** — up from the original 6–8. Offline sync is still the single biggest addition to both effort and risk in this project; PIN/device-pairing and the retention job are smaller but real additions on top. I'm surfacing the full jump rather than folding it in quietly, since each round of decisions has changed the shape of the timeline, not just its length. Hosting/deployment (Section 7 Q2) is still unestimated until a platform is chosen.

**Confidence note on the offline addition**: "Low" confidence on the sync engine and testing lines reflects genuine unknowns — exact conflict frequency in your venue's actual wifi conditions, and edge cases in idempotent replay, won't be fully known until the spike below runs and early real-world use accumulates.

### Dependency Graph

```
[Solution scaffold + SharedKernel]
  └── Unblocks: all modules

[StaffIdentity module]
  └── Requires: Solution scaffold
  └── Unblocks: authorization on every other module's endpoints

[Tables module] ── parallel-safe with ── [Scheduling module]
  └── Requires: Solution scaffold, StaffIdentity (for Manager-only endpoints)

[Reservations module]
  └── Requires: Tables module (table capacity/status lookups), StaffIdentity
  └── Unblocks: SignalR hub's reservation events

[SignalR hub]
  └── Requires: Reservations + Tables modules (events to broadcast)
  └── Unblocks: real-time UI in RCL components

[Shared RCL UI.Components + UI.Abstractions]
  └── Requires: API contracts frozen (Section 10) — can start against mocked clients
      before backend modules are fully done
  └── Unblocks: Blazor Server host, MAUI app

[Blazor Server host] ── parallel-safe with ── [MAUI Blazor Hybrid app]
  └── Both require: RCL components, SignalR hub, StaffIdentity auth wired

[MAUI local store + Outbox + sync engine] (ADR-002)
  └── Requires: MAUI Blazor Hybrid app skeleton, API contracts frozen (Section 10)
  └── Should start with Spike 1 (Technical Analyst) before full build
  └── Unblocks: US-06 (Conflict resolution UI), US-07 (offline booking/seating)

[Conflict resolution UI + server ConflictRecord endpoints]
  └── Requires: MAUI sync engine, server-side revalidation logic
  └── Parallel-safe with: Blazor Server desktop work once its own scope is done
```

### Technical Risk Register

```
RISK-01: Eventual consistency between Reservations and Tables modules
Probability: Medium | Impact: Low | Category: Data
Description: Domain-event handoff between modules means Table.Status can lag
  Reservation.Status by a short window during "mark Seated."
Mitigation: Acceptable given NFR-04's ~1-2s latency target; make the RCL floor
  plan optimistically update the table tile immediately on the action that
  triggered it, reconciled by the SignalR event when it arrives.
Owner: Architect / Backend Dev
Status: ACCEPTED (pending your sign-off)

RISK-02: SUPERSEDED by ADR-002 — offline is now first-class, not a degraded state.
  Replaced by RISK-05 through RISK-07 below.

RISK-05: Simultaneous offline double-booking produces unavoidable conflicts
Probability: Medium | Impact: Medium | Category: Data / Process
Description: If two devices are offline at once and both book the same table
  for overlapping times, one WILL end up in the Manager conflict queue
  (ADR-002, BR-08) — this is inherent to offline-first design, not a bug.
Mitigation: Make the conflict queue impossible to miss (badge/notification),
  and train staff that this can happen. Track conflict frequency after launch
  to decide if ADR-002's "Review Triggers" threshold is hit.
Owner: Product Manager (staff process) / Client Dev (queue visibility)
Status: ACCEPTED — inherent to the chosen design, not mitigable to zero

RISK-06: Local device data security (new attack surface from ADR-002)
Probability: Low | Impact: High | Category: Security
Description: The MAUI tablet now holds an encrypted local copy of customer PII
  and today's floor plan. A lost/stolen device is a higher-impact event than
  before offline support existed.
Mitigation: SQLCipher encryption at rest (AES-256); recommend Coteng enable
  MDM/remote-wipe on host-stand tablets if available — this is an
  environment-level control outside what I can configure from here.
Owner: DevOps / Sam (device management policy)
Status: OPEN — needs a decision on whether Coteng has or will get MDM for these tablets

RISK-07: Sync engine correctness (Outbox idempotent replay, ordering)
Probability: Medium | Impact: Medium | Category: Data integrity
Description: Bugs in the Outbox/replay logic are the kind that surface rarely
  but badly (e.g. a double-applied operation, or a lost one after an app crash
  mid-sync). This is genuinely the highest-risk new component in the project.
Mitigation: Dedicated integration test suite for crash-mid-sync, out-of-order
  delivery, and duplicate-operation scenarios before this ships; see spike below.
Owner: Backend Dev / Client Dev (MAUI)
Status: OPEN

RISK-03: Shared RCL components coupling to a single transport
Probability: Medium (if not addressed now) | Impact: Medium | Category: Architecture
Description: Without the IReservationsClient/ITablesClient abstraction above,
  the reuse promise of the RCL breaks and one of the two hosts needs
  component forks.
Mitigation: Bake the abstraction into ADR-001 before UI build starts (cheap now,
  expensive later).
Owner: Architect
Status: OPEN — recommend resolving before Client Dev starts on the RCL

RISK-04: Translation content availability (nl-BE)
Probability: Low | Impact: Low | Category: Non-technical dependency
Description: Dev work for localization is small, but accurate Dutch UI copy
  needs a native speaker's review — this can slip independently of engineering.
Mitigation: Draft nl-BE strings early and get them reviewed in parallel with
  backend/API work, not gated behind it.
Owner: Product Manager / Sam
Status: OPEN

RISK-08: Team unfamiliarity with PostgreSQL specifics (ADR-001 Amendment 1)
Probability: Low | Impact: Low | Category: Third-party / tooling
Description: Your org's primary DB has been SQL Server; Postgres-specific
  behaviour (xmin concurrency, case-sensitive text, timestamptz strictness)
  is new surface even though EF Core hides most of it.
Mitigation: The two concrete gotchas are already called out in Section 10 and
  ADR-001 Amendment 1 rather than left implicit; low effort to close.
Owner: DBA / Backend Dev
Status: OPEN — low concern, noted for completeness

RISK-09: PIN brute-force if device binding is implemented loosely
Probability: Low (if built per ADR-001 Amendment 2) / High (if a shortcut is taken)
Impact: High | Category: Security
Description: A 4-6 digit PIN is only acceptable because it's bound to a
  specifically paired, revocable device. If a future change (e.g. "just let
  PIN login work from the API directly for convenience") loosens that binding,
  the PIN becomes a weak standalone credential guessable in minutes.
Mitigation: Enforce the DeviceRegistration check server-side on every PIN
  login attempt, not just at pairing time; rate-limit per staff-per-device;
  treat any request to relax this as a security review trigger, not a quick fix.
Owner: Architect / Backend Dev
Status: OPEN — design is sound as specified; risk is in future erosion of the control
```

### Spike Recommendations

```
Spike 1: Prove the Outbox/sync engine end-to-end on a throwaway entity
  before building it into Reservations/Tables for real.
Timebox: 2 days
Done when: A prototype demonstrates create-while-offline → app killed mid-sync →
  reopened → reconnect → exactly-once apply on the server, plus a forced
  two-device conflict that correctly lands in a conflict list instead of
  silently resolving.
Output: Decision note confirming the Outbox schema, retry/backoff policy, and
  conflict-detection approach to use for real (feeds ADR-002).
```

```
Spike 2: Validate SignalR client reconnect/backoff behaviour in MAUI BlazorWebView
  under intermittent wifi, and confirm the local-store-first rendering approach
  (UI never blocks on network) feels right on a real tablet.
Timebox: 1 day
Done when: A small MAUI prototype demonstrates connect → drop → local-only
  operation → reconnect → reconciliation, with a UX pattern for
  Synced/Pending/Conflict states (BR-09).
Output: Short decision note confirming SignalR client configuration
  (reconnect intervals, timeout) and the sync-state UI treatment.
```

---

## 13. Next Steps

1. You confirm/adjust the remaining **Open Question** in Section 7 (deployment target — currently deferred to DevOps), sign off on RISK-01's acceptance and RISK-03's fix in ADR-001, and **confirm or correct the 12-hour offline auth grace period proposed in ADR-002** — that's the one open security/availability tradeoff I picked a default for rather than leaving blank.
2. Run **Spike 1** (Outbox/sync engine proof) early — it's the highest-uncertainty, highest-impact piece of the whole project, and everything else in EPIC 4 depends on what it confirms.
3. **UI Designer** produces wireframes for the reservation-entry flow and the live floor plan (host-stand/tablet and desktop layouts, plus Synced/Pending/Conflict visual states) — also where we settle the MAUI-vs-Blazor device split.
4. Scaffold the actual .NET solution (modules, RCL, MAUI project incl. local store/Outbox, Blazor host) per ADR-001 + ADR-002 — **Backend Dev**, **Client Dev (MAUI + Blazor)**, and **DBA** (EF Core migrations for the schema in Section 10, including RowVersion/ConflictRecord) build in parallel from this charter.
5. **Testing** turns the acceptance criteria in Section 6 into the automated test suite (offline/conflict scenarios included); **DevOps** stands up CI/CD (reusing your existing GitLab CI/Docker/Nexus setup) and the environment once Section 7 Q2 is answered.
