# Wasnie Design System

Stripe-quality component library for the Wasnie SPM platform. All primitives live in `src/app/shared/ui/`. The single source of truth for tokens is `src/styles.scss`.

---

## Surface elevation system

Wasnie uses a 4-level surface elevation system to convey hierarchy:

| Level | Token | Light | Dark | Usage |
|---|---|---|---|---|
| 0 | `--color-bg-page` | `#f6f8fb` | `#11161f` | Page canvas — set on `.shell__content` |
| 1 | `--color-bg-surface-sunken` | `#f1f5f9` | `#0a0e15` | Inputs, internal dividers, sunken areas |
| 2 | `--color-bg-surface` | `#ffffff` | `#161c28` | Cards, tables — primary content containers |
| 2b | `--color-bg-surface-deep` | `#e8ecf2` | `#141b2a` | Outer container cards that hold other surface elements (tables, sub-cards). Sits visually below `--color-bg-surface-raised` so contained elements pop. |
| 3 | `--color-bg-surface-raised` | `#f8fafc` | `#1d2432` | Modals, select dropdowns, table headers, hover states |

### Rules

- **Never** use `--color-bg-base` for the page background — that's for the raw HTML root; use `--color-bg-page`
- **Never** apply `--color-bg-page` to a card or modal
- **Cards and tables** use `--color-bg-surface` with `border: 1px solid var(--color-border-default)` + `box-shadow: var(--shadow-card)`
- **Inputs are sunken** relative to their containing card — always use `--color-bg-surface-sunken`
- **Modals and dropdowns** use `--color-bg-surface-raised` — in dark themes this makes them visibly higher; in light themes the shadow conveys elevation
- **Table headers** inside card-like containers use `--color-bg-surface-sunken` for a sunken stripe

### Named elevation shadows

| Token | Maps to | Usage |
|---|---|---|
| `--shadow-card` | `--shadow-sm` | Cards, ws-table wrappers |
| `--shadow-modal` | `--shadow-xl` | Modal dialogs |
| `--shadow-dropdown` | `--shadow-popover` | Select dropdowns, popovers |

### Theme-specific notes

- **Light theme:** elevation is primarily conveyed by background (`#f6f8fb` page → `#ffffff` card) plus shadow
- **Dark theme:** elevation is conveyed by luminance steps (deeper = darker); shadows are less effective
- **Soft theme:** same pattern as light, with warm tan palette (`#f3eee2` page → `#fffaf0` card)

### Forbidden patterns

- Hardcoded color values for backgrounds in component SCSS
- Cards visually identical to page background
- Using `--color-bg-page` or `--color-bg-base` inside cards/modals
- Using `--color-bg-surface` for inputs (they should be sunken, not same level as card)

---

## Tokens

### Radii
| Token | Value |
|-------|-------|
| `--radius-xs` | 4px |
| `--radius-sm` | 6px |
| `--radius-md` | 8px |
| `--radius-lg` | 10px |
| `--radius-xl` | 14px |
| `--radius-full` | 9999px |

### Spacing (4 px base)
`--space-0` 0 · `--space-1` 4 · `--space-2` 8 · `--space-3` 12 · `--space-4` 16 · `--space-5` 20 · `--space-6` 24 · `--space-8` 32 · `--space-10` 40 · `--space-12` 48 · `--space-16` 64 · `--space-20` 80

### Control heights
`--height-control-sm` 28px · `--height-control-md` 32px · `--height-control-lg` 38px

### Type scale
`--font-size-11` through `--font-size-32` (11, 12, 13, 14, 15, 16, 18, 20, 24, 28, 32 px)  
`--font-mono` — monospace stack

### Transitions
`--transition-fast` 100ms · `--transition-base` 150ms · `--transition-normal` 200ms · `--transition-slow` 250ms

### Shadows
`--shadow-sm` `--shadow-md` `--shadow-lg` `--shadow-xl` `--shadow-popover` `--shadow-focus`

---

## Color tokens (all three themes)

Themes are applied via `data-theme="light|soft|dark"` on `<html>`. Three themes are fully defined: **light** (`:root`), **soft** (`[data-theme="soft"]`), **dark** (`[data-theme="dark"]`).

### Surfaces
| Token | Purpose |
|-------|---------|
| `--color-bg-base` | Page background |
| `--color-bg-canvas` | App canvas (behind cards) |
| `--color-bg-surface` | Card / panel background |
| `--color-bg-surface-raised` | Slightly elevated surface |
| `--color-bg-surface-hover` | Hover highlight |
| `--color-bg-surface-sunken` | Sunken / inset areas |
| `--color-bg-overlay` | Modal/drawer backdrop |

### Text
`--color-text-primary` · `--color-text-secondary` · `--color-text-tertiary` · `--color-text-placeholder` · `--color-text-inverse` · `--color-text-link` · `--color-text-link-hover`

### Borders
`--color-border-subtle` · `--color-border-default` · `--color-border-strong` · `--color-border-focus`

### Brand
`--color-brand` · `--color-brand-hover` · `--color-brand-subtle` · `--color-brand-contrast`

### Semantic
Each semantic color has three variants: base, `-bg`, `-border`.

| Name | Base token |
|------|-----------|
| Success | `--color-success` |
| Warning | `--color-warning` |
| Danger | `--color-danger` |
| Info | `--color-info` |

### Gradients / Special
`--gradient-brand` · `--gradient-cta` · `--gradient-surface-soft` · `--focus-ring`

> **Rule:** When adding a new color token, define it in all three themes and update this document.

---

## Primitives

### WsButton `<ws-button>`
Inputs: `variant` (primary | secondary | ghost | danger | link) · `size` (sm | md | lg) · `type` (button | submit | reset) · `loading` · `disabled` · `fullWidth` · `iconOnly` · `routerLink`

```html
<ws-button variant="primary" [loading]="saving()">Save</ws-button>
<ws-button variant="ghost" size="sm" [iconOnly]="true"><app-icon name="plus" /></ws-button>
```

