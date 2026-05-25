# Wasnie Design System

Stripe-quality component library for the Wasnie SPM platform. All primitives live in `src/app/shared/ui/`. The single source of truth for tokens is `src/styles.scss`.

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

### WsSelect `<ws-select>`
CVA. Options type: `SelectOption { value: string; label: string; disabled?: boolean }`. Labels are run through `| translate` automatically.  
Inputs: `options` · `label` · `placeholder` · `searchable` · `error`

```html
<ws-select formControlName="currency" label="Currency" [options]="currencyOptions" />
```

### WsDatePicker `<ws-date-picker>`
CVA. Emits ISO date string. Inputs: `label` · `placeholder` · `minDate` · `maxDate`

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
Two-way: `[(isOpen)]`. Slots: `[slot=header]` · body (default) · `[slot=footer]`  
Inputs: `size` (sm | md | lg | xl) · `closeOnBackdrop`  
Outputs: `closed`  
Focus trap + body-scroll lock built-in.

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

## Dev preview

Navigate to `/__design-system` (dev only, gated by `environment.production === false`) to see all 15 primitives rendered with example data.
