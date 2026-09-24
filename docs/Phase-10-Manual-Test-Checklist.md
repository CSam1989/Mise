# Phase 10 Manual Test Checklist — Shared RCL + Blazor screens

Companion to the automated suites, not a replacement for them. Phase 10 ships one screen family
per step (`docs/UI-plan.md`) and stops for review after each, so this file grows one section per
step. **Read CLAUDE.md's "Shared UI foundation (Phase 10.0)" section first** — it is the map for
everything below.

Check a box, or write a one-line note next to it if something looks off. Anything you flag, bring
back to the next session with the file/line and what you expected instead.

---

## 0. One-time setup

Unchanged from Phase 9 — no new secrets or Aspire parameters. One new *config* section with safe
defaults already in `src/Mise.Web/appsettings.json`:

```json
"Restaurant": { "Name": "Lilshof", "TimeZoneId": "Europe/Brussels" }
```

- [ ] `dotnet --version` prints `10.0.x`; Docker is running; `dotnet tool restore` done once.
- [ ] Playwright browsers installed (`tests/Mise.E2ETests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium`).

---

## Step 10.0 — Foundation and shell

**Scope:** theme (Bootstrap in the RCL + `mise-theme.css`, self-hosted fonts), `TestIds`, nine
primitives, the new top bar + sidebar shell, the restyled "Back office" login card, RCL
localization (en + nl-BE), the Development-only `/design` gallery. **No API change.** No new
screen beyond the gallery — Reservations keeps its Phase 2 form until 10.3.

**Decisions you made at the start of this step** (recorded so the review can check them):
nl-BE strings ship now (not Phase 11); unbuilt nav items and data-less chips are *hidden*, not
disabled; fonts are self-hosted, not Google Fonts; nothing is committed until you approve.

### 10.0.1 Re-run the automated suites

```powershell
./scripts/test.ps1
```

- [ ] All five suites green — **19 architecture, 242 unit, 116 bUnit, 148 integration,
      21 E2E (546 total)**, plus the format check. Phase 9 was 418; the delta is **+111 bUnit**
      (5 → 116) and **+17 E2E** (4 → 21). Architecture/unit/integration counts are unchanged —
      nothing server-side moved.
- [ ] Run E2E two or three times in a row — it was run 3× consecutively green in the session that
      wrote this step; one earlier flake (focus-trap timing, see 10.0.5) was a test bug and is fixed.

### 10.0.2 Code review — files to read

| Area | Files | What to look for |
|---|---|---|
| Render mode | `src/Mise.Web/Components/App.razor`, `Pages/Login.razor`, `Pages/Error.razor` | Global `InteractiveServer(prerender:false)`; only Login/Error opt out via `[ExcludeFromInteractiveRouting]`. Reservations.razor no longer has `@rendermode`. |
| Theme | `src/UI/Mise.UI.Components/wwwroot/css/mise-theme.css` | Only Bootstrap variable overrides + status sets + avatar palette. Three colours deliberately differ from the mockup (AA contrast — see 10.0.6). |
| Assets | `wwwroot/lib/bootstrap/dist/css/bootstrap.min.css(.map)`, `wwwroot/fonts/*.woff2` | Bootstrap moved (git shows a rename) and the other 42 template files were deleted — only the minified CSS was ever referenced. |
| Primitives | `Primitives/` — `PageHeader`, `StatusPill`, `StatusDot`, `Drawer`, `Modal` (+ `OverlayBase`), `EmptyState`, `Stepper`, `SegmentedControl<T>`; `Feedback/ToastService` + `ToastHost` | Parameters `[EditorRequired]` where mandatory; every string via `IStringLocalizer<UiStrings>`; `data-testid` only from `TestIds`. |
| Shell | `Shell/` — `AppShell`, `TopBar`, `SideNav`, `ShellClock`, `NavEntry`/`MiseNavigation`, `StaffIdentityView`, `AuthFrame`, `AuthCard` | Nav lists only existing screens; Manager group behind `AuthorizeView Policy="Manager"`. Clock ticks on the minute boundary via `TimeProvider`. |
| Strings | `Resources/UiStrings.resx`, `UiStrings.nl-BE.resx`, `src/Mise.Web/Resources/Components/Pages/Login*.resx` | Values match the mockup's `T.en`/`T.nl`. New non-mockup copy (a11y names, error banner) is listed in 10.0.4. |
| Host wiring | `src/Mise.Web/Program.cs`, `RestaurantOptions.cs`, `Layout/MainLayout.razor`, `Layout/AuthLayout.razor`, `Pages/Home.razor`, `Pages/DesignGalleryPage.razor` | Policies mirror the API; `RestaurantOptions` validated on start (bad time zone ⇒ startup fails loudly); `/design` 404 gate outside Development. |
| Security fix | `Pages/Login.razor` (`RedirectHttpResult.IsLocalUrl`), `RedirectToLogin.razor` (now sends a relative path) | **Pre-existing open redirect**, found by `/code-review` this step: `?returnUrl=https://evil…` used to forward a freshly signed-in user off-site. Now only local paths are honoured. |
| Tests | `tests/Mise.Client.UnitTests/**`, `tests/Mise.E2ETests/**` (see 10.0.10 for the page-object layout) | bUnit selectors via `TestIds`; Playwright locators only inside page objects via `GetByTestId`; no sleeps; time via `FakeTimeProvider`. |