### WsInput `<ws-input>`
CVA. Inputs: `type` (text | email | password | number | search) · `label` · `placeholder` · `error` (translation key) · `prefixIcon` · `suffixText` · `clearable` · `inputId`  
Outputs: `valueChange` (for use without reactive forms)

```html
<ws-input formControlName="email" label="Email" type="email" [error]="fieldError('email')" />
```

### WsTextarea `<ws-textarea>`
CVA. The multi-line counterpart of WsInput — use it for anything that takes a paragraph (chat message, note, description, pasted output). WsInput is single-line by construction; do NOT reach for a native `<textarea>`.

Inputs: `label` · `placeholder` · `error` (translation key) · `inputId` · `maxlength` · `submitOnEnter` (default **true**) · `minHeight` (px, default 56) · `maxHeight` (px, default 200)
Outputs: `valueChange` · `submitted` (fires on Enter when `submitOnEnter`, carries the value)

**Keyboard.** With `submitOnEnter` (the default): **Enter submits, Shift+Enter breaks the line** — the conversational contract. Set `[submitOnEnter]="false"` for an ordinary textarea where Enter just breaks the line. Enter mid-IME-composition never submits.

**Autosize.** Grows with the content from `minHeight` up to `maxHeight`, then scrolls internally instead of growing. The rule is the exported pure function `computeAutosize(contentHeight, minHeight, maxHeight)` — test growth behaviour against it, not against measured pixels (headless does not lay out).

```html
<!-- Chat composer: Enter sends -->
<ws-textarea [placeholder]="'ASSISTANT.COMPOSER_PLACEHOLDER' | translate" (submitted)="send()" />

<!-- Ordinary notes field: Enter breaks the line -->
<ws-textarea formControlName="notes" label="Notes" [submitOnEnter]="false" [maxHeight]="320" />
```

★ Its chrome is token-for-token WsInput's: `--color-bg-surface`, `--color-border-default`, `--color-border-focus` + `--shadow-focus`, `--color-danger`, and the same `--error` / `--disabled` / `__field--focused` modifiers. **The focus treatment in this design system is a border colour plus `box-shadow: var(--shadow-focus)` — NOT a Tailwind ring utility.** A parity spec asserts the two resolve to the same computed border, background and focus shadow, so a hand-written colour here fails the suite.

### WsSelect `<ws-select>`
CVA. Options type: `SelectOption { value: string; label: string; disabled?: boolean }`. Labels are run through `| translate` automatically.  
Inputs: `options` · `label` · `placeholder` · `searchable` · `error` · `searchFn` · `initialOption`

```html
<!-- Client-side (static list): -->
<ws-select formControlName="currency" label="Currency" [options]="currencyOptions" />

<!-- Async mode (server-side typeahead, large datasets): -->
<ws-select formControlName="payeeId" label="Payee" [searchFn]="payeeSearchFn" [initialOption]="preselectedPayee()" />
```

#### Async mode

Pass a `searchFn` input (`(query: string) => Observable<SelectOption[]>`) instead of `[options]`. The component handles debounce (300 ms), in-flight cancellation (`switchMap`), loading indicator, and empty state automatically.

- The search input appears automatically — do not also set `[searchable]="true"`.
- On dropdown open, the component fires an empty-string query to pre-populate the first server page.
- `[initialOption]` (`SelectOption | null`) — supply the pre-known label when the form is patched with a value that is not yet in `asyncOptions` (edit mode, query-param preselection). The component falls back to `initialOption` when it cannot find the value in `asyncOptions`.
- Status filtering in async mode: the backend `search` param is the primary filter. Client-side status filtering is not applied. Add a `filters` param to `PaginationParams` when backend filtering by status is required.

```typescript
// In the component class:
readonly payeeSearchFn = (q: string): Observable<SelectOption[]> =>
  this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
    map(r => r.items.map(p => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` })))
  );
