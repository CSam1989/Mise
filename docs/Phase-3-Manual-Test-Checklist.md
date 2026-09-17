# Phase 3 Manual Test Checklist — StaffIdentity: Real Sign-In, Roles, Policies

Companion to the automated suites, not a replacement for them. Everything below is **already
proven automatically** (99 tests: 18 architecture, 46 unit, 4 bUnit, 27 integration, 4 E2E — all
green, plus the tiered coverage gate at 99.4% on `*.Domain`/`*.Application` and the format check).
This checklist is for *you* to look at the actual code and click through the actual app before
trusting that.

**Update:** while reviewing this phase, a real gap was flagged — logging existed, but nothing
caught an *unexpected* failure (API down, a bug, a corrupted invariant) and showed the user
something short of a crash or a leaked stack trace. Section 2 now has a dedicated "Error
handling" review, and section 3 has a manual step to actually see it happen. See CLAUDE.md's
new "Error handling" section for the full project-wide standard this established.

Check a box, or write a one-line note next to it if something looks off. Anything you flag, bring
back to the next session with the file/line and what you expected instead.

---

## 0. One-time setup

- [ ] `dotnet --version` prints a `10.0.x` version (not an `11.x` preview).
- [ ] Docker Desktop (or equivalent) is running.
- [ ] `dotnet tool restore` has been run once in the repo root.
- [ ] `dotnet user-secrets set Parameters:jwt-signing-key <any-random-string> --project src/Mise.AppHost`
      — unchanged from Phase 2, still required.
- [ ] **New this phase:** `dotnet user-secrets set Parameters:seed-manager-password
      <any-random-string> --project src/Mise.AppHost` — `Mise.MigrationService` fails fast with a
      clear message if this is missing, the same way it already does for the signing key.
      (`Parameters:seed-manager-username` is optional; it defaults to `manager`.)
- [ ] Playwright browsers installed: `tests/Mise.E2ETests/bin/Release/net10.0/playwright.ps1
      install --with-deps chromium`.

## 1. Re-run the automated suites yourself

```powershell
./scripts/test.ps1        # build + all five suites + format check
./scripts/coverage.ps1     # tiered coverage gate (90% on *.Domain/*.Application)
```

- [ ] All five suites report green (95 tests total).
- [ ] `dotnet format Mise.slnx --verify-no-changes` reports no changes.
- [ ] Coverage gate reports **pass**. `Mise.Modules.StaffIdentity.Domain` and
      `.Application` should show 100% — the report prints per-assembly numbers.

If any of these are *not* green on your machine but I reported them green above, that is the
single most important discrepancy to flag — it usually means something environment-specific (a
stale `bin`/`obj`, a port conflict, Docker not actually running).

---

## 2. Code review — walking the layers

Not exhaustive line-by-line — a guided tour hitting the decisions most worth a second opinion.
File paths are relative to the repo root.

### A real architectural decision this phase had to make

- [ ] Read **CLAUDE.md's "Cross-cutting infrastructure" section** first — it explains *why*
      `shared.processed_operation`/`shared.audit_log_entry` moved out of Reservations'
      Infrastructure project into a brand-new
      [`src/Mise.SharedKernel.Persistence`](../src/Mise.SharedKernel.Persistence) project, rather
      than either (a) duplicating the mapping into StaffIdentity's own Infrastructure, or (b)
      putting it in the existing `Mise.SharedKernel.Infrastructure`. The constraint that forced
      this: `Mise.SharedKernel.Infrastructure` is referenced directly by every module's
      `Application` project (for `IAuditWriter`) and **must stay EF-free forever**, or EF Core
      would leak transitively onto every `Application` project — exactly what the architecture
      tests forbid. Does this reasoning hold up, or would you have made a different call?
