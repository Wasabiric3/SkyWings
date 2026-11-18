# SkyWings Flat Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle every SkyWings screen to the flat brand kit while removing the repaint-heavy glass techniques that make the app laggy.

**Architecture:** Keep the `Glass*` class names and public properties so no form needs editing; change `Theme.cs` and the controls' paint code to flat solid drawing. Brand images are rendered once from the kit's SVGs to PNG (1x/1.5x/2x), embedded as resources and served by one cached `Assets` helper.

**Tech Stack:** C# / WinForms on net10.0-windows, MSTest (`WinFormsApp3.Tests`), Node + `@resvg/resvg-js` for the one-time asset conversion (not part of the build).

**Spec:** `docs/superpowers/specs/2026-11-18-flat-restyle-design.md`

## Global Constraints
- No new runtime NuGet packages; no SVG library in the app.
- Class names `GlassForm`, `GlassCard`, `GlassButton`, `FieldRow`, `ChoiceCard`, `StepBar`, `SeatMap` stay; `GlassCard.ShadowSize` and `.Opacity` stay as ignored properties; `GlassCard.DisplayRectangle` inset must equal today's (`S(ShadowSize) + S(ContentPadding)`) so layouts do not shift.
- Palette: Primary `#527866`, dark green `#315B49` (hover/pressed `#234739`), soft fill `#DAECE1`, TextPrimary `#171C1A`, TextSecondary `#77857E`, BgMain `#F3F7F4`, borders `#E1E8E4` / `#DFE7E1`, Seat Available `#9EC8AC`.
- Font stays Segoe UI, sizes unchanged. Layout stays DPI-aware via `Gfx.S`; `AutoScaleMode.None`.
- No `Color.Transparent` backgrounds over custom-painted surfaces.
- No changes to database, booking logic, security or email code. No layout reorganisation.
- All 198 existing tests keep passing (one unidentified flaky failure was seen once: rerun before blaming the restyle).
- The project is not in git: "checkpoint" steps copy the work to `C:\Users\Patrick\source\repos\WinFormsApp3\_backup\<name>` (user may `git init` instead).

## Review Focus
1. High-DPI (150%/200%): icons and logo stay crisp, not blurry or oversized. Test: bucket selection by DPI (Task 4).
2. A missing or corrupt embedded image must not crash a screen: helper returns `null` and callers skip drawing. Test (Task 4).
3. Disabled and focused controls stay readable and keyboard focus stays visible on flat buttons, fields and seats. Test: disabled button text contrast >= 3:1 (Task 3).
4. Tab pages, dialogs and forms opened after `Theme.Apply` already styled children must not turn grey/white or show transparent holes. Test (Task 1).
5. Seat labels like "01A", long button captions and long route names are not clipped or overlapping at 100% and 150%. Verified in the manual click-through (Task 7), plus a text-fit test for seats (Task 3).

---

### Task 0: Baseline and backup
**Files:** Create `_backup/baseline/` (copy of `WinFormsApp3/` excluding `bin`, `obj`).
- [ ] **Step 1:** Copy the project: `robocopy WinFormsApp3 _backup\baseline /E /XD bin obj` (from the repo root).
- [ ] **Step 2:** Run `dotnet build` then `dotnet test`. Expected: 0 errors, 198 passed. Record the result.
- [ ] **Step 3:** Run the app, take screenshots of login, home, booking wizard (seat step), manage booking, check-in, admin, help into `_backup/baseline-screens/` for later comparison.

