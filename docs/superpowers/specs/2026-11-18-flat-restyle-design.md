# SkyWings flat restyle (Approach A) — design

Date: 2026-11-18. Status: awaiting approval.

## Intent and why
SkyWings is still laggy after the first optimization pass. The main remaining cost is the translucent
"glass" look: transparent child controls repaint their whole parent chain, cards draw layered shadows, and the
form background is a gradient with glows. The user supplied a Figma asset kit
(`C:\Users\Patrick\Downloads\SkyWings Assets-20261118T012905Z-1-001\SkyWings Assets`) whose style is flat:
solid fills, rounded corners, no blur or shadows. Restyle the app to that kit so it looks like the new brand
and paints cheaply.

## Goals
- Every screen adopts the kit's flat style (palette, rounded solid shapes, logo, line icons).
- Remove the repaint-heavy glass techniques: transparency over custom backgrounds, layered shadows, gradient/glow background.
- Noticeably smoother resizing, scrolling, hovering and screen switching.
- Existing screen layouts and behaviour stay the same; all 198 tests keep passing.

## Non-goals
- No layout reorganisation (no sidebar/navigation redesign). That is a later phase.
- No renaming `Glass*` classes (they keep their names so no form needs editing; rename later).
- No new runtime dependencies (no SVG library).
- No changes to database, booking logic, security or email code.

## Anti-goals
- A look that matches the kit but is no faster. Performance is part of "done".
- Blurry or mis-sized icons on high-DPI displays.
- Layout breakage in any of the ~18 forms that use the glass controls.

## Constraints
- WinForms on net10.0-windows; DPI-aware layout through `Gfx.S`; `AutoScaleMode.None`.
- WinForms cannot draw SVG natively; no SVG package is wanted.
- No git repository in this project, so keep a copy of the files being rewritten before editing (see Rollout).

## Approaches considered
- **A. Flatten the existing controls from inside (chosen).** Change `Theme.cs` and the paint code of the glass
  controls; keep class names. Least work, widest effect, forms untouched. Cost: misleading "Glass" names until a later rename.
- **B. New flat controls, migrate every form.** Cleaner names, but ~15 files edited and a much larger regression surface.
- **C. Native controls with light styling.** Fastest to draw but cannot match the kit's rounded look.
The user picked A.

## Design