- [ ] [`SharedKernelModelBuilderExtensions.cs`](../src/Mise.SharedKernel.Persistence/SharedKernelModelBuilderExtensions.cs)
      and the `isOwner` parameter on
      [`ProcessedOperationConfiguration.cs`](../src/Mise.SharedKernel.Persistence/Configurations/ProcessedOperationConfiguration.cs) /
      [`AuditLogEntryConfiguration.cs`](../src/Mise.SharedKernel.Persistence/Configurations/AuditLogEntryConfiguration.cs) —
      exactly one module (Reservations) owns the `CreateTable` DDL for these two physical
      tables; every other module (StaffIdentity) maps the same tables with
      `ExcludeFromMigrations()` set instead. This makes migration **order** meaningful — see
      [`Mise.MigrationService/Program.cs`](../src/Mise.MigrationService/Program.cs)'s comment
      above the two `MigrateAsync()` calls. There's no architecture-test guardrail yet stopping
      a future module from getting this backwards; is that an acceptable gap for now?
- [ ] [`AuditWriter.cs`](../src/Mise.SharedKernel.Persistence/AuditWriter.cs) — the generic
      `AuditWriter<TDbContext>` both Reservations and StaffIdentity now register for
      `IAuditWriter`, replacing Reservations' own former copy.

### StaffIdentity — Domain

- [ ] [`StaffRole.cs`](../src/Modules/Mise.Modules.StaffIdentity.Domain/StaffRole.cs) — exactly
      two values, matching the charter's "two-tier roles."
- [ ] [`StaffUser.cs`](../src/Modules/Mise.Modules.StaffIdentity.Domain/StaffUser.cs) — private
      constructor, `Create(...)` factory, one invariant (`FullName` required). Deliberately
      thin: no `PinHash` yet (Phase 13), no `Deactivate()` method yet (no use case calls it).

### StaffIdentity — Application

- [ ] [`Ports/IStaffIdentityData.cs`](../src/Modules/Mise.Modules.StaffIdentity.Application/Ports/IStaffIdentityData.cs)
      — one gateway covering both the Identity credential row and the module's own profile
      row, since they must change together. `UsernameTaken` and the four
      `ValidateCredentialsOutcome` values are modeled as **outcomes**, not exceptions — do you
      agree that's the right split between "expected business outcome" and "exceptional"?
- [ ] [`Ports/IJwtTokenIssuer.cs`](../src/Modules/Mise.Modules.StaffIdentity.Application/Ports/IJwtTokenIssuer.cs)
      — Application defines the port, Infrastructure does the actual JWT library work.
- [ ] [`RegisterStaff/RegisterStaffCommandHandler.cs`](../src/Modules/Mise.Modules.StaffIdentity.Application/RegisterStaff/RegisterStaffCommandHandler.cs)
      — mirrors `CreateReservationCommandHandler`'s shape exactly (validate, call gateway,
      branch on outcome, audit once). A taken username becomes a field-scoped
      `ValidationException` from the *handler*, not the validator — the validator can't know
      about a database-level uniqueness conflict. Does routing it through the same exception
      type/400 shape as ordinary validation failures read as consistent or as a layering leak?
- [ ] [`Login/LoginCommandHandler.cs`](../src/Modules/Mise.Modules.StaffIdentity.Application/Login/LoginCommandHandler.cs)
      — read the doc comment: this handler is **not** OperationId-idempotent like
      `RegisterStaffCommandHandler` (a repeated login isn't a duplicated side effect the way a
      repeated registration would be), but it still takes `IAuditWriter` because
      `CrossCuttingTests`' rule matches every `*CommandHandler` by name, not just "mutating"
      ones — and it puts that dependency to genuine use (a `"SignedIn"` audit entry per
      success). Unknown-username and wrong-password are deliberately mapped to the identical
      `LoginOutcome.InvalidCredentials` — confirm you're comfortable with that (never revealing
      which one it was).

### StaffIdentity — Infrastructure