```

### WsDatePicker `<ws-date-picker>` — canonical pattern

CVA. Emits ISO date string (`"yyyy-MM-dd"`). Inputs: `label` · `minDate` · `maxDate` · `error`

#### Anatomy

The date picker has three layers:

1. **Trigger** — styled like `WsInput` (calendar icon left, chevron right). Accepts direct text input in locale or ISO format.
2. **Calendar popover** — anchored directly below (or above) the trigger, contained within the nearest parent panel or viewport.
3. **Month/Year selector** — secondary view opened by clicking the "MMMM yyyy ▾" header. Replaces the calendar grid to let users jump years without linear navigation.

#### Calendar visual specifications (locked)

| Property | Value |
|----------|-------|
| Background | `var(--color-bg-surface)` |
| Border | `1px solid var(--color-border-default)` |
| Border-radius | `var(--radius-md)` |
| Shadow | `var(--shadow-popover)` |
| Padding | `var(--space-4)` |

Header row: **prev arrow · "Month Year ▾" button · next arrow**. Clicking the month+year label opens the year/month selector.  
Footer row: **Today** · **Clear** buttons.

#### Month/Year selector — non-negotiable

Users must be able to jump to any year via the selector (12-year grid × month grid). Linear month-by-month navigation alone is FORBIDDEN — hire dates, plan periods, and termination dates regularly span years.

**3-click year jump (locked):** open selector → click year → click month → returns to date grid.

```
┌──────────────────────────────────────────┐
│  ◀         2016 – 2027                ▶  │
├──────────────────────────────────────────┤
│  2016  2017  2018  2019                  │
│  2020  2021  2022  2023                  │  ← click year to highlight
│  2024  2025 [2026] 2027                  │
├──────────────────────────────────────────┤
│  Jan   Feb   Mar   Apr                   │
│  May   Jun   Jul   Aug                   │  ← click month to apply and return
│  Sep   Oct   Nov   Dec                   │
├──────────────────────────────────────────┤
│                              [Back]      │
└──────────────────────────────────────────┘
```

#### Containment rules

Same algorithm as `WsSelect`: detects nearest `.ws-modal__dialog` ancestor, compares available space below and above, opens upward if needed. Never extends past the panel's `overflow: hidden` boundary.

#### Direct typing

The trigger `<input>` accepts date text directly. Accepted formats (per active locale):
- ISO: `2026-05-12`
- Locale long: `May 12, 2026` (EN) · `12/05/2026` (ES)

On blur or Enter: parses and applies. Invalid input reverts to the previous value with a brief red border flash.

#### Keyboard navigation

| Key | Action |
|-----|--------|
| `←` `→` | ±1 day |
| `↑` `↓` | ±1 week |
| `PageUp` / `PageDown` | ±1 month |
| `Shift+PageUp` / `Shift+PageDown` | ±1 year |
| `Enter` | select focused date / confirm typed input |
| `Esc` | close calendar, revert typing |

#### Locale support

Month names, weekday abbreviations, first day of week, and trigger display format all adapt to the active i18n locale (EN/ES/PL). Powered by `date-fns` locales — never roll custom date math.

#### Forbidden patterns

- Calendar without a visual container (background, border, shadow)
- Calendar without month/year header with clickable label
- Linear-only navigation (no year selector)
- Calendar that visually escapes its parent panel
- Custom date arithmetic — use `date-fns`
- Missing keyboard support
- `position: fixed` calendar inside a modal

### WsDateRangePicker `<ws-date-range-picker>`
CVA. Emits `DateRange { start: string | null; end: string | null }`. Dual-month calendar, auto-swap.  
Inputs: `label`

### WsCard `<ws-card>`
Inputs: `variant` (default | flat | interactive) · `padding` (none | sm | md | lg) · `accent` (none | brand | success | warning | danger | info)

```html
<ws-card variant="interactive" accent="brand" padding="md">…</ws-card>
```

### WsBadge `<ws-badge>`
Inputs: `variant` (neutral | brand | success | warning | danger | info) · `size` (sm | md) · `dot`

```html
<ws-badge variant="success" [dot]="true">Active</ws-badge>
```

### WsModal `<ws-modal>`
Two-way: `[(isOpen)]`. Slots: body (default) · `[slot=footer]`  
Inputs: `title` (required for proper heading styling) · `description` · `size` (sm | md | lg | xl) · `closable` · `closeOnBackdrop`  
Outputs: `closed`  
Focus trap + body-scroll lock built-in. Height is capped to the viewport and the body scrolls, so the
footer is always reachable — see [Height and scroll](#height-and-scroll-locked). There is no
`scrollable` input and none is needed.

**Always use `[title]` input — never `slot="header"`**. The `title` input renders with `.ws-modal__title` class (18px weight-600). Content projected via `slot="header"` bypasses this class and produces incorrect styling.

### WsConfirmationModal `<ws-confirmation-modal>`
Single-tag helper for all confirmation dialogs. Two-way: `[(isOpen)]`.  
Inputs: `title` (required) · `message` · `confirmLabel` (required) · `cancelLabel` · `variant` (default | danger) · `loading`  
Outputs: `confirmed` · `cancelled`  
Supports optional content projection for confirmation dialogs that include a field (e.g. a termination date).

```html
<!-- Simple confirmation -->
<ws-confirmation-modal
  [(isOpen)]="archiveOpen"
  [title]="'PLANS.CONFIRM_ARCHIVE_TITLE' | translate"
  [message]="'PLANS.CONFIRM_ARCHIVE_MSG' | translate"
  [confirmLabel]="'PLANS.ACTION_ARCHIVE' | translate"
  [cancelLabel]="'COMMON.CANCEL' | translate"
  variant="danger"
  [loading]="archiveSaving()"
  (confirmed)="onConfirmArchive()"
  (cancelled)="archiveOpen.set(false)"
/>

<!-- With projected field -->
<ws-confirmation-modal [title]="..." variant="danger" (confirmed)="onConfirm()">
  <ws-date-picker formControlName="terminationDate" [label]="'PAYEES.FIELD_TERMINATION_DATE'" />
</ws-confirmation-modal>
```

### WsPopover `<ws-popover>`
Two-way: `[(isOpen)]`. Slots: `[slot=trigger]` · panel (default)  
Inputs: `placement` (bottom | top | bottom-end | top-end) · `gap`

### WsSegmentedControl `<ws-segmented-control>`
Two-way: `[(value)]`. Options type: `SegOption { value: string; label: string }`.  
Inputs: `options` · `translateLabels` (default true)

### WsTooltip `[wsTooltip]`
Directive. 300ms delay, appends to `document.body`.  
Inputs: `wsTooltip` (text) · `tooltipPlacement` (top | bottom | left | right, default top)

```html
<ws-button wsTooltip="Delete this item" tooltipPlacement="bottom">Delete</ws-button>
```

### WsTable / WsTablePagination / WsTableEmpty
`<ws-table>` — `ViewEncapsulation.None` wrapper; project a `<table>` inside.  
`<ws-table-pagination>` — Inputs: `page` · `totalCount` · `pageSize` (default 20). Outputs: `pageChange`  
`<ws-table-empty>` — Inputs: `titleKey` · `descKey`

### WsEmptyState `<ws-empty-state>`
Inputs: `illustration` (plans-empty | payees-empty | transactions-empty | payouts-empty | quotas-empty | assignments-empty) · `icon` · `titleKey` (required) · `descKey` · `actionKey` · `actionRoute` · `secondaryActionKey` · `secondaryActionRoute`  
Outputs: `actionClick`

### WsToast / WsToastService
Service: `WsToastService.show(message: string, type: WsToastType)` · `dismiss(id)`  
Types: `success | error | warning | info`  
Container: `<ws-toast-container />` — add once in `app-shell.component.html`

### WsPageHeader `<ws-page-header>`
Inputs: `title` (required) · `subtitle` · `backRoute` · `backLabel`  
Slots: `[slot=actions]` · `[slot=kpis]`

```html
<ws-page-header title="Plans" subtitle="Manage compensation plans">
  <div slot="actions"><ws-button variant="primary">New Plan</ws-button></div>
