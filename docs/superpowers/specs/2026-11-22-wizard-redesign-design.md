# Booking wizard redesign (Figma) — design

Date: 2026-11-22 · Status: draft, waiting for approval

## Intent and why

The owner has a Figma design for the six-step booking wizard (Flight, Passengers, Class & extras, Seats, Payment, Receipt) and wants the app to look like it. The longer goal is a consistent new look across every form, but only the wizard has been designed; the other forms get the new look afterwards, without new Figma screens.

The wizard already has the same six steps, the same right-hand trip summary and the same features (promo code, add-ons, seat map, demo payment, receipt) as the Figma. So this is a restyle and re-layout, not a new flow. `BookingWizardController` and all booking rules stay as they are.

## Goals

- The wizard looks like the Figma: pill step bar with logo, large page title outside the card, white section cards, rounded input fields, dark-green pill buttons, light-green total box, cabin cards, restyled seat map, two-card review step, receipt card.
- The look is built once, in `Theme` and `UI/`, so later forms reuse it.
- No change to booking behaviour, prices, validation or data.
- Still usable at the current minimum window size and on scaled displays (125%, 150%).

## Non-goals

- Redesigning the home, check-in, manage-booking, staff or admin forms (phase 2, after the wizard).
- Changing business rules, the database or the controller.
- The seat window illustration and cabin artwork from the Figma (decorative, no assets exist). Cabin cards are drawn with text and a tick.
- Bundling the Inter font. The app keeps Segoe UI.
- A custom window title bar. The native one stays.

## Anti-goals (a failure even if it builds)

- Copying the Figma's sample numbers. The mockups show `PHP 47040.00000000001.00`, `PHP 600.0000001.00` in the total box and a receipt total (117,600.00) that disagrees with the summary. Real values come from the live quote.
- A total that wraps or is cut off when the amount is long.
- Text boxes that lose keyboard focus, tab order, accessible names or DPI scaling because they were wrapped for rounded corners.

## Constraints

- WinForms on .NET 10, MSTest 4. Colours, fonts and spacing come from `Theme` and `Colors` only; no hard-coded colours in forms.
- Prefer shared controls in `UI/` over one-off painting. Keep form logic in the controller; the form handles layout and events.
- Existing guard tests (`PanelPaintTests`, `GlassButtonTests`, `GlassCardTests`, `ManageBookingFormLayoutTests`, help-content tests) must keep passing.
- Update `HelpForm` and `README.md` if anything the help describes changes.

## Approaches considered

1. **Shared kit first, then restyle each step (chosen).** Build the repeated pieces once (tokens, rounded field, pill button, section card, step bar, total box), then apply them step by step. More up-front work, but the other forms inherit the look and the six steps stay consistent.
2. **Restyle each step in place.** Fastest to the first visible result, but the same pieces get re-done six times and drift apart; phase 2 would start from scratch.
3. **Rebuild the wizard on a new UI technology** (WebView2 or WPF). Would match Figma most closely, but it is a rewrite of a working app, far outside "restyle", and rejected.

## Design

### Shared kit (phase 0)

| Piece | Where | Notes |
|-------|-------|-------|
| New tokens (page background `#F6F5F0`-style warm off-white, deep green primary, soft-green fill, border, amber for held and demo notice) | `Theme.cs`, `UI/Colors.cs` | Palette is read from the Figma screenshots; exact values are the builder's call, shown to the owner before use. |
| Rounded field | `UI/` (builds on `FieldRow`) | Borderless `TextBox`/`ComboBox`/`DateTimePicker` hosted in a rounded panel; focus ring on the host; keeps tab order and `AccessibleName`. Built and tested first because it is the main technical risk. |
| Pill button | `GlassButton` styles | Primary (dark green), Secondary (white with border), Ghost. Optional arrow in the text. |
| Section card with title | `GlassCard` | Rounded, thin border, optional heading. |
| Pill step bar with logo | `StepBar` + `BrandArt` | Logo at left, six pills, current one filled. Keeps the "click an earlier step to go back" behaviour. |
| Total box | `PriceSummaryPanel` | Light-green box, amount auto-shrinks to fit the width. |

### Wizard frame

The page title and hint move out of the card to sit above the content. The step bar is the top band. The right-hand summary card is restyled ("YOUR TRIP", trip, departure, travellers, cabin, fare lines, total box). Footer has Back at the left and the primary action at the right with the arrow.

### Steps

1. **Flight:** a section card with the flight dropdown, a flight info strip (flight number, time, "From PHP …"), departure and return dates, and "Add a return flight". The Figma's separate From and To fields are dropped (they repeat the flight). The return-flight picker stays, shown when the box is ticked.
2. **Passengers:** two-column field grid (name, ID, birthday, contact, address, optional email), Senior and PWD tick boxes, a "Travellers (n)" list with an "Add traveller" pill button.
3. **Class & extras:** four cabin cards in a row (name, price, selected tick), then a card with baggage, meal, add-ons and promo code with Apply.
4. **Seats:** the existing `SeatMap` is kept (it handles every aircraft layout) and re-coloured: available light green, selected dark green with tick, held amber, taken grey. A "Selected passenger" card and a legend card sit beside it.
5. **Payment:** the amber "Demo only" notice, a Booking summary card and a Payment details card side by side; primary button "Confirm and pay".
6. **Receipt:** green header band with logo and reference, two-column details, a "Total paid" band, then Print receipt and Done. The existing `ReceiptPanel` and printer keep their data; only the look changes.

### Window

Open larger (about 1280×800 on screens that allow it), still shrinking to the current minimum via `SizeForScreen`. Layouts must hold at the minimum size.

## Phases

- **Phase 0:** the shared kit, each piece with a test.
- **Phase 1:** wizard frame and step 1, then steps 2 and 3, then 4 and 5, then 6. Build, run the tests and take a screenshot to compare with the Figma after each group.
- **Phase 2:** apply the kit to the other forms (home, check-in, manage booking, staff login, admin), one form at a time. The Refunds dialog from earlier is the first example of a form that gets it.

## Verification

- `dotnet build SkyWings.slnx` and `dotnet test SkyWings.slnx` pass after every group of steps.
- New tests: rounded field keeps tab order, accessible name and value; step bar still blocks forward jumps and allows back; total box fits a six-figure amount; wizard pages fit at the minimum size and at 150% scaling.
- Owner compares a screenshot of each step with the Figma before the next group starts.

## Left to the builder

Exact colour values and corner radii (read from the Figma, shown to the owner), the pill and field heights, how the focus ring is drawn, and the order of small controls inside each card.

## Decisions made by me, to check

- Keep Segoe UI instead of Inter.
- Drop the From and To fields and the seat illustration.
- Keep `SeatMap` rather than the Figma's fixed 2-1 grid.
- Open the window larger.