- [ ] [`Persistence/StaffIdentityDbContext.cs`](../src/Modules/Mise.Modules.StaffIdentity.Infrastructure/Persistence/StaffIdentityDbContext.cs)
      — `IdentityUserContext<StaffIdentityUser, Guid>`, **not** `IdentityDbContext` (no Roles
      table — Role is the single required enum column on `StaffUser` instead). Read the doc
      comment about `AspNetUsers` and friends keeping Identity's own default PascalCase table
      names rather than this project's usual snake_case — standard practice for framework-owned
      tables, not an inconsistency.
- [ ] [`Persistence/Migrations/*_InitialCreate.cs`](../src/Modules/Mise.Modules.StaffIdentity.Infrastructure/Persistence/Migrations) —
      read the generated SQL-equivalent directly. Confirm it creates `staff_identity.AspNetUsers`,
      `staff_identity.staff_user`, and the Identity satellite tables (`AspNetUserClaims`,
      `AspNetUserLogins`, `AspNetUserTokens`) — and confirm it does **not** attempt to create
      `shared.audit_log_entry`/`shared.processed_operation` (those are Reservations' migration's
      job; see the shared-persistence note above). Also note the unique index on
      `NormalizedUserName` — that's what a duplicate-username registration actually collides
      against at the database level.
- [ ] [`Persistence/StaffIdentityData.cs`](../src/Modules/Mise.Modules.StaffIdentity.Infrastructure/Persistence/StaffIdentityData.cs)
      — the `AutoSaveChanges = false` trick on `IUserStore<StaffIdentityUser>` (resolved
      separately from `UserManager`, since `UserManager.Store` is `protected`) is what makes the
      Identity user, the profile row, and the `ProcessedOperation` row commit atomically in one
      `SaveChangesAsync` call. This is more atomicity than Phase 2's own `AuditWriter` gets (that
      one is still a deliberately separate `SaveChangesAsync`, an accepted gap until Phase 9's
      interceptor). Does the inconsistency between "this write is atomic" and "that write isn't
      yet" bother you, or is it fine given they're different guarantees for different reasons?
- [ ] [`Security/JwtTokenIssuer.cs`](../src/Modules/Mise.Modules.StaffIdentity.Infrastructure/Security/JwtTokenIssuer.cs)
      — 8-hour token lifetime (a work shift). Claims: `NameIdentifier` (staff id),
      `Name` (full name), `Role`.
- [ ] [`StaffIdentitySeeder.cs`](../src/Modules/Mise.Modules.StaffIdentity.Infrastructure/StaffIdentitySeeder.cs)
      — bootstraps exactly one Manager account, idempotent, called directly by
      `Mise.MigrationService` (bypassing the Application layer entirely — a startup/ops concern,
      not a business use case, so it has no OperationId and no audit attribution).
- [ ] [`StaffIdentityPersistenceServiceCollectionExtensions.cs`](../src/Modules/Mise.Modules.StaffIdentity.Infrastructure/StaffIdentityPersistenceServiceCollectionExtensions.cs)
      — read the comment on the relaxed `PasswordOptions`: FluentValidation
      (`RegisterStaffCommandValidator`) is the single source of password policy, so Identity's
      own (stricter-by-default) policy is deliberately loosened to match it exactly rather than
      silently enforcing a second, overlapping policy.

### The placeholder auth spine, now real

- [ ] [`src/Mise.ServiceDefaults/JwtAuthDefaults.cs`](../src/Mise.ServiceDefaults/JwtAuthDefaults.cs) —
      renamed from `PlaceholderAuthDefaults` (read the doc comment for why: CLAUDE.md's own rule
      that a stale name is worse than none). Same `Issuer`/`Audience`/`SigningKeyConfigKey`
      constants, now used by the real `JwtTokenIssuer` instead of a fixed system-identity minter.
- [ ] [`src/Mise.ApiService/Program.cs`](../src/Mise.ApiService/Program.cs) — the `"FloorStaff"`
      and `"Manager"` policy definitions, right below the JWT-bearer setup.