- [ ] `git diff --stat` — nothing outside `src/Mise.Web`, `src/UI`, `tests/Mise.Client.UnitTests`,
      `tests/Mise.E2ETests`, `Directory.Packages.props`, `CLAUDE.md`, `docs/`.
- [ ] `Directory.Packages.props` gained exactly two versions: `Microsoft.AspNetCore.Components.Authorization`
      and `Microsoft.Extensions.Localization` (both 10.0.12). No `Version=` on any `PackageReference`.
- [ ] `Mise.UI.Components.csproj` still references **only** `Mise.UI.Abstractions` (architecture
      tests enforce this; confirm by eye too).

### 10.0.3 Run it and compare with the prototype

```powershell
aspire run          # or: dotnet run --project src/Mise.AppHost
```

Open the prototype side by side: unzip `docs/Mise UI mockups design.zip`, serve the folder
(`npx http-server -p 8765`) and open `Mise Prototype.dc.html` — `file://` is blocked by some
browsers' module loading.

**Login (`/login`), at 1280×800 and 1024×768**
- [ ] "Mise" serif wordmark top-left with "Lilshof · Front of House" (nl: "Lilshof · Zaal").
- [ ] Centered white card, eyebrow "BACK OFFICE", title "Sign in to Mise", white inputs, full-width navy button.
- [ ] Wrong password → red alert "Incorrect username or password." and you stay on the form.
- [ ] Stop the `apiservice` resource in the Aspire dashboard, sign in → **different** message
      "Something went wrong signing in. Please try again." (no stack trace). Restart it afterwards.
- [ ] Browser language nl-BE (or remove `en` from its language list) → Dutch login ("Aanmelden bij Mise", "Backoffice").

**Shell (sign in as the seeded Manager)**
- [ ] Lands on `/reservations` (Home `/` now redirects there — the floor plan arrives in 10.1).
- [ ] Dark top bar: wordmark, restaurant name, date chip (e.g. "Thu 24 Sep" / "do 24 sep"), clock
      in **Brussels** time (not UTC), avatar with initials, short name ("Sam V." style) and role.
- [ ] Leave the page open across a minute boundary → the clock updates without a reload.
- [ ] Sidebar shows **only** "Reservations" (active, navy) and "Sign out" at the bottom. No Manager
      group yet — correct, no Manager screen exists until 10.6. No connection pill, service chip,
      badges or EN/NL switcher — hidden until their step wires real data.
- [ ] "Sign out" → back to the login card; `/reservations` afterwards redirects to login again.
- [ ] Resize to ~700px wide → sidebar becomes a horizontal row; nothing overlaps.

**Floor Staff** — create one via `POST /api/staff` (Manager token, see `Mise.ApiService.http`) or reuse Phase 3's.
- [ ] Role label reads "Floor staff" (nl: "Zaalpersoneel"); same nav as Manager for now.

