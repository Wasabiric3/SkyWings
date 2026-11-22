# App-wide restyle (wizard look everywhere) — design

Date: 2026-11-22 · Status: draft, waiting for approval

## Intent and why

The booking wizard was restyled to the owner's Figma design (see `2026-11-22-wizard-redesign-design.md`) and the owner likes it. Every other screen still has the old look, so the app now looks like two products. The owner wants the whole system to look like the wizard, with no screen left behind. This is phase 2 of the wizard design, now agreed as a project of its own.

Only the wizard has Figma screens. For every other screen the look is an extension of the wizard's style, and the owner judges it from screenshots.

## Goals

- Every form, dialog and tab uses the wizard's look: warm off-white page, deep-green pill buttons, rounded input fields, white section cards, large page title above the content, the new status pills and grid styling.
- The look is the default. A new form that uses `GlassForm`, `GlassButton`, `FieldRow`, `GlassCard` and `GridStyle` looks right without opting in.
- The app is consistent after the first step, and later steps are layout polish only.
- Every screen still fits at its minimum window size and at 125% and 150% display scaling, with nothing clipped.

## Non-goals

- New features, changed business rules, changed data, changed controller behaviour.
- New Figma screens, a new font (Segoe UI stays), a custom title bar (the native one with the Mica backdrop stays).
- The printed receipt and boarding pass. They already follow the brand kit.
- Rearranging what a screen offers (what is on Home, which admin tabs exist). Only the look and the layout of what is there change.

## Anti-goals (a failure even if it builds)

- A screen that mixes the old green and the new green, or old square fields next to rounded ones.
- Text clipped, wrapped badly or overlapping at the minimum size or at 150% scaling.
- Rounded or restyled inputs that lose Tab order, keyboard focus, accessible names or the Enter / Esc buttons.
- Hard-coded colours in forms. Colours come from `Theme` and `Colors` only.
- Breaking the existing guard tests to make the restyle pass.

## Constraints

- WinForms on .NET 10, MSTest 4. Form logic stays in controllers; forms only handle layout and events.
- Existing tests (`PanelPaintTests`, `GlassButtonTests`, `GlassCardTests`, `ManageBookingFormLayoutTests`, the wizard and admin tests, help-content tests) keep passing. A test may change only when it asserts the old look itself (for example an old colour), and the change is mentioned in the commit.
- `HelpForm` and `README.md` are updated if anything they describe changes.
- Work happens on a branch, in small commits, one group at a time.

## Approaches considered

1. **Shared base first, then finish each screen in groups (chosen by the owner).** The new look becomes the default in the form base, theme, button, field, card and grid styling, so the whole app changes at once. Then each group of screens gets its layout finished. The app is consistent early. Some screens look restyled but not re-laid-out for a short while.
2. **One screen at a time, each fully finished, starting with Home.** Each screen can be reviewed as finished, but the app looks half old for a long time, and the same small fixes are repeated per screen.
3. **Customer screens only, staff and admin later.** Cheapest start, but leaves the admin console and staff dialogs old, which is the inconsistency the owner wants gone.

## Design

### How it works today

The wizard switches the new look on control by control: `GlassButton.Pill = true`, `FieldRow(..., rounded: true)`, `BackColor = Theme.PageBg`, titles in `Theme.Brand`. Everything else uses the old defaults: `GlassForm` and `Theme.Apply` use `Theme.BgMain` (a cool green-grey), buttons are square-ish in the old `Primary` green, `FieldRow` draws square fields, and the grids use the old header colours.

### Phase 1 — shared base (the whole app changes)

| Piece | Change |
|-------|--------|
| Page colour | `GlassForm`, `Theme.Apply` and tab pages use the warm page colour. `BgMain` becomes the same colour as `PageBg` so code that still says `BgMain` is right too. |
| Buttons | `GlassButton` defaults to the pill shape; Primary is the deep green (`Brand`). Secondary, Ghost, Danger and Outline get pill versions. The `Pill` property stays so existing code compiles. |
| Fields | `FieldRow` draws the rounded field by default. The old square look stays available through an explicit option, for the rare cell that needs it. |
| Cards | `GlassCard` already matches. Its heading style is used by every screen that has sections. |
| Page header | A small shared `PageHeader` control: large title in `Brand`, a hint line under it, placed above the content like the wizard. New, because every form needs it. |
| Grids | `GridStyle` takes its header, selection and row colours from the new tokens; the status pills stay. Header sort arrows and the tabs' toolbars keep working. |
| Old tokens | Where the old green (`Primary`, `Accent`, `StepActive`…) marks "the main action" or "selected", it moves to the new tokens. Where it marks a status (paid, open), it stays as part of the status pill colours. Which is which is decided one use at a time. |
| Dialog standard | `PromptForm`, `StaffDialogs`, the small password and first-run forms get a shared dialog frame: header, content, button row at the bottom right with the primary pill. |

After this phase every form is already consistent in colour, buttons, fields and grids, even before its layout is finished. The build and the whole test suite must pass, and a screenshot of every form is taken and reviewed.

### Phase 2 — customer screens (layout)

In this order, each as its own group:

1. **Home**: the largest and the front door. Page header and cards for the booking entry, the destination cards, the lookups (check-in, manage booking, waitlist) and the departures board. What Home offers does not change.
2. **Check-in** and **Manage booking**: page header, section cards, the traveller list and the footer actions in the wizard's footer style.
3. **Flight results**, **Flight details**, **Change flight**, **Change seat** (it reuses the restyled seat map), **Booking detail**, **Boarding pass** window.
4. **Help** and **Policy** screens.

### Phase 3 — staff and admin (layout)

1. **Staff login**, **first-run admin**, **change password**, **prompt** and the **staff dialogs** (on the dialog frame).
2. **Waitlist** entry dialog, **Flight editor**, the booking and refund dialogs.
3. **Admin console**: the section navigation, toolbars, stat tiles, every tab's grid and its filters, the Refunds screen, the data browser. This is the largest file, but mostly shared styling, so the work is the layout of each tab.

### Windows

Each form keeps its minimum size, opens a bit larger where the new spacing needs it (through `SizeForScreen`), and is checked at the minimum size and at 150%.

## Verification

- `dotnet build` and `dotnet test` pass after every group.
- A snapshot test renders each form to an image at normal size, at its minimum size and at 150% (like `WizardSnapshotTests`). The owner reviews the images after every group before the next one starts.
- New tests: the new defaults (a form's page colour, a button's shape, a field's rounded box); each form fits at its minimum size without clipped text; the dialog frame keeps Enter, Esc and Tab order.
- Each group's commit lists the screens it covered.

## Left to the builder

Exact spacing, corner radii and heights where the wizard does not decide them; the order of small controls inside a card; which old-green uses are "main action" and which are "status".

## Decisions made by me, to check

- Page colour is switched in `GlassForm` and `Theme.Apply` (and `BgMain` is aliased to it), so the whole app changes at once.
- The pill button and rounded field become the defaults, with an explicit way to get the old square look.
- Home keeps all its current content; it only gets the page header, cards and the wizard's spacing.
- Admin tabs keep their toolbars and filters; the work there is styling and layout, not new navigation.
- Work is on a branch (`app-restyle`) and not on `main` until the owner has reviewed the screenshots.