- [ ] [`src/Mise.ApiService/Staff/AuthEndpoints.cs`](../src/Mise.ApiService/Staff/AuthEndpoints.cs)
      and [`StaffEndpoints.cs`](../src/Mise.ApiService/Staff/StaffEndpoints.cs) — `/api/auth/login`
      is `AllowAnonymous`; `/api/staff` requires the `"Manager"` policy.
- [ ] [`src/Mise.ApiService/Reservations/ReservationsEndpoints.cs`](../src/Mise.ApiService/Reservations/ReservationsEndpoints.cs)
      — `PerformedByStaffId` now comes from the caller's real `NameIdentifier` claim, not
      `httpContext.User.Identity?.Name`. Compare against
      [`CreateReservationCommand.cs`](../src/Modules/Mise.Modules.Reservations.Application/CreateReservation/CreateReservationCommand.cs)'s
      doc comment from Phase 2 — this is exactly the change that comment said Phase 3 would make.

### Error handling — logging isn't the same as "showed the user something"

New, project-wide, added while reviewing this phase. Read CLAUDE.md's new "Error handling"
section first.

- [ ] [`src/Mise.ApiService/GlobalExceptionHandler.cs`](../src/Mise.ApiService/GlobalExceptionHandler.cs)
      — the catch-all safety net, registered after `ValidationExceptionHandler` in
      [`Program.cs`](../src/Mise.ApiService/Program.cs) (order matters: first handler to return
      `true` wins). Logs the full exception at `Error`; the response carries only a generic
      title and a `traceId`, never the exception's type or message.
- [ ] [`tests/Mise.IntegrationTests/GlobalExceptionHandlerTests.cs`](../tests/Mise.IntegrationTests/GlobalExceptionHandlerTests.cs)
      — read the doc comment for how it triggers a *genuine* unhandled exception through the
      real login endpoint (deleting a `staff_user` row out from under its `AspNetUsers` row,
      so `StaffIdentityData`'s `SingleAsync` throws) rather than adding test-only production
      code. Does this feel like a legitimate trigger to you, or too contrived?
- [ ] [`src/Modules/Mise.Modules.StaffIdentity.Application/RegisterStaff/RegisterStaffCommandValidator.cs`](../src/Modules/Mise.Modules.StaffIdentity.Application/RegisterStaff/RegisterStaffCommandValidator.cs)
      — the new username character-set rule, found *because of* this review: without it, a
      username with a disallowed character (e.g. a space) sailed past validation and hit
      Identity's own `CreateAsync` rejection instead, surfacing as an opaque 500 rather than an
      ordinary field-scoped 400. Closing this is arguably more valuable than the catch-all
      itself — the catch-all is the net for what's *left over* after cases like this are closed.
- [ ] [`src/Mise.MigrationService/Program.cs`](../src/Mise.MigrationService/Program.cs) — the new
      try/catch around the whole startup body, logging `Critical` before rethrowing.
- [ ] [`src/UI/Mise.UI.Components/ReservationForm.razor.cs`](../src/UI/Mise.UI.Components/ReservationForm.razor.cs)
      and [`src/Mise.Web/Components/Pages/Login.razor`](../src/Mise.Web/Components/Pages/Login.razor)
      — both now wrap their call into a typed HTTP client and show a generic message distinct
      from an expected failure's message. Read the comment on why these use a plain
      `Logger.LogError(...)` call instead of `[LoggerMessage]` — a genuine incompatibility
      between that source generator (needs a field) and Blazor's `[Inject]` (only populates a
      property), not a shortcut.

### Mise.Web — the sign-in split

- [ ] Read **CLAUDE.md's new "Blazor Server split" section** before the files below — it explains
      the two things that had to be true simultaneously: an ordinary cookie for the site's own
      `[Authorize]`, and the API's JWT held server-side (never in the browser).
- [ ] [`Components/Pages/Login.razor`](../src/Mise.Web/Components/Pages/Login.razor) — no
      `@rendermode` (static SSR), `[CascadingParameter] HttpContext`, calls `SignInAsync`
      directly. `[SupplyParameterFromForm]`'s `Input` property has no inline initializer
      (`OnInitialized` sets it instead) — this is the fix for a real compiler error (`BL0008`)
      the first version of this file hit; worth understanding even if you never trip it yourself.