**Design gallery (`/design`, Development only)**
- [ ] All 5 table + 5 reservation status pills in the mockup's colours; 5 legend dots with labels; an empty state.
- [ ] Stepper: − disabled at 1, + disabled at 12; segmented control highlights the selected option (dark) and every option is a normal Tab stop (toggle buttons, `aria-pressed`).
- [ ] "Show toast" → dark toast bottom-centre, gone after ~2.4 s.
- [ ] "Open drawer" slides in from the right; Esc closes; backdrop click closes; the × closes.
- [ ] "Open modal" centred; Tab cycles inside it (see 10.0.5); click outside closes.
- [ ] Run Mise.Web with `ASPNETCORE_ENVIRONMENT=Production` (or trust `DesignGalleryGateTests`) → `/design` is a plain 404, not a login redirect. The page itself also refuses outside Development (`NavigationManager.NotFound()`), because an in-app navigation never passes through the HTTP gate.

**Known, deliberate leftovers (flag only if you disagree)**
- [ ] The Reservations page body (form labels, "Today's reservations", "No reservations yet.") is
      still the Phase 2 English-only markup, now Bootstrap-styled. It is replaced wholesale in 10.3,
      so it was not localized — expect English inside an otherwise Dutch shell until then.
- [ ] The gallery's own headings are English-only by design (developer tool, never in production).

### 10.0.4 New copy not in the mockup (`design:ux-copy`)

| Key | en | nl-BE |
|---|---|---|
| `StepperDecrease`/`StepperIncrease` | Decrease {0} / Increase {0} | {0} verlagen / {0} verhogen |
| `Notifications` (toast live region) | Notifications | Meldingen |
| `SkipToContent` | Skip to main content | Naar de hoofdinhoud |
| `MainNavigation` (nav landmark) | Main navigation | Hoofdnavigatie |
| `UnhandledError` / `Reload` / `Dismiss` (circuit error banner) | Something went wrong. Reload the page to continue. / Reload / Dismiss | Er ging iets mis. Herlaad de pagina om verder te werken. / Herladen / Sluiten |
| `Login.BackOffice`, `Login.Heading` (changed) | Back office, Sign in to Mise | Backoffice, Aanmelden bij Mise |

- [ ] Dutch wording reads naturally to a Belgian speaker.

### 10.0.5 Keyboard & screen reader (`design:accessibility-review`)

- [ ] Tab from the address bar: the "Skip to main content" link appears top-left on focus; Enter
      moves focus into the page (URL does **not** change — see CLAUDE.md gotcha).
- [ ] After every navigation, focus lands on the page's `<h1>` (FocusOnNavigate).
- [ ] Drawer/Modal: focus moves into the panel on open; Tab never reaches the page behind;
      Esc closes. **Known limitations:** focus is not returned to the button that opened it on
      close, and the Tab wrap is a server round-trip (a very fast double-Tab can briefly escape) —
      both need JS; revisit with 10.2's JS module.
- [ ] Screen reader: the user's status dots without labels announce their status name; toasts are
      announced politely; the dialog is announced by its title.

### 10.0.6 Contrast decisions (computed, WCAG 2.1 AA)

| Pair | Mockup | Ratio | Now | Ratio |
|---|---|---|---|---|
| Muted text on page canvas | `#6b7280` | 4.24 ❌ | `#636a77` | 4.77 ✅ |
| Cancelled pill text | `#8b90a0` | 2.92 ❌ | `#686d7d` | 4.73 ✅ |
| White initials on avatar colour 3 | `#a9692e` | 4.42 ❌ | `#9c602a` | 5.10 ✅ |
| Input border on white | `#d7dae0` | 1.40 ⚠️ | unchanged | — |

- [ ] Accept or revise the three darkened colours.
- [ ] **Decide:** input borders (1.40:1) fail WCAG 1.4.11's 3:1 non-text contrast — the same is
      true of stock Bootstrap. Left at the mockup value pending your call; `#868c98` would pass.