</ws-page-header>
```

---

## Icon alignment with text

Icons rendered inline with text (in buttons, links, nav items, badges, etc.) must be vertically centered using the following rules:

1. The `IconComponent` host applies `display: inline-flex`, `line-height: 0`, and `vertical-align: middle` — the `line-height: 0` prevents the host from contributing an unwanted line-box gap; `vertical-align: middle` aligns it correctly in inline flow contexts.
2. The internal SVG has `display: block` — removes the default SVG baseline alignment that causes upward shift.
3. The parent container (button, link, nav item) uses `display: flex` or `display: inline-flex` with `align-items: center` and `gap`. In flex contexts, `vertical-align` is ignored and `align-items: center` takes over.
4. `WsButton`'s `.ws-btn__content` wrapper uses `display: contents` — this makes it invisible to flex layout, so the icon and text are direct flex children of the button and inherit `gap-2` and `align-items: center` properly.

Never set icon width/height directly in pixels in usage code. Pass the `size` input to `IconComponent`; do not set width/height elsewhere.

Never use raw `<svg>` tags in templates. Always use `<app-icon name="...">`. This guarantees consistent alignment.

---

## Interactive text weight

All interactive text in the application uses **font-weight 600 (semibold)**. This is non-negotiable.

Applies to:
- Every button (all variants)
- Every link
- Sidebar nav items
- Topbar actions
- Tab labels
- Segmented control labels
- Action menu items
- Dropdown menu items
- Pagination controls

Does NOT apply to:
- Body paragraphs (400 regular)
- Form field labels (500 medium)
- Headings (300–600 depending on level)
- Status badges (500 medium)
- Tabular numeric data (500 medium)
- Section group labels in nav (500 medium with uppercase tracking)

The rationale: interactive elements need to feel "clickable" and present. Semibold gives them weight without screaming. It also creates a consistent rhythm across the entire UI — when the user scans a page, all clickable elements look the same weight.

---

## Border tokens — slate-toned

All borders use slate-toned values, never neutral white or pure black. This applies to all three themes:

- **Light theme:** slate-500 (`#64748B`) at 10/18/30% for subtle/default/strong
- **Dark theme:** slate-400 (`#94A3B8`) at 8/14/24% for subtle/default/strong
- **Soft theme:** warm earth-toned (`#7A6646`) at 8/16/28% to maintain the soft theme's identity

Never use:
- Pure white borders (`rgba(255,255,255,...)`) in dark mode
- Pure black borders (`rgba(0,0,0,...)`) in light mode
- Tailwind's default `gray-*` borders directly

Slate tones harmonize with both navy backgrounds (dark theme) and cool white surfaces (light theme), creating a more premium feel than neutral grays.

---

## Forbidden border patterns

Borders in component SCSS must ALWAYS use a CSS variable token:

- `var(--color-border-subtle)` — low-contrast dividers (between rows, between sections within a card, `<hr>` elements)
- `var(--color-border-default)` — standard component borders (cards, inputs, popovers)
- `var(--color-border-strong)` — emphasized borders (focused state without color, large section dividers)
- `var(--color-border-focus)` — active focus state (combined with focus ring)
- Semantic tokens (`--color-danger`, `--color-success`, etc.) — for state-specific borders

**Forbidden:**
- Hex codes (`#fff`, `#000`, any `#rrggbb`)
- Raw rgba values (`rgba(255,255,255,...)`, `rgba(0,0,0,...)`)
- Tailwind palette utilities (`border-gray-200`, `border-slate-300`)

**Critical:** `border-color` is NOT an inherited CSS property. Setting `border-color` on a parent element (e.g., to fix `divide-y` colors) does NOT propagate to children's border colors. Always set border tokens directly on the element that renders the border.

If a new semantic border type is genuinely needed, add a CSS variable in all three themes and document it here.

---

## Forbidden patterns

| # | Rule |
|---|------|
| 1 | No hex codes in component SCSS — tokens only |
| 2 | No raw Tailwind palette utilities (`text-blue-600`) — semantic utilities only |
| 3 | No native `<select>` or `<input type="date">` — use `ws-select` / `ws-date-picker` |
| 4 | No invented radii / paddings / gaps / font-sizes outside the defined scales |
| 5 | No inline styles in templates |
| 6 | No `confirm()` or browser dialogs — use `<ws-modal>` or `WsToastService` |
| 7 | No ad-hoc table styling — use `<ws-table>` |
| 8 | No new shadows beyond the defined set |
| 9 | No new color tokens without updating all three themes **and** this document |
| 10 | No copy-pasted logic — if a pattern repeats twice, make a primitive |

---

## Modal anatomy — canonical pattern (locked)

Every modal in Wasnie follows the same visual anatomy. Inconsistency between modals is a defect.

### Sizes — pick by content

- `sm` — 400px — confirmation dialogs
- `md` — 560px — short forms (≤ 4 fields)
- `lg` — 720px — standard forms (5–10 fields), **default for Create/Edit modals**
- `xl` — 960px — wizards, complex layouts