- [ ] [`Services/IStaffSessionTokenCache.cs`](../src/Mise.Web/Services/IStaffSessionTokenCache.cs) /
      [`StaffSessionTokenCache.cs`](../src/Mise.Web/Services/StaffSessionTokenCache.cs) — a
      singleton keyed by staff id, so a token survives a circuit reconnect (the cookie — and
      therefore the claim — outlives any one circuit).
- [ ] [`Services/HttpReservationsClient.cs`](../src/Mise.Web/Services/HttpReservationsClient.cs) —
      read `AttachBearerTokenAsync`'s doc comment. The token is attached inside the typed client
      itself, specifically **not** via a `DelegatingHandler` registered with
      `AddHttpMessageHandler<T>()` — that mechanism resolves from `IHttpClientFactory`'s pooled
      internal scope, not the calling circuit's DI scope, so a scoped
      `AuthenticationStateProvider` injected there would silently be the wrong instance. Is this
      reasoning clear from the comment alone, or does it need more?
- [ ] [`Program.cs`](../src/Mise.Web/Program.cs) — cookie auth registration,
      `AddCascadingAuthenticationState()`, the `/logout` minimal-API endpoint (also a plain GET,
      for the same "needs a real HTTP response" reason `Login.razor` isn't interactive).
- [ ] [`Components/Layout/NavMenu.razor`](../src/Mise.Web/Components/Layout/NavMenu.razor) —
      `<AuthorizeView>` swaps "Sign in" for the current user's name + "Sign out"; the
      Reservations nav link only shows when authenticated.
- [ ] [`Components/Pages/Reservations.razor`](../src/Mise.Web/Components/Pages/Reservations.razor) —
      the one-line addition, `@attribute [Authorize]`.
- [ ] [`Resources/Components/Pages/Login.resx`](../src/Mise.Web/Resources/Components/Pages/Login.resx) /
      [`Login.nl-BE.resx`](../src/Mise.Web/Resources/Components/Pages/Login.nl-BE.resx) — the
      localization *wiring* (NFR-08), deliberately minimal content (just this one page's
      strings) — plan.md explicitly allows full-UI content coverage to wait for Phase 11.

### Architecture tests

- [ ] [`tests/Mise.ArchitectureTests/ModuleRegistry.cs`](../tests/Mise.ArchitectureTests/ModuleRegistry.cs)
      — `"StaffIdentity"` is now listed alongside `"Reservations"`.
- [ ] Rerun `dotnet test tests/Mise.ArchitectureTests` yourself if you want extra confidence — all
      18 rules now check two real modules instead of one, several for the first time
      non-vacuously (e.g. `Application_NeverReferencesAnotherModulesDomainApplicationOrInfrastructure`
      finally has two modules to check *against* each other).

### New test files, if you want to read the tests themselves

Unit: `tests/Mise.UnitTests/StaffIdentity/*.cs` (5 files). Integration:
`tests/Mise.IntegrationTests/{AuthEndpointTests,StaffEndpointTests,MigrationHistoryTests,PasswordRehashTests,StaffRegistrationSteps}.cs`.
E2E: `tests/Mise.E2ETests/{LoginE2ETests,LoginSteps}.cs` plus the updated
`ReservationsE2ETests.cs`. The password-rehash test
([`PasswordRehashTests.cs`](../tests/Mise.IntegrationTests/PasswordRehashTests.cs)) is worth a
closer read — it's the proof of plan.md's non-deferrable "rehash-on-login" guarantee: it
manufactures a hash with an older `PasswordHasherCompatibilityMode`, logs in through the real
`/api/auth/login` endpoint, and asserts the stored hash changed afterward.

---

## 3. Manual walkthrough — the browser

```powershell
aspire run
```

- [ ] The Aspire dashboard opens. `postgres`, `migrations`, `apiservice`, `webfrontend` all show
      healthy/completed. Check the `migrations` resource's logs: you should see both
      "Applying migrations for ReservationsDbContext... applied" and the same for
      `StaffIdentityDbContext`, **in that order**, followed by a "Seed Manager account
      `manager`: created" (or "already existed" on a second run) line.
- [ ] Open the `webfrontend` URL. The home page loads with no sign-in required.
- [ ] Click **Reservations** in the nav — you're redirected to `/login` (you're not signed in
      yet). Confirm the URL becomes `/login?returnUrl=...`.