### 1. Palette and typography (`Theme.cs`)
Take values from the kit (colours found in its SVGs):
| Token | Now | New |
|---|---|---|
| Primary | `#567E6C` | `#527866` |
| Accent / dark green | `#3F6556` | `#315B49` (hover/pressed `#234739`) |
| PrimaryLight / soft fill | `#699280` | `#DAECE1` |
| TextPrimary | `#1E2726` | `#171C1A` |
| TextSecondary | `#687E74` | `#77857E` |
| BgMain | `#F3F7F4` | `#F3F7F4` (kept; matches kit's page tint) |
| Borders | `#C8D8D0` / `#E1EAE4` | `#E1E8E4` / `#DFE7E1` |
Danger/warning/success keep their current meaning; adjust only if the kit's status pills specify otherwise.
Font stays Segoe UI (the kit's text is Arial/Inter in design files only). Sizes unchanged.
`Theme.Apply` stops making Panel/Label/CheckBox/RadioButton `Transparent`; they take the colour of the surface they
sit on (`BgMain` on forms, `BgCard` inside cards). This is the single biggest repaint fix.

### 2. Controls (keep class names)
- **GlassForm:** solid `BgMain` background, no gradient/glow bitmap, no background rebuild on resize (this removes
  the debounce timer added earlier). Keep the Mica title bar call. Optional faint wing watermark (see assets) only on login/home.
- **GlassCard:** white fill, 1px border `#E1E8E4`, radius 16, no shadow. `ShadowSize` and `Opacity` properties remain so
  callers compile, but are ignored (`DestinationCard` and `StatTile` set `ShadowSize = 8`); the content inset stays
  equal to the old value so layouts do not shift. Drop the cached-bitmap code from the first pass; a flat card is cheap to paint directly.
- **GlassButton:** kit button specs: radius ~17/62 of height, Primary `#527866` with white bold text, Secondary soft
  fill `#DAECE1`, Outline (border only), Disabled muted; hover/pressed shades; focus ring kept for accessibility. Ghost/Danger styles restyled to match.
- **FieldRow / GlassField / ChoiceCard / StepBar:** solid white fields with 1px border, primary-coloured focus border; flat step circles.
- **SeatMap / SeatLegend:** five states from the kit's seat SVGs: Available `#9EC8AC` with white label, Selected (my seat) primary,
  Occupied muted, On hold amber, Other passenger distinct. Labels appear on seats as in the kit. Selection and hover behaviour unchanged.
- **Status pills (`GridStyle`):** use the kit's tinted-pill look (Paid `#DAECE1`/`#315B49`, Pending, Cancelled, Delayed).
  Grids keep flat white rows.
- **Home, login and other panels** (`LoginBrandPanel`, `ReceiptPanel`, `PriceSummaryPanel`, `StatTile`, `DestinationCard`):
  recolour and flatten; replace the painted plane illustration on Home and the brand panel with the logo mark on the kit's
  sage hero background (pre-rendered bitmap, drawn once and cached).
- **Receipt and boarding pass:** follow the kit's on-screen receipt card and boarding pass layouts for colours, dividers and the logo.
  Printable receipt output stays as is unless the existing print code is simple to align with the A4 design (builder's call).

### 3. Assets pipeline
- One-time conversion with a throwaway Node script kept outside the project (e.g. under the scratchpad), using
  `@resvg/resvg-js`, rendering SVG to PNG. Not part of the build.
- Output at 1x, 1.5x and 2x for: logo mark (green/white/black), the ~30 icons in green and white, the sage hero background,
  and the wing watermark. Wordmark SVGs rely on live text; use the logo mark plus "SkyWings" text in app code instead (builder may convert if fonts render).
- Embedded as resources in `SkyWings.csproj`, loaded through one small `UI/Assets.cs` helper that picks the size for the control's DPI
  and caches each bitmap. Icons are drawn with `DrawImageUnscaled`, never rescaled per paint.

### 4. Rollout
1. Copy `UI/`, `Theme.cs`, `HomeForm.cs`, `StaffLoginForm.cs` and other files to be edited into a backup folder (no git here).
2. Theme and `Theme.Apply` (transparency removal), then GlassForm/GlassCard/GlassButton; build and run after each.
3. Remaining controls, seat map, grid pills.
4. Assets pipeline, logo and icons on login/home/sidebar-less headers and buttons where the current UI already has icon slots.
5. Receipt and boarding pass.
After each stage: `dotnet build`, `dotnet test`, and visual check of the login, home, booking wizard (including seat map), manage booking,
check-in, admin and help screens.

## Verification
- `dotnet build` with 0 errors; `dotnet test` all 198 pass (note: one unidentified flaky failure was seen once; rerun before blaming the restyle).
- Run the app and click through every screen listed above at 100% and 150% Windows scaling.
- Compare resizing, hover on the seat map and scrolling the departures grid against the pre-restyle build; they should feel smoother.
- No control uses `Color.Transparent` over a custom-painted surface (grep `Transparent` and review each hit).

## Left to the builder
Exact hover/pressed shade values, spacing tweaks where flat padding differs from the glass version, which icons go where,
the printable receipt alignment, and any small forms with unusual layouts. Calls made during build should be reported afterwards.

## Calls made by the author (not yet confirmed by the user)
- Resource format: PNG at 1x/1.5x/2x via a one-time Node conversion script.
- Keep Segoe UI, keep `BgMain`.
- Drop the first-pass card cache and the resize debounce since the flat versions don't need them.
- Keep `ShadowSize`/`Opacity` as ignored properties for compatibility.
