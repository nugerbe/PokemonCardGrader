# Handoff: Card Grader Redesign (Calibra)

## Overview
A full redesign of the Pokemon Card Grader Blazor app into a modern, editorial, data-forward UI called **Calibra — Card Grading Studio**. Covers the four primary flows: Overview (dashboard), Collection, New Submission (4-step wizard), and Submission Detail.

## About the Design Files
The files in this bundle are **design references created in HTML/React (Babel inline)** — interactive prototypes showing intended look and behavior, not production code to copy verbatim. The task is to **recreate these designs in the existing Blazor/Razor codebase** (`PokemonCardGrader.Web`) using its established patterns, component conventions, and Bootstrap grid — replacing the default Bootstrap utility look with the custom tokens defined in `styles.css`.

The original backend (Application services, DTOs, Domain entities) should remain untouched; only the Razor pages and shared components need to be updated to match these designs.

## Fidelity
**High-fidelity (hifi).** Final colors, typography, spacing, and interaction states are all decided. Recreate pixel-close, using the tokens in `styles.css` as the authoritative source.

## Scope — Pages to redesign
| Current Razor page | Redesigned view | Design file |
|---|---|---|
| `Dashboard.razor` | Overview | `components/dashboard.jsx` |
| `MyCollection.razor` + `SubmissionHistory.razor` | Collection (grid + list toggle) | `components/collection.jsx` |
| `NewSubmission.razor` | New Submission wizard | `components/new_submission.jsx` |
| `SubmissionDetail.razor` | Submission Detail | `components/submission_detail.jsx` |
| `NavMenu.razor` + `MainLayout.razor` | Sidebar + topbar shell | `components/shell.jsx` |

## Design Tokens (authoritative — see `styles.css`)

### Color
- **Paper** `#FFFFFF` — card surfaces
- **Canvas** `#F7F5F1` — app background (warm off-white)
- **Canvas sunk** `#EFECE5` — sidebar, inactive tabs
- **Rule** `#E5E1D8` — hairline borders
- **Rule strong** `#D4CEC0` — dashed dropzone borders
- **Ink** `#17171A` — primary text
- **Ink-2** `#3A3A42` — secondary text
- **Ink-3** `#6B6B74` — tertiary/labels
- **Ink-4** `#9A9AA3` — hints, inactive
- **Accent** `oklch(0.78 0.13 85)` (mustard) — primary accent; tweakable (indigo / rose / sage alternates included)
- **Accent ink** `oklch(0.28 0.06 85)`
- **Accent soft** `oklch(0.94 0.05 85)`
- **Sage** `oklch(0.72 0.08 150)` — positive deltas, "graded" state
- **Rose** `oklch(0.68 0.12 25)` — destructive only

### Typography
- **Display/UI**: `Inter Tight` 400/500/600, letter-spacing -0.02em to -0.04em at display sizes
- **Body**: `Inter` 400/500
- **Mono / numerals**: `JetBrains Mono` 400/500, `font-variant-numeric: tabular-nums` on all numeric content
- **Optional serif** (logo only): `Fraunces` 500/600

Grade numerals are the hero — `Inter Tight 500` at 28–120px, `-0.04em` tracking, tabular.

### Radii
`6 / 10 / 14 / 20` — most surfaces use 14px (`--r-lg`).

### Shadow
- `--shadow-sm`: soft 1px shadow for active nav, floating chips
- `--shadow-md`: card hover
- `--shadow-lg`: Tweaks panel

### Spacing
Page padding `28px 32px`, section gap `16–20px`, card inner padding `14–18px`.

## Views

### 1. Shell (sidebar + topbar)
- **Sidebar** — 240px wide, `--canvas-sunk` background, 1px rule on right
  - Brand mark: 28px charcoal square with `⌘` glyph; "Calibra / Card Grading Studio" subtitle
  - "WORKSPACE" and "LIBRARY" section labels — 10.5px uppercase, 0.1em tracking, `--ink-4`
  - Nav items: 8/10 padding, 10px radius; active = paper bg + shadow-sm; count pill at right in mono
  - Footer: avatar (accent bg) + user name + plan
- **Topbar** — sticky, 85% canvas color + 12px backdrop blur, 1px bottom rule
  - Breadcrumb (crumbs array) at left; search input (260px min, paper bg, `⌘K` kbd); optional right action

### 2. Overview
- 4-column stat tile row: Submissions / Graded / Avg estimated grade / Estimate accuracy
  - `.stat` class: 11px uppercase label, 40px display number (Inter Tight 500 -0.035em), 11px mono delta
- Grade distribution chart — custom vertical bars, height proportional to count; active-grade bar is `--ink`, others `--canvas-sunk`; mode marker uses `--accent` above the bar
- Estimate-vs-actual panel — 4 dimensions with 6px bar + accent tick for actual
- Recent submissions table (see Collection table spec)

### 3. Collection
- Header: title + count, right-aligned Export + New submission buttons
- Filter bar — segmented control (all/estimated/graded) inside `--canvas-sunk` pill; inline filter chips; grid/list toggle
- **Grid view** — `repeat(auto-fill, minmax(220px, 1fr))`, 18px gap
  - CardArt placeholder component (see below) in 63:88 aspect
  - Graded badge floats top-right on card (paper bg + shadow-sm)
  - Meta below: name + set/number left, "EST PSA 9.0" right with 18px display grade
- **List view** — `.clean` table: Card (with 30×42 thumbnail) / Set / Submitted / Corners / Edges / Surface / Estimate / Actual / chevron