- [ ] Sign in with username `manager` and the password you set as `seed-manager-password`.
  - [ ] You land back on `/reservations` (the `returnUrl` round-tripped).
  - [ ] The nav now shows your name ("Restaurant Manager") and a "Sign out" link instead of
        "Sign in".
- [ ] Create a reservation exactly as in Phase 2's walkthrough (customer name, party size, date).
      It still appears in "Today's reservations".
- [ ] Click **Sign out**. You land back on the home page; the nav shows "Sign in" again.
- [ ] Try entering `/reservations` directly in the address bar while signed out — you're
      redirected to `/login` again, not shown an error page.
- [ ] Sign in again with a **wrong** password.
  - [ ] An inline error appears (`"Incorrect username or password."` in English, or the nl-BE
        text if your browser's language preference is Dutch) — the page does not navigate away.
- [ ] **See the new error handling actually fire:** with `aspire run` still going, stop just the
      `apiservice` resource from the Aspire dashboard (leave `webfrontend` running), then:
  - [ ] Try to create a reservation on `/reservations`. Expect a message reading *"Something
        went wrong creating the reservation. Please try again."* under the form — **not** a
        blank page, not the "An unhandled error has occurred. Reload." banner, and no browser
        console stack trace dump. The page stays usable.
  - [ ] Sign out, then try to sign in again. Expect *"Something went wrong signing in. Please
        try again."* on the login form itself — not ASP.NET Core's generic `/Error` page.
  - [ ] Restart `apiservice` from the dashboard and confirm both flows work again normally.

## 4. Manual walkthrough — the API directly

With `aspire run` still going, find `apiservice`'s URL from the Aspire dashboard.

- [ ] **Real login** (single-quoted strings, like Phase 2's checklist — PowerShell doesn't need
      the JSON's double quotes escaped inside them):
  ```powershell
  $loginJson = curl.exe -s -X POST https://<apiservice-host>/api/auth/login `
    -H "Content-Type: application/json" `
    -d '{"username":"manager","password":"<your seed-manager-password>"}'
  $login = $loginJson | ConvertFrom-Json
  $login.token
  ```
  Expect a 200 with a `token`, `expiresAtUtc`, `staffUserId`, `fullName`, and `role: "Manager"`.

- [ ] **Register a new FloorStaff member as the Manager:**
  ```powershell
  $operationId = [Guid]::NewGuid()
  curl.exe -i -X POST https://<apiservice-host>/api/staff `
    -H "Content-Type: application/json" -H "Authorization: Bearer $($login.token)" `
    -d "{`"operationId`":`"$operationId`",`"username`":`"jdoe`",`"password`":`"correct-horse-battery`",`"fullName`":`"Jane Doe`",`"role`":`"FloorStaff`"}"
  ```
  Expect `201 Created` with a `Location` header and a body showing `"role":"FloorStaff"`. (This
  one needs double quotes because `$operationId` must interpolate — PowerShell's backtick is the
  escape character there, not a backslash.)

- [ ] **The new FloorStaff member can log in and create a reservation, but cannot register
      staff:** log in as `jdoe`, then repeat the `/api/staff` call above with `jdoe`'s token.
      Expect `403 Forbidden` this time — the Manager policy rejecting a FloorStaff-role caller.

- [ ] **Unauthenticated register attempt:** repeat `/api/staff` with no `Authorization` header.
      Expect `401 Unauthorized`.

- [ ] **`scripts/mint-dev-token.ps1` still works** for testing an arbitrary role/staff id
      directly, without a real account: `./scripts/mint-dev-token.ps1 -Role FloorStaff` — read
      its updated doc comment; it now mints `NameIdentifier`/`Name`/`Role` claims (not just
      `Name`, as it did in Phase 2), since the API now reads a real staff id off the token.

## 5. Manual walkthrough — the database

```sql
select "Id", "UserName", "NormalizedUserName" from staff_identity."AspNetUsers";
select id, full_name, role, is_active from staff_identity.staff_user;
select entity_type, action, performed_by_staff_id, performed_by_system_process from shared.audit_log_entry order by occurred_at_utc;
select * from staff_identity."__ef_migrations_history";
select * from reservations."__ef_migrations_history";
```

- [ ] Every account you created above (the seeded Manager, `jdoe`) has exactly one row in
      `staff_identity."AspNetUsers"` **and** exactly one matching row (same `Id`) in
      `staff_identity.staff_user`.
- [ ] `shared.audit_log_entry` shows a `"SignedIn"` row per successful login and a `"Created"`
      row for registering `jdoe`, each with `performed_by_staff_id` populated (never
      `performed_by_system_process` — that field is only for Phase 2-era rows, if any survive
      from earlier testing).
- [ ] `staff_identity."__ef_migrations_history"` and `reservations."__ef_migrations_history"`
      are **distinct tables**, each recording its own one migration — confirms the per-module
      migration-history gotcha plan.md called out is actually holding, not just asserted by the
      automated `MigrationHistoryTests`.
- [ ] `shared.audit_log_entry`/`shared.processed_operation` exist exactly once each (obviously —
      they're physical tables — but worth confirming there isn't a second, differently-schema'd
      copy anywhere from a migration mistake).

---

## 6. Known, deliberate gaps — not bugs

Don't file these — they're documented scope boundaries for Phase 3, not oversights:

- No staff-management UI — registering staff is API-only (curl/mint-dev-token), proven at the
  integration tier. A Manager-facing screen for this is deferred to Phase 10 (shared RCL
  build-out) rather than built ad hoc here.
- No PIN, no device pairing, no MAUI login at all — Phase 13 (ADR-001 Amendment 2).
- No rate-limiting or lockout on repeated failed login attempts — that's specifically PIN/device
  scope (BR-11 area) per the charter, not this endpoint.
- No password-reset flow — a Manager (or the seeded bootstrap Manager) is the only way to create
  an account today; there's no self-service "forgot password."
- `AuthenticationStateProvider` doesn't revalidate mid-circuit — deactivating a staff account (no
  such feature exists yet anyway) wouldn't kick an already-open browser tab until it reconnects.
  Acceptable for now; would need revisiting alongside any future "deactivate staff" feature.
- Localization content covers only `Login.razor`'s strings — the wiring (`IStringLocalizer`,
  `.resx`, `UseRequestLocalization`) is real and tested, but the rest of the UI's strings are
  still hardcoded English/inline text. Full coverage is Phase 11.
- `RegisterStaffAsync`'s atomicity depends on setting `AutoSaveChanges = false` on the resolved
  `IUserStore` — a real, working technique, but a sharper edge than the rest of this project's
  persistence code; flag if you'd rather see this documented even more prominently or reconsidered.
- The shared-table migration-ownership convention (`isOwner: true/false`) has no
  architecture-test guardrail yet — a future module could get this backwards and only find out
  when its migration throws "relation already exists" against a real database.

---

## 7. Sign-off

- [ ] I reviewed the code sections above and have no unresolved concerns, **or** I've listed
      concerns below with file/line references.
- [ ] I ran the manual browser + API + DB walkthroughs and everything matched what's described
      above, **or** I've listed discrepancies below.

**Notes / concerns:**

```
(space for you to fill in)
```