Size is a **width** choice. It does nothing for vertical overflow, so "bump the size up" is not a
remedy for a modal that is too tall — see [Height and scroll](#height-and-scroll-locked) below.

### Visual specifications (locked)

- Title: `var(--font-size-18)` weight 600 — always via `[title]` input on `WsModal`, never via `slot="header"`
- Subtitle: `var(--font-size-14)`, secondary color — via `[description]` input
- Header divider: always present, `1px solid var(--color-border-subtle)`
- Footer divider: present when `[slot=footer]` exists, `1px solid var(--color-border-subtle)`
- Panel background: `var(--color-bg-surface)`
- Panel border: `1px solid var(--color-border-default)`
- Panel border-radius: `var(--radius-lg)`
- Panel shadow: `var(--shadow-xl)`
- Backdrop: `var(--color-bg-overlay)`
- Panel isolation: `isolation: isolate`

### Height and scroll (locked)

**Three zones, always.** `WsModal` is a flex column with a hard ceiling of `100dvh − 64px`
(`100vh` fallback):

| Zone | Behaviour |
|---|---|
| Header | `flex-shrink: 0` — pinned |
| Body | `flex: 1 1 auto` + **`min-height: 0`** + `overflow-y: auto` — the only zone that gives |
| Footer | `flex-shrink: 0` — pinned, so Save/Cancel are **always** reachable |

`min-height: 0` is load-bearing and must not be removed. A flex item defaults to
`min-height: auto` and refuses to shrink below its content, so without it a long body pushes the
footer past the dialog's `overflow: hidden` edge: **the buttons are clipped away and nothing
scrolls to them.** That shipped once (bulk-approve on Payouts, 2026-07-30) and left users unable to
either confirm or cancel. `ws-modal.component.spec.ts` asserts the geometry, not the declarations.

`overflow-y: auto` scrolls only when it must, so short modals are visually unchanged and show no
scrollbar. `overscroll-behavior: contain` stops the page behind the modal scrolling at the end of
the body.

**Scrollbar styling** comes from the shared `ws-scroll-thin` class on the body — never native
browser chrome: width 6px, thumb `var(--color-border-strong)` (hover `var(--color-text-tertiary)`),
transparent track.

*(Superseded 2026-07-30: the body used to be `overflow: visible` with an opt-in
`--scrollable` modifier. The modifier was never wired to an input — it was unreachable dead CSS —
and the visible default is what caused the trap above. The reason for `visible` was that positioned
children would be clipped, which no longer applies: see the next section.)*

### Dropdowns and date pickers inside modals

`WsSelect` and `WsDatePicker` detect available viewport space on open and flip upward automatically when there is insufficient space below. No special configuration required.

**Both always render their popover with `position: fixed`**, measured from the trigger's
`getBoundingClientRect()` (`ws-select.component.ts:241-251`, `ws-date-picker.component.ts:284-290`).
That is what lets them escape ANY overflow-constrained ancestor — the modal body, a filter panel, a
card, a page scroll container — and it is why the modal body may now be a scroll container without
clipping them. Both also attach `scroll` listeners in the **capture** phase, so they reposition (or
close) when a nested scroller such as the modal body moves underneath them.

Consequently a form wrapper no longer needs padding `min-height` to keep a calendar inside the
panel; the popover is not inside the panel's clipping region at all.

### Form-inside-modal pattern

When a form component is the modal body and provides its own Cancel/Save buttons, no `[slot=footer]` is used. The form's button row must visually match a footer (top border, right-aligned, same padding). See `payee-form.component.scss` `.form-actions`.

### Confirmation flows

Use `WsConfirmationModal` — never inline `WsModal` with custom buttons for a plain yes/no confirmation. Delete `ModalService.confirm()` call sites.

### Forbidden

- `slot="header"` on any `ws-modal` — always use `[title]` input
- Per-modal custom padding overrides
- Per-modal custom title font-sizes
- Modals without a header divider
- Inline backdrop colors (always use `var(--color-bg-overlay)`)
- Modal widths outside the four standard sizes
- Forms with 6+ fields rendered in `md` size (use `lg`)
- Native browser scrollbars inside modal bodies
- Validation errors visible before user interaction

---

## Form components — single shared component rule

When the same form fields appear in both a Create page **and** an Edit modal (or Edit page), they **must** be extracted into a single shared `*FormComponent`. Duplicate inline form implementations are forbidden.

**Rule:** One form component per entity. Create and Edit both use it.

**Pattern:**
```typescript
// app-payee-form — the canonical example
@Component({ selector: 'app-payee-form', ... })
export class PayeeFormComponent {
  readonly payee = input<Payee | null>(null);   // null = create mode
  readonly saved = output<Payee>();
  readonly cancelled = output<void>();

  readonly isEditMode = computed(() => this.payee() !== null);

  constructor() {
    effect(() => {
      const p = this.payee();
      if (p) this.form.patchValue({ /* all fields */ });
    });
  }
}
```

```html
<!-- Create page -->
<app-payee-form [payee]="null" (saved)="onSaved($event)" (cancelled)="onCancelled()" />

<!-- Edit modal -->
<app-payee-form [payee]="store.selectedPayee()" (saved)="onEditSaved($event)" (cancelled)="editModalOpen.set(false)" />
```

**Why this rule exists:** The Edit modal for Payees previously had a hand-rolled inline form that diverged from the Create page — it was missing the Manager field entirely. Duplicate form implementations double the bug surface: validation rules, field lists, and default values all drift independently.

**Checklist when adding a new entity with Create + Edit:**
1. Create a `form/` subdirectory next to `create/` and `detail/`
2. Put the shared `*FormComponent` there
3. Create page: thin wrapper — just router navigation in `onSaved()`
4. Detail component: imports `*FormComponent`, passes `store.selectedEntity()` as input

---

---

## Form layout — grid pattern (canonical)

Forms with 4 or more fields use a two-column CSS grid layout. Single-column stacks are reserved for:

- Forms with 3 fields or fewer
- Forms in narrow contexts (sidebar, narrow modal)
- Mobile/responsive breakpoints below 640px

### Grid rules

```scss
.ws-form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  column-gap: var(--space-5);
  row-gap: var(--space-4);

  &__full {
    grid-column: 1 / -1;
  }
}

@media (max-width: 640px) {
  .ws-form-grid {
    grid-template-columns: 1fr;

    &__full {
      grid-column: 1;
    }
  }
}
```

### Field placement semantics

- **Primary identification field** (name, title): full row — `ws-form-grid__full`
- **Compact related pairs** (code + email, role + hire date): share a row — no modifier needed
- **Long-form fields** (textarea, notes, descriptions): full row — `ws-form-grid__full`
- **Relationship fields** (dropdowns that link to another entity — payee, plan, manager): full row — `ws-form-grid__full`, gives breathing room for the searchable dropdown
- **Currency-amount pairs**: nested `amount-pair` grid with `2fr 1fr` inside the right column, paired with measurement type in the left column

### Amount + currency nested layout

```scss
.amount-pair {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: var(--space-3);
  align-items: start;
}
```

```html
<!-- Col 1: measurement type | Col 2: amount + currency pair -->
<ws-select formControlName="measurementType" label="..." />
<div class="amount-pair">
  <ws-input type="number" formControlName="amount" label="..." />
  <ws-select formControlName="currency" [options]="currencies" label="..." />
</div>
```

### Payee form layout (reference implementation)

```
┌─────────────────────────────────────────────────────────┐
│  Full name *                                            │ full row
│  Employee code *           Email *                      │ paired
│  Role                      Hire date *                  │ paired
│  Manager                                                │ full row
└─────────────────────────────────────────────────────────┘
```

### Quota create layout

```
┌─────────────────────────────────────────────────────────┐
│  Payee *                                                │ full row
│  Plan *                                                 │ full row
│  Measurement type *        [Amount *] [Currency ▾]      │ col pair + nested amount-pair
│  Period (date range)                                    │ full row
│  Notes                                                  │ full row
└─────────────────────────────────────────────────────────┘
```

### Forbidden

- Single-column layouts for forms with 5+ fields
- Mixing column counts within the same form
- Custom row/column spacing per form
- Forcing related fields into different rows for aesthetic reasons

---

## Popovers and dropdowns — containment rules

`WsSelect`, `WsDatePicker`, and any popover-based component must respect the visual boundary of their containing modal panel.

### Placement algorithm

When a popover opens it must detect its nearest `.ws-modal__dialog` ancestor. If found, that element's bounding rect is the container; otherwise the viewport is the container.

```ts
const modalDialog = triggerEl.closest('.ws-modal__dialog') as HTMLElement | null;
const containerBottom = modalDialog
  ? modalDialog.getBoundingClientRect().bottom - 8
  : window.innerHeight - 8;
const containerTop = modalDialog
  ? modalDialog.getBoundingClientRect().top + 8
  : 8;

const spaceBelow = containerBottom - triggerRect.bottom;
const spaceAbove = triggerRect.top - containerTop;

if (spaceBelow >= estimatedHeight) {
  placement = 'below';          // normal
} else if (spaceAbove >= estimatedHeight) {
  placement = 'above';          // flip upward
} else {
  placement = spaceAbove > spaceBelow ? 'above-scroll' : 'below-scroll';
  maxListHeight = Math.max(spaceBelow, spaceAbove) - 8;  // constrained scroll
}
```

### CSS implementation

- **Below (default):** `top: calc(100% + var(--space-1)); bottom: auto`
- **Above:** `top: auto; bottom: calc(100% + var(--space-1))` — via `--upward` BEM modifier
- **Constrained scroll:** inline `max-height` on the list element overrides the default 240px cap

### Modal panel requirements

```scss
.ws-modal__dialog {
  isolation: isolate;
  overflow: hidden;         // the panel never spills; the body scrolls instead
  max-height: calc(100dvh - 64px);
}

.ws-modal__body {
  flex: 1 1 auto;
  min-height: 0;            // REQUIRED — without it the footer is pushed out and clipped away
  overflow-y: auto;         // scrolls only when the content exceeds the panel
}
```

Popovers are unaffected by the body being a scroll container: they are `position: fixed` and are not
laid out inside it. See [Height and scroll](#height-and-scroll-locked).

`overflow: visible` on the body is load-bearing. Changing it to `auto` recreates the clipping problem — the body becomes a scroll container that clips absolutely-positioned children at the body's rendered height, even when no scroll is visible.

### Forbidden

- Removing `min-height: 0` from `.ws-modal__body`, or giving the header/footer a shrink factor — either one puts Save/Cancel off screen again
- Giving `.ws-modal__dialog` a height that is not bounded by the viewport
- Per-screen height/scroll patches on an individual modal instead of fixing `WsModal`

*(No longer forbidden, and now the required behaviour: `position: fixed` popovers inside modals.
They were banned when they were expected to detach from the modal stack; `WsSelect` and
`WsDatePicker` both position against the trigger rect, track scroll in the capture phase and sit at
`z-index: 1100`, above the dialog. This is what makes a scrollable modal body possible.)*

---

## Plan rule display — canonical pattern

Plan rules in the Plan Detail page are rendered as expanded cards showing all components inline. No collapse/expand — financial data must always be visible.

### Card structure

1. **Header row**: `#N` sort order + rule name + Edit / View button. Separated from body by a `1px solid var(--color-border-subtle)` divider.
2. **Sections** (in order):
   - Measurement (always present)
   - Rate Table (always present)
   - Trigger (if present)
   - Modifier (if present)
   - Cap (if present)
   - Floor (if present — displayed even when amount is 0)

### Card visual spec

- Background: `var(--color-bg-surface-raised)`
- Border: `1px solid var(--color-border-default)`
- Border-radius: `var(--radius-lg)`
- Padding: `var(--space-5)`
- Shadow: `var(--shadow-sm)`
- Between cards: `gap: var(--space-6)`

### Section anatomy

- **Label**: `display: flex; align-items: center; gap: 6px` — Lucide icon at 14px + uppercase text
  - Font: `var(--font-size-11)`, weight 600, letter-spacing `0.05em`, color `var(--color-text-tertiary)`
  - Subtitle after em dash uses same styling
- **Content**: `var(--font-size-13)`, weight 400, color `var(--color-text-primary)`, line-height 1.6
- Section gap (between label + content): `var(--space-2)`
- Between sections: `var(--space-5)`

### Section icons (locked — do not substitute)

| Section | Icon name |
|---|---|
| Measurement | `activity` |
| Rate Table | `layers` |
| Trigger | `zap` |
| Modifier | `trend-up` |
| Cap | `arrow-up-to-line` |
| Floor | `arrow-down-to-line` |

### Tier table spec

Wrapped in `.rule-tier-wrap` (border + border-radius + overflow:hidden), table fills the container:

- Container max-width: 440px
- Header row (`<thead>`): `background: var(--color-bg-surface-sunken)`, `border-bottom: 1px solid var(--color-border-subtle)`
- `<th>`: `var(--font-size-11)`, weight 600, uppercase, letter-spacing `0.05em`, color `var(--color-text-secondary)`, padding `var(--space-3)`
- `<td>`: `var(--font-size-13)`, mono font, tabular-nums, padding `var(--space-3)`
- Row borders: `border-bottom: 1px solid var(--color-border-subtle)` on all except last
- Row hover: `background: var(--color-bg-surface-hover)`
- Rate values: percentages with exactly 1 decimal place (e.g. `7.5%`, `10.0%`)
- Open-ended boundary: `∞`

### Enum-to-label mapping (locked)

All these mappings are derived from the TypeScript enum names, which mirror the backend. The i18n key pattern is `PLANS.COND_OP_${EnumName.toUpperCase()}`.

#### Condition operators (ConditionOperator enum)

| Value | Enum name | EN label |
|---|---|---|
| 0 | Equal | equals |
| 1 | NotEqual | does not equal |
| 2 | GreaterThan | is greater than |
| 3 | GreaterThanOrEqual | is greater than or equal to |
| 4 | LessThan | is less than |
| 5 | LessThanOrEqual | is less than or equal to |
| 6 | In | is in |
| 7 | NotIn | is not in |

#### Cap scopes (CapScope enum)

| Value | Enum name | EN label |
|---|---|---|
| 0 | PerTransaction | Per Transaction |
| 1 | PerPeriod | Per Period |
| 2 | PerPayeePerPeriod | Per Payee per Period |

#### Modifier types (ModifierType enum)

| Value | Enum name | EN label |
|---|---|---|
| 0 | Accelerator | Accelerator |
| 1 | Multiplier | Multiplier |
| 2 | Spiff | Spiff |

### Formatting rules

- **Tier rates**: `(val * 100).toLocaleString(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 }) + '%'`
- **Modifier factor**: `×N.N` format — multiplication sign prefix, 1–2 decimal places
- **Monetary amounts**: `amount.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 })` + currency code
- **Trigger conditions**: `When **field** operator "**value**"` — field name and value in bold

### Forbidden

- Showing raw enum numbers in the UI
- Showing operator symbols (`=`, `>`, `<`, `≠`, `≥`, `≤`) instead of words
- Tier tables without borders or header differentiation
- Section labels without icons
- Inconsistent spacing between sections
- Hiding the Floor section when amount is 0

---

## Bulk import wizards — canonical pattern

Resources that support bulk import (Payees, Quotas, Assignments, etc.) use a 3-step wizard at `/{resource}/import`:

1. **Upload** — drag-and-drop file selection with format/size validation
2. **Map columns** — interactive mapping with auto-detection where possible
3. **Preview & Import** — full row-level validation with errors/warnings inline before commitment

### Limits

- Max file size: 5 MB (enforced client-side and server-side)
- Max rows per import: 300 (current limit; architecture designed to allow future async expansion)
- Supported formats: CSV, XLSX (UTF-8)

### Architecture rule (non-negotiable)

Backend import flow MUST separate these concerns into distinct services:

1. **FileParserService** — reads CSV/XLSX, returns structured rows (no DB, no validation)
2. **ColumnMappingService** — maps user-selected column names to DTO fields (pure data transformation)
3. **PayeeImportValidationService** — validates a batch of rows (DB reads OK, no writes)
4. **PayeeImportExecutionService** — creates records in a transaction (DB writes only)

This separation enables future migration to async/background job processing without rewriting business logic.

### Five-step variant (async execute)

When the execute endpoint is async (returns 202 + `{ jobId }` instead of a synchronous result), the wizard gains a **Progress** step between Preview and Complete:

1. **Upload** — file selection (same as 3-step)
2. **Map columns** — same as 3-step
3. **Preview & Import** — on submit, calls execute → receives `{ jobId }` → transitions to Progress
4. **Progress** *(new)* — polls `GET /api/jobs/{id}` every 3 seconds; shows indeterminate bar (Pending) or determinate bar (Running); stops polling on `Succeeded`/`Failed` OR on component destroy (zombie-poll prevention via `takeUntilDestroyed`)
5. **Complete** — shows result

**Polling pattern (canonical):**
```typescript
this._polling = timer(0, 3000).pipe(
  takeUntilDestroyed(this.destroyRef),
  switchMap(() => this.service.getJobStatus(jobId).pipe(
    catchError(() => { this.netError.set(true); return of(null); })
  )),
).subscribe(s => {
  if (!s) return;
  this.netError.set(false);
  this.status.set(s);
  if (s.state === 'Succeeded' || s.state === 'Failed') {
    this._polling?.unsubscribe(); // stop on terminal state
    // emit completed or set failure message
  }
});
```

**Progress bar:** implemented as LOCAL CSS in the progress step component — NOT a shared `WsProgressBar` primitive. If ≥2 features need it, elevate to `shared/ui/` in a dedicated design-system WI (§10.3).

**Retry on failure:** goes back to Preview (not Upload/Map). The parsed file and mapping are still valid.

**SessionStorage:** does not persist the `progress` step. On page reload, falls back to the last non-progress step (Preview). The `jobId` is not persisted — a reloaded page cannot resume a running job.

### Three-endpoint API pattern

```
POST /api/imports/{resource}/parse    → returns fileId + headers + sample rows
POST /api/imports/{resource}/validate → returns row-level validation results
POST /api/imports/{resource}/execute  → performs DB writes, returns import result
```

`fileId` is a server-side memory-cache key (15 min TTL). The file is parsed once on `/parse` and referenced by ID on subsequent calls to avoid re-uploading.

### Auto-detection of columns

The mapping step pre-selects likely matches based on header names in all supported languages (EN, ES, PL). The user can always override. Patterns are defined in the step component (see `mapping-step.component.ts`).

### Composed fields

Some fields cannot be mapped to a single column in every HR export format (e.g. Full Name is often split into First Name + Apellido Paterno + Apellido Materno in Latin American exports). These use a **composition builder** instead of a single `ws-select`:

- Selected columns are rendered as dismissible chips (`.name-chip`) inside a `.name-composer` container.
- A secondary `ws-select` lets the user add another column from the remaining unmapped headers.
- When more than one column is selected, a live preview line shows the joined result: `Preview: "Juan García López"`.
- Auto-detection tries a single full-name column first, then falls back to detecting first + last name pairs/triplets across EN/ES/DE/IT/PT/FR header conventions.

**Data flow:** the composed field sends `fullNameColumns: string[]` in the `PayeeImportColumnMapping` DTO. The backend `ComposeFullName` helper joins the columns with a single space and trims. This keeps the server-side row cache intact — no client-side row mutation required.

### Validation severities

- **Error:** blocks import for that row (e.g., duplicate email, invalid date, missing required field)
- **Warning:** allows import but flags for review (e.g., empty optional field, recent hire date, personal email domain)

### Audit trail

Every import attempt (success or failure) creates an immutable `ImportAudit` record with:
- `TenantId`, `ImportedBy`, `StartedAt`, `CompletedAt`
- `ResourceType` ("Payees", "Quotas", "Assignments")
- `TotalRows`, `CreatedCount`, `SkippedCount`, `OriginalFileName`, `Status`

The original file is **never stored** — only metadata. Status values: `"Success"`, `"PartialSuccess"`, `"Failed"`.

---

## Server-side pagination

All list views use server-side pagination. The shared contract lives in `src/app/shared/models/pagination.models.ts`.

### PagedResult\<T\>

The API always returns:
```typescript
interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}
```

### PaginationParams

Sent as HTTP query parameters:
```typescript
interface PaginationParams {
  page: number;
  pageSize: number;
  sortBy?: string;
  sortOrder?: 'asc' | 'desc';
  search?: string;
  filters?: Record<string, string>;  // serialised as filters[key]=value
}
```

### WsPaginationComponent

`<ws-pagination>` renders the page controls row below a `<ws-table>`.

**Inputs**
| Input | Type | Required | Description |
|-------|------|----------|-------------|
| `currentPage` | `number` | yes | 1-based active page |
| `totalPages` | `number` | yes | Total number of pages |
| `totalCount` | `number` | yes | Total record count |
| `pageSize` | `number` | yes | Active page size |
| `pageSizeOptions` | `number[]` | no | Defaults to `[10, 25, 50, 100]` |

**Outputs**
| Output | Type | Description |
|--------|------|-------------|
| `pageChange` | `number` | Emits the new 1-based page number |
| `pageSizeChange` | `number` | Emits the selected page size |

**Usage**
```html
<ws-pagination
  [currentPage]="store.page()"
  [totalPages]="store.pagedResult()!.totalPages"
  [totalCount]="store.pagedResult()!.totalCount"
  [pageSize]="store.pageSize()"
  (pageChange)="goToPage($event)"
  (pageSizeChange)="goToPageSize($event)"
/>
```

### Store pattern

Each list store exposes individual signals and setters. Search is debounced 300 ms via `Subject` + `toSignal`. An `effect()` triggers a server reload whenever page, pageSize, sortBy, sortOrder, search, or status changes.

```typescript
// Setters
store.setSearch(value: string)
store.setStatus(status: T | null)
store.setPage(page: number)
store.setPageSize(size: number)

// State signals
store.page()        // number
store.pageSize()    // number
store.pagedResult() // PagedResult<T> | null
store.loading()     // boolean
store.error()       // string | null

// Convenience (for segmented control two-way binding)
store.listParams()  // { status, search, page, pageSize, ... }
```

### Backend contract

Handlers accept `PaginationQuery` (from `Wasnie.Application.Common.Models`) via the controller's `[FromQuery] PaginationQuery pagination` parameter. Each handler maintains a **sort whitelist** and falls back to a default column for unknown `SortBy` values. Search is a case-insensitive `Contains` across designated text columns. Filters use **flat query params** (e.g., `?status=Active`, `?payeeId=<guid>`).

`ToPagedResultAsync()` lives in `Wasnie.Application.Common.Extensions.QueryableExtensions` and applies `Skip`/`Take` + `CountAsync` in a single async pair.

### Query param format (locked — do not change)

All paginated endpoints use **flat query params**. Never use nested bracket syntax.

| Param | Example | Notes |
|-------|---------|-------|
| `page` | `?page=2` | 1-based |
| `pageSize` | `?pageSize=25` | Default 25, max 100 |
| `search` | `?search=alice` | Case-insensitive contains |
| `sortBy` | `?sortBy=fullname` | Handler-specific whitelist |
| `sortOrder` | `?sortOrder=desc` | `asc` or `desc` |
| `status` | `?status=Active` | Enum name or integer |

**Frontend:** all services import `buildHttpParams` from `src/app/shared/utils/build-http-params.ts`. Callers pass filters via `PaginationParams.filters` as a flat `Record<string, string>` — the helper serializes each key directly (e.g., `{ status: 'Active' }` → `?status=Active`).

```ts
// Correct
getPayees({ page: 1, pageSize: 25, filters: { status: '1' } })
// → GET /api/payees?page=1&pageSize=25&status=1

// Wrong — never do this
httpParams.set(`filters[status]`, '1')
// → GET /api/payees?filters%5Bstatus%5D=1  ← backend ignores this
```

---

## Dev preview

Navigate to `/__design-system` (dev only, gated by `environment.production === false`) to see all 15 primitives rendered with example data.