### 4. New Submission (4-step wizard)
- **Stepper** — pill container with 4 tab-like buttons; numbered circle turns `--ink`/active, `--sage` when complete (check mark), `--canvas-sunk` ahead
- **Step 1 Select Card** — search input + grid of CardArt thumbnails with name/number labels; selected card gets 2px ink border
- **Step 2 Input Method** — 3 big option tiles: "Upload photos" / "Score manually" / "Photos + fine-tune"; each with 36px rounded-square icon, title, body, optional chip ("Recommended" / "Best"); selected has 2px ink border + sunk bg
- **Step 3 Provide Scores** — 2-column layout: main + sticky 280px sidebar
  - Main: conditional image dropzones (dashed `--rule-strong` → `--sage` on upload, with check icon), centering sliders (default style, 4 in a 2×2 grid, 30–70 range), physical condition sliders (accent thumb, 1–10 step 0.5, large display number above)
  - Sidebar: live CardArt, LIVE ESTIMATE block with 48px grade + confidence chip; estimate updates reactively from slider values via `estimateFrom()` heuristic
  - Analyzing state: accent-soft alert with spinning ring
- **Step 4 Review** — 2-col: card display + estimates (4 `GradeBadge`s, top one `lead` variant = ink bg) + scores table + primary submit button (accent, lg)

### 5. Submission Detail
- Back to Collection + submission ID/date + Export/Delete
- 320px fixed column (card art, meta, chips, uploaded photos grid) + flex main
- **Hero grade card** — huge 120px display grade (Inter Tight 500 -0.06em), "TOP ESTIMATE" or "FINAL GRADE" mono label, then 4 sub-grade stats in a row (9px label, 30px number, 6px bar)
- Actual-grade callout: `--sage-soft` bg with check + cert # if graded
- **Estimates table** — 5-column grid (company / 28px grade / label / confidence bar / method chip); 14/18 padding, rule separators
- **Record grade** empty-state card: dashed border, icon tile + copy + button; expands into form on click (Company select / Grade number / Cert text + 4 sub-grade inputs)

## Shared components

### `CardArt`
Placeholder trading-card illustration (don't try to recreate real Pokémon art):
- 63:88 aspect, 10px radius, 1px rule
- Per-card hue drives an oklch tint (`oklch(0.82 0.08 H)` + `oklch(0.72 0.12 H)`) in a 135° repeating stripe
- Inner art window + title bar with card name + HP + rarity label in mono
- Use container queries (`cqw`) so text scales with the component
- Real app should replace this with `<img src="@card.ImageUrl">` once upstream imagery is wired

### `GradeBadge`
Stacked mono label (e.g. "PSA") above huge display grade. Two variants: default (paper bg) and `lead` (ink bg, canvas text).

### `Icon`
Inline 24-box stroke SVGs — home / stack / plus / grid / clock / search / chevron / upload / check / camera / sparkle / settings / dot / arrow / trash / image / target / chart / download / tweaks. Replace with whatever icon library the Blazor app uses (or keep as inline Razor components).

## Interactions & Behavior
- All nav items reuse existing routes: `/dashboard`, `/collection`, `/submissions`, `/submissions/new`, `/submissions/{id}`
- New Submission wizard already matches the existing 4-step state machine; behavior is identical — only the rendering changes
- Polling behavior for image analysis should stay; the visual = accent-soft alert with spinning ring instead of a Bootstrap info banner
- Record actual grade form = inline expansion within the detail page, not a modal
- Live estimate sidebar on Step 3 recomputes from sliders on every change — in Blazor this is a simple `@bind:event="oninput"` + computed property

## State Management
No changes to backend services. State that was local component state in Razor stays local:
- `_currentStep`, `_selectedCard`, `_inputMethod`, `_scores`, `_notes`, `_frontUploaded`, `_backUploaded`, `_imageDerivedScores`, `_isWaitingForAnalysis` — keep as-is, re-render with new markup.

## Assets
- **Fonts**: Google Fonts — Inter Tight, Inter, JetBrains Mono, Fraunces (weights 400/500/600, 500/600 for Fraunces). Bundle into `wwwroot/fonts/` if offline use is needed.
- **Icons**: inline SVG strokes (no icon library required). Copy from `components/shell.jsx`.
- **Images**: none — CardArt is fully CSS/DOM. Actual card images come from the existing `card.ImageUrl` / `image.ImageUrl` fields.

## Tweaks / Variants
The prototype exposes an accent swatch picker (mustard / indigo / rose / sage). In production this can be a user preference stored on the Identity user, or removed entirely — mustard is the default recommendation.

## Files in this bundle
```
design_handoff_card_grader/
├── README.md                          (this file)
├── index.html                         (prototype entry)
├── styles.css                         (design tokens + all component CSS — authoritative)
├── data.js                            (sample data model)
└── components/
    ├── shell.jsx                      (Sidebar, Topbar, CardArt, GradeBadge, Icon)
    ├── dashboard.jsx                  (Overview)
    ├── collection.jsx                 (Collection grid + list)
    ├── new_submission.jsx             (4-step wizard + stepper)
    └── submission_detail.jsx          (Hero grade, estimates, record form)
```

## Notes for the implementing developer
- **Do not recreate** any specific grading company's branded UI. "PSA / BGS / CGC / SGC" appear only as text labels against an original visual system.
- Tabular numerals everywhere a grade or percentage appears — both `font-family: JetBrains Mono` and `font-variant-numeric: tabular-nums` matter.
- The sidebar-layout shell replaces `NavMenu.razor`'s top-bar arrangement; `MainLayout.razor` should switch from `<div class="page">` with top-nav to `<div class="app">` with the 240px grid.
- Prefer CSS Grid + flex over Bootstrap's row/col where the design calls for non-12-column layouts (dashboard stat row = `grid-template-columns: repeat(4, 1fr)`).