- [ ] Sidebar rows are 38px and segments 36px tall (mockup values). WCAG 2.1 AA has no target-size
      criterion (2.2's 2.5.8 needs 24px — met); stepper/close buttons are 44px. Flag if the tablet
      needs 44px everywhere.

### 10.0.7 Automated coverage for this step

**bUnit (`tests/Mise.Client.UnitTests`, 111 new):**
- `PageHeaderTests` — `Render_TitleOnly_OmitsSubtitleAndActions`, `Render_WithSubtitle_ShowsIt`, `Render_WithActions_RendersTheActionsFragment`, `Render_Title_IsTheFocusTargetH1`
- `StatusPillTests` — `Render_TableStatus_ShowsLabelAndColourSet` (×5), `Render_ReservationStatus_ShowsLabelAndColourSet` (×5), `Render_DutchCulture_ShowsDutchLabel`, `Render_UnsupportedEnum_Throws`
- `StatusDotTests` — `Render_WithoutLabel_ExposesStatusAsAccessibleName`, `Render_WithLabel_ShowsTextAndHidesDotFromAssistiveTech`, `Render_Status_AppliesItsColourSet`
- `DrawerTests` / `ModalTests` (shared `OverlayContractTests<T>`, each runs all) — `Render_Closed_RendersNothing`, `Render_Open_ShowsTitleBodyAsLabelledModalDialog`, `Render_OpenWithoutFooter_OmitsFooter`, `Render_OpenWithFooter_ShowsFooter`, `ClickClose_RaisesOnClose`, `Render_CloseButton_HasLocalizedAccessibleName`, `ClickBackdrop_RaisesOnClose`, `ClickInsidePanel_DoesNotRaiseOnClose`, `PressEscape_RaisesOnClose`, `PressOtherKey_DoesNotRaiseOnClose`, `FocusSentinel_Focused_SendsFocusBackToThePanel` (×2), `Open_MovesFocusIntoThePanel`; Modal only: `Render_Size_AppliesWidthClass` (×3)
- `EmptyStateTests` — `Render_TitleOnly_OmitsBody`, `Render_WithBody_ShowsIt`, `Render_WithAction_RendersIt`
- `StepperTests` — `Render_Value_ShowsIt`, `ClickIncrease_RaisesValuePlusOne`, `ClickDecrease_RaisesValueMinusOne`, `Render_AtMinimum_DisablesDecreaseOnly`, `Render_AtMaximum_DisablesIncreaseOnly`, `Render_Disabled_DisablesBothButtons`, `Render_Buttons_HaveExactAccessibleNames`, `Render_DutchCulture_UsesDutchAccessibleNames`
- `SegmentedControlTests` — `Render_Value_MarksOnlyThatOptionPressed`, `ClickOtherOption_RaisesValueChanged`, `ClickSelectedOption_DoesNotRaiseValueChanged`, `Render_Labels_ComeFromOptions`
- `ToastServiceTests` (FakeTimeProvider) — `Show_SetsCurrentAndRaisesChanged`, `Show_JustBeforeDisplayDuration_IsStillVisible`, `Show_AtDisplayDuration_AutoDismissesAndRaisesChanged`, `ShowTwice_ReplacesTheFirstAndRestartsTheTimer`, `Dismiss_WhenNothingShown_DoesNotRaiseChanged`
- `ToastHostTests` — `Render_NoToast_ShowsEmptyPoliteLiveRegion`, `Show_RendersTheToastText`, `Show_ErrorKind_AppliesErrorStyle`, `Show_AfterDisplayDuration_DisappearsFromTheDom`
- `ShellClockTests` (FakeTimeProvider boundaries) — `Render_ShowsRestaurantLocalDateAndTime_NotUtc`, `Render_LocalMidnightCrossing_ShowsTheLocalDay`, `AdvanceToNextMinute_UpdatesTheClock`, `AdvanceWithinTheSameMinute_DoesNotChangeTheClock`, `Render_DutchCulture_UsesDutchDayAndMonth`
- `TopBarTests` — `Render_ShowsRestaurantNameAndClock`, `Render_Manager_ShowsShortNameInitialsAndRole`, `Render_FloorStaff_ShowsFloorStaffRole`, `Render_FloorStaffDutch_ShowsDutchRole`, `Render_Anonymous_HidesUserBlock`, `Render_AvatarColour_IsOneOfThePalette`
- `SideNavTests` (Manager **and** FloorStaff rendered) — `Render_Default_ShowsOnlyScreensThatExist`, `Render_Manager_SeesManagerGroup`, `Render_FloorStaff_DoesNotSeeManagerGroup`, `Render_ManagerWithNoManagerEntries_HasNoEmptyGroupHeading`, `Render_SignOut_PointsAtTheHostsSignOutEndpoint`, `Render_Dutch_LocalizesLabels`
- `AppShellTests` — `Render_ComposesTopBarSideNavContentAndToasts`, `Render_PassesTheRestaurantNameThroughToTheTopBar`, `Render_SkipLink_TargetsTheMainLandmark`, `ClickSkipLink_FocusesMainInsteadOfNavigating`
- `AuthFrameTests` / `AuthCardTests` — `Render_ShowsRestaurantAndFrontOfHouse`, `Render_Dutch_ShowsZaal`, `Render_WithEyebrow_ShowsEyebrowAndTitle`, `Render_WithoutEyebrow_OmitsIt`
- `StaffIdentityViewTests` — `From_Name_DerivesShortNameAndInitials` (×4), `From_ManagerRole_UsesManagerLabel`, `From_FloorStaffRole_UsesFloorStaffLabel`, `From_SameStaffId_AlwaysGetsTheSameAvatarColour`
- `DesignGalleryTests` — `Render_ShowsEveryTableAndReservationStatus`, `ClickOpenDrawer_OpensTheDrawer_AndCloseDismissesIt`, `ClickOpenModal_OpensTheModal`, `StepperAndSegmentedControl_AreTwoWayBound_IntoTheToast`
- `UiStringsResourceTests` — `NeutralAndDutch_HaveExactlyTheSameKeys`, `EveryValue_IsNonEmpty`, `FormatPlaceholders_MatchAcrossCultures`

**Playwright E2E (`tests/Mise.E2ETests`, 21 total — all on page objects, see 10.0.10):**
- `LoginE2ETests` (drives the real form) — `Login_ValidCredentials_LandsOnReservationsInsideTheShell`, `Login_InvalidCredentials_ShowsErrorAndStaysOnLoginPage`, `UnauthenticatedVisit_ToReservations_RedirectsToLogin`, `Login_OffSiteReturnUrl_StaysOnMise`, `Login_SameOriginReturnUrl_ReturnsToTheRequestedPage`, `DutchBrowser_SeesTheDutchLoginCard` (nl-BE), `SignOut_ReturnsToTheLoginCardAndProtectsPagesAgain` (dedicated `E2EUsers.SignOut`)
- `ShellE2ETests` (saved sessions) — `Manager_OpensHome_LandsOnReservationsInsideTheShell`, `Shell_Landmarks_ExposeTheirRolesAndNames`, `FloorStaff_SeesFloorStaffRoleAndNoManagerGroup` (role-gated path), `DutchBrowser_SeesTheDutchShell` (nl-BE, 1024×768), `SkipLink_Focused_EnterMovesFocusToMainContentWithoutNavigating`
- `DesignGalleryE2ETests` — `Gallery_InDevelopment_RendersEveryStatusAndAnEmptyState`, `StepperAndSegments_Change_AndTheToastReflectsThem`, `Drawer_Opened_IsANamedDialogThatTrapsTabAndClosesOnEscape`, `Drawer_CloseButton_HasAnAccessibleNameAndCloses`, `Modal_ClickOutside_Closes`
- `ReservationsE2ETests` — `CreateReservation_HappyPath_AppearsInDayList` (moved onto `ReservationsPage`)
- `DesignGalleryGateTests` (host-level HTTP, no browser) — `Design_InProduction_Returns404BeforeAuth`, `Design_InDevelopment_RequiresSignIn`
- `HomePageTests` — `Home_Unauthenticated_RedirectsToTheBackOfficeLoginCard` (replaces the template's "Hello, world!" check)
- Every context leaves `playwright-traces/{TestName}.png` + `.zip` under `tests/Mise.E2ETests/bin/Release/net10.0/` — open a `.zip` with `pwsh bin/Release/net10.0/playwright.ps1 show-trace <file>` to step through a run.

**Not covered, by design:** no RCL component in 10.0 calls a client, so the "client failure path"
row of the bUnit contract has nothing to exercise yet (Login's outage message is covered manually
in 10.0.3). The stale-`If-Match`/409 and live-update E2E flows arrive with 10.1.

### 10.0.8 Review passes run before handing this over

- **`/code-review` (medium):** 1 finding — the Login open redirect (10.0.2). Fixed, with two new E2E tests.
- **`/simplify` (4 parallel agents: reuse, simplification, efficiency, altitude).** Applied: an in-page
  `/design` guard (in-circuit navigation bypassed the HTTP gate), the framework's `IsLocalUrl`
  instead of a hand-rolled check, a cached `TimeZoneInfo`, a toggle-button `SegmentedControl`
  (a radiogroup would owe arrow-key navigation), shared overlay CSS + Bootstrap `.visually-hidden`
  sentinels, dead `TestIds.Status` removed, `SideNav` splits entries once, E2E
  fixture/`TracedPage`/`ReservationsE2ETests` tidied.
- [ ] **Skipped — say if you want any of them:** merging Drawer + Modal markup into one component;
      Bootstrap `.offcanvas`/`.modal` classes instead of the mockup-matched CSS; a compile-time
      `StatusKey` instead of the runtime enum check; one shared `AddStaffPolicies()` for API + Web
      (the API still uses string literals — outside this step); hosting `ToastHost` at the app root;
      caching `StatusStyle` strings per status.

### 10.0.9 Pre-existing issue noticed, not fixed here

- [ ] `StaffIdentitySeeder.EnsureManagerExistsAsync` has the **same** 3-type-argument
      `UserOnlyStore<…>` check that Phase 9 fixed in `StaffIdentityData.RegisterStaffAsync`, so the
      seeder's `AutoSaveChanges = false` never takes effect (non-atomic seed, harmless in practice).
      Out of scope for a UI step — raised as a separate follow-up task.

### 10.0.10 Addendum — E2E moved to the Page Object Model (Playwright best practices)

Requested after the first 10.0 review. Your three decisions: **test-id locators + separate role
checks**, **one saved sign-in per role**, **own fixtures** (not `Microsoft.Playwright.Xunit.v3`).
CLAUDE.md's "Shared UI foundation (Phase 10.0)" section documents the convention for every later step.

**Layout (`tests/Mise.E2ETests/`)**

| Folder | Contents | Rule |
|---|---|---|
| `Infrastructure/` | `IBrowserHost` (both fixtures), `BrowserTest` (base class), `PlaywrightBrowser` (launch + `SetTestIdAttribute`), `E2EUsers` | Tests never create a context or start tracing themselves. |
| `Pages/` | `LoginPage`, `ReservationsPage`, `DesignGalleryPage` — each with `Path` + `GotoAsync` | One class per screen; locators + actions, no `Expect`. |
| `Pages/Components/` | `AppShell`, `Dialog` (scoped to one Drawer/Modal), `Stepper` | Anything two screens share. |

- [ ] Read `Infrastructure/BrowserTest.cs` — per-test isolated contexts, `BaseURL` set (page objects
      navigate by relative path), screenshot + trace per context on teardown, multi-context ready.
- [ ] Read `MiseE2EFixture.SignInOnceAsync` — Manager and Floor Staff sign in **once, through the real
      login page**, and their storage state is reused. Only `LoginE2ETests` drives the form.
- [ ] Understand why `E2EUsers.SignOut` exists: Mise.Web caches the API token per *staff id*, so
      signing a shared user out would silently break every later test using that user's saved session.
      Any future test that signs out / changes a password / deactivates a user needs its own seeded user.
- [ ] `grep -rn "data-testid=" tests/Mise.E2ETests --include=*.cs` returns nothing — every locator goes
      through a page object's `GetByTestId(TestIds.X)`.
- [ ] Role/name checks (`ToHaveRoleAsync`/`ToHaveAccessibleNameAsync`) now cover: nav landmark, main,
      toast status region, login heading + alert, dialog role + name, close button name, stepper group
      + button name. Flag any element you'd also want covered.
- [ ] Long "do everything" tests were split so one failure names one behaviour (the gallery test is now five).
- [ ] The reservation form's literal test ids moved into `TestIds` (`ReservationForm.razor`,
      `Reservations.razor`, `ReservationFormTests`) so bUnit and Playwright share one vocabulary.
- [ ] Ran 3× consecutively green (21/21) in the session that wrote this.

**Not done, say if you want it:** an architecture rule that fails on raw `[data-testid=` strings in
`Mise.E2ETests` — `Mise.ArchitectureTests` doesn't reference that project (it would pull in both hosts
and Playwright), so today the convention is enforced by structure (only page objects locate) and review.