### Task 1: Palette and non-transparent `Theme.Apply`
**Files:** Modify `WinFormsApp3/Theme.cs`; Test `WinFormsApp3.Tests/ThemeTests.cs` (create).
**Interfaces:** Produces `Theme.Primary`, `Theme.Accent` (= `#315B49`), `Theme.AccentDark` (new, `#234739`), `Theme.SoftFill` (new, `#DAECE1`), `Theme.TextPrimary`, `Theme.TextSecondary`, `Theme.Border`, `Theme.BorderLight`, `Theme.BgMain`, `Theme.BgCard` with the Global Constraints values.
- [ ] **Step 1: Write failing tests** in `ThemeTests`: `Palette_MatchesBrandKit` (asserts each token's ARGB against the constraints list); `Apply_DoesNotMakePanelsLabelsTransparent` (build a `Form` with a `Panel` containing a `Label`, `CheckBox` and `RadioButton`; after `Theme.Apply(form)` assert none has `BackColor == Color.Transparent`, panel/labels equal their parent's `BackColor`); `Apply_TabPageTakesBgMain`; `Apply_SkipsSelfStyledChildren` (an `ISelfStyled` control's colours are untouched).
- [ ] **Step 2:** Run `dotnet test --filter ThemeTests`. Expected: FAIL.
- [ ] **Step 3:** In `Theme.cs` update the colour constants and add `AccentDark`, `SoftFill`. In `Apply`, replace the `Color.Transparent` assignment with `control.BackColor = control.Parent?.BackColor ?? BgMain` for Panel/Label/CheckBox/RadioButton, applied top-down so a label inside a white card gets white. `Apply` must run again when a control is re-parented into a card (call sites already call it after building the tree; verify with the tests).
- [ ] **Step 4:** Run `dotnet test --filter ThemeTests`. Expected: PASS. Run the full suite.
- [ ] **Step 5:** Checkpoint to `_backup/task1`.

### Task 2: Flat `GlassForm` and `GlassCard`
**Files:** Modify `UI/GlassForm.cs`, `UI/GlassControls.cs` (GlassCard only), `UI/DestinationCard.cs`, `UI/StatTile.cs`, `UI/PriceSummaryPanel.cs` (only if they reference removed members); Test `WinFormsApp3.Tests/GlassCardTests.cs` (create).
**Interfaces:** Consumes Task 1 theme tokens. Produces `GlassCard` unchanged public API (`Radius`, `ShadowSize`, `Opacity`, `ContentPadding`, `DisplayRectangle`).
- [ ] **Step 1: Write failing tests:** `DisplayRectangle_InsetUnchanged` (card 400x300, ShadowSize 10, ContentPadding 24 at 96 dpi: rectangle `X == 34`, `Width == 332`); `ShadowSize_AndOpacity_AreAcceptedButNotRequired` (setting them does not throw and does not change `BackColor`); `Card_IsOpaque` (a new `GlassCard` has `BackColor == Theme.BgCard` and `BackColor.A == 255`).
- [ ] **Step 2:** Run `--filter GlassCardTests`. Expected: FAIL on opacity test.
- [ ] **Step 3:** `GlassForm`: delete the background bitmap, `BuildBackground`, rebuild timer, and `ResizeRedraw`; set `BackColor = Theme.BgMain`; keep `DoubleBuffered`, `WindowBackdrop.Apply`, `SizeForScreen`. `GlassCard`: remove the bitmap cache, shadow loop and `SupportsTransparentBackColor`; `BackColor = Theme.BgCard`; paint one rounded rect fill (radius 16) plus a 1px `Theme.BorderLight` border directly in `OnPaint`; keep `CardBounds`/`DisplayRectangle` math identical. Keep `ResizeRedraw`.
- [ ] **Step 4:** Tests pass; run the app and confirm home/login render with flat cards and no transparency artefacts.
- [ ] **Step 5:** Checkpoint to `_backup/task2`.

### Task 3: Buttons, fields, choice cards, step bar
**Files:** Modify `UI/GlassControls.cs` (`GlassButton`, `StepBar`, `FieldRow`, `ChoiceCard`); Create `UI/Colors.cs` with `static class Contrast { public static double Ratio(Color a, Color b) }`; Test `WinFormsApp3.Tests/GlassButtonTests.cs`.
**Interfaces:** Produces `GlassButton` colours per `GlassButtonStyle`: Primary (fill `Theme.Primary`, white text, hover `Theme.Accent`, pressed `Theme.AccentDark`), Secondary (fill `Theme.SoftFill`, text `Theme.Accent`), Ghost (no fill, text `Theme.Accent`), Danger (existing red family, flat). Disabled: fill `Theme.DisabledBack`, text `Theme.DisabledText`.
- [ ] **Step 1: Write failing tests:** `Contrast.Ratio` known values (black/white = 21); `PrimaryButton_TextContrastAtLeast4_5` (white on `Theme.Primary`); `DisabledButton_TextContrastAtLeast3` (`DisabledText` on `DisabledBack`).
- [ ] **Step 2:** Run `--filter GlassButtonTests`. Expected: FAIL (no `Contrast`).
- [ ] **Step 3:** Implement `Contrast.Ratio` (WCAG relative luminance). Restyle `GlassButton` (radius = 0.27 × height, solid fills, no gradient/transparency, bold `Theme.Body` font taken from a shared static font instead of `new Font` per instance; keep the focus ring and `IButtonControl` behaviour). `FieldRow`: white fill, 1px `Theme.Border`, `Theme.Primary` border on focus. `ChoiceCard`: white fill + border, selected = `Theme.SoftFill` fill with `Theme.Primary` border. `StepBar`: flat circles (active Primary, done Accent, pending `Theme.StepPending`); cache the two fonts used in `OnPaint`. Fix any contrast test failure by adjusting `DisabledText` in `Theme.cs`.
- [ ] **Step 4:** Tests pass. Run the app; check hover, pressed, disabled and keyboard focus on login and the wizard.
- [ ] **Step 5:** Checkpoint to `_backup/task3`.

### Task 4: Asset pipeline and `Assets` helper
**Files:** Create `tools/svg2png/package.json`, `tools/svg2png/convert.mjs`, `WinFormsApp3/Assets/*.png` (generated), `WinFormsApp3/UI/Assets.cs`; Modify `WinFormsApp3/SkyWings.csproj`; Test `WinFormsApp3.Tests/AssetsTests.cs`.
**Interfaces:** Produces, in `namespace SkyWings.UI`:
`internal static class Assets { internal static string Bucket(int dpi); // "x1" (<=108), "x15" (<=156), else "x2"
public static Bitmap? Icon(string name, bool white, int dpi);  // name like "Flights"
public static Bitmap? Logo(LogoVariant variant, int dpi);      // enum LogoVariant { Green, White, Black }
public static Bitmap? Hero(int dpi);                            // sage hero background
public static Bitmap? Watermark(int dpi); }`
Each returns a cached shared bitmap (callers must not dispose) or `null` if the resource is missing. Resource names: `Assets/icon_<Name>_<green|white>_<bucket>.png`, `Assets/logo_<green|white|black>_<bucket>.png`, `Assets/hero_<bucket>.png`, `Assets/watermark_<bucket>.png`.
- [ ] **Step 1: Write failing tests:** `Bucket_PicksByDpi` (96→x1, 120→x15, 144→x15, 192→x2); `Icon_Flights_Green_Loads` (non-null at each of 96/144/192); `Icon_Missing_ReturnsNull_DoesNotThrow`; `Icon_SameCall_ReturnsSameInstance`; `Logo_AllVariants_Load`; `Hero_And_Watermark_Load`.
- [ ] **Step 2:** Run `--filter AssetsTests`. Expected: FAIL.
- [ ] **Step 3:** Write `convert.mjs`: reads the kit folder (path as argument), uses `@resvg/resvg-js` to render icons (24 px base, green `#527866` and white variants, skipping the Black set), logo solid Green/White/Black (160 px base), hero (use a width of 1440 base scaled down to 720 px logical), watermark; writes the file names above for scales 1, 1.5, 2. Run it with `npm install` inside `tools/svg2png`, output into `WinFormsApp3/Assets/`. Add to `SkyWings.csproj`: `<EmbeddedResource Include="Assets\*.png" LogicalName="Assets/%(Filename)%(Extension)" />`. Implement `Assets` with a `ConcurrentDictionary<string, Bitmap?>` cache loading via `Assembly.GetManifestResourceStream`; wrap loading in try/catch returning `null`.
- [ ] **Step 4:** Tests pass; confirm output PNGs visually (open two icons and the hero).
- [ ] **Step 5:** Checkpoint to `_backup/task4`.

### Task 5: Seat map, legend and status pills
**Files:** Create `UI/SeatPalette.cs`; Modify `UI/SeatMap.cs`, `UI/GridStyle.cs`; Test `WinFormsApp3.Tests/SeatPaletteTests.cs`.
**Interfaces:** Consumes `SeatState` (existing enum: Free, Taken, Held, Selected). Produces `internal static class SeatPalette { public static (Color Fill, Color Border, Color Text) For(SeatState state, bool hover); }`. Mapping: Free = fill `#9EC8AC`, white text (hover slightly darker); Selected = `Theme.Primary`, border `Theme.Accent`, white; Taken (Occupied) = muted grey-green `#E4E9E7` fill, text `#96A29C`; Held (On hold) = existing amber family. The kit's "Other passenger" state is used only if `BookingWizardForm` already distinguishes a booking's other passengers' seats; otherwise skip it and note it in the report.
- [ ] **Step 1: Write failing tests:** `Free_UsesKitGreen` (fill == `#9EC8AC`), `Selected_UsesPrimary`, `AllStates_TextHasContrastAtLeast3_ForSeatLabel`, `SeatLabel_Fits` (measure "01A" at `Theme.Small` bold with `TextRenderer.MeasureText`; width <= seat width 38 minus 4).
- [ ] **Step 2:** Run `--filter SeatPaletteTests`. Expected: FAIL.
- [ ] **Step 3:** Implement `SeatPalette`; make `SeatMap.DrawSeat` and `SeatLegend` use it, show the seat code label on Free seats (as in the kit), drop the headrest drawing, cache the bold label font as a field disposed in `Dispose`. Selection numbers and hover/keyboard behaviour unchanged. In `GridStyle`, make `OnCellPainting` pills use `Theme.SoftFill` background with `Theme.Accent` text for Paid/Confirmed/Active, and tinted fills for Cancelled, Delayed, Pending (use the kit's `Status_*.svg` colours); keep the single `StatusColor` function.
- [ ] **Step 4:** Tests pass; open the booking wizard seat step and the admin Bookings grid.
- [ ] **Step 5:** Checkpoint to `_backup/task5`.

### Task 6: Panels, brand imagery and receipt
**Files:** Modify `UI/LoginBrandPanel.cs`, `HomeForm.cs` (hero painting around line 665+), `UI/DestinationCard.cs`, `UI/StatTile.cs`, `UI/PriceSummaryPanel.cs`, `UI/ReceiptPanel.cs`, `BoardingPassForm.cs`; Test `WinFormsApp3.Tests/PanelPaintTests.cs`.
**Interfaces:** Consumes `Assets.Logo`, `Assets.Hero`, `Assets.Watermark`, `Assets.Icon`, theme tokens. Produces no new public API.
- [ ] **Step 1: Write failing test:** `Panels_PaintWithoutException_WhenAssetsMissing` (render each of `LoginBrandPanel`, `DestinationCard`, `StatTile`, `PriceSummaryPanel`, `ReceiptPanel` into a bitmap via `DrawToBitmap` after temporarily clearing the asset cache; no exception).
- [ ] **Step 2:** Run it. Expected: FAIL only if a panel still dereferences a removed member; otherwise it passes: then record that and continue.
- [ ] **Step 3:** `LoginBrandPanel` and the home hero: draw `Assets.Hero` stretched once into a cached bitmap per size, the white logo mark and "SkyWings" text (Segoe UI bold) over it; remove the painted plane illustration. `DestinationCard`: flat white card, `Theme.SoftFill` accent strip, map-pin icon. `StatTile`, `PriceSummaryPanel`: flat, borders per card. `ReceiptPanel` and `BoardingPassForm`: use the kit's receipt card / boarding pass colours, dashed divider, logo; leave the printed output alone unless the print code already uses `ReceiptPanel` drawing (then align it).
- [ ] **Step 4:** Tests pass; compare login, home, receipt and boarding pass screens with `_backup/baseline-screens`.
- [ ] **Step 5:** Checkpoint to `_backup/task6`.

### Task 7: Sweep, cleanup and verification
**Files:** Modify any leftovers found; Delete unused `Gfx.Glow` if nothing references it.
- [ ] **Step 1:** Search for `Color.Transparent`, `SupportsTransparentBackColor`, `ShadowSize`, `Gfx.Glow` across `WinFormsApp3/`; review each hit and justify or fix it.
- [ ] **Step 2:** Run `dotnet build` (0 errors) and `dotnet test` (198 + new tests pass).
- [ ] **Step 3:** Click through login, first-run admin, change password, home, flight details/results, booking wizard (all steps incl. seat map), manage booking, check-in, seat change, waitlist, boarding pass, admin (every tab), help and policy dialogs at 100% and 150% Windows scaling. Look for clipped text, transparent holes, wrong colours, blurry icons.
- [ ] **Step 4:** Compare resizing the window, hovering seats, scrolling the departures grid and switching destinations against the baseline build; report whether it feels smoother.
- [ ] **Step 5:** Final checkpoint to `_backup/final`; list the author's calls made during the build for the user.
