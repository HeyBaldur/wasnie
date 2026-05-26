# 10 — Visual Changes Protocol

**Reading time:** ~5 min
**Applies to:** Frontend UI, design tokens, component styling

---

## Why this matters

Visual changes look easy. They are not — for AI-assisted development, they have a peculiar failure mode: **systemic refactors caused by local bugs**. In Phase A:

- A simple card-background mismatch turned into a "let's redesign the entire surface elevation system"
- A simple icon missing turned into a "let's audit every page for missing icons"
- A wizard step indicator issue turned into a "let's create 4 new reusable components"

These tangential expansions caused:
- App-shell layouts breaking
- Working pages getting "refactored" into worse state
- Hours of rollbacks
- User frustration that was justified

This file codifies how to make visual changes SURGICALLY.

---

## 10.1 The core principle

### Rule 10.1.1 — Local visual bug = local visual fix

If the bug is one card looking wrong on one page, the fix is ONE FILE. Not a system. Not a refactor. ONE FILE.

### Rule 10.1.2 — Numerical specs, not adjectives

FORBIDDEN in visual specs:
- "subtle"
- "clear"
- "visible"
- "noticeable"
- "modern"
- "clean"
- "polished"

REQUIRED:
- "15% more luminance than X"
- "minimum contrast ratio 4.5:1"
- "12px gap between elements"
- "Letters tracking 0.05em"

### Rule 10.1.3 — Specific file lists, not "related components"

FORBIDDEN in visual prompts:
- "update related components"
- "fix similar issues"
- "audit other pages"
- "refactor where applicable"

REQUIRED:
- "Only modify `payee-detail.component.scss`"
- "DO NOT touch app-shell, sidebar, or layout files"
- "Modify exactly 2 files: [list]"

---

## 10.2 What MUST be in every visual prompt

### Rule 10.2.1 — Explicit scope

```markdown
## Scope

Files to modify (and ONLY these):
1. src/styles/tokens.scss
2. src/app/features/payees/payee-detail/payee-detail.component.scss

Files NOT to touch under any circumstance:
- src/app/shell/*
- src/app/core/*
- Any component other than the one named above
```

### Rule 10.2.2 — Concrete acceptance criteria

```markdown
## Acceptance

1. On http://localhost:4200/payees/{id}, the card has background `--color-bg-surface-deep`
2. The token `--color-bg-surface-deep` is defined in all 3 themes (light, dark, soft)
3. The token value is 8-12% darker (lower luminance) than `--color-bg-surface-raised`
4. No other file in the codebase changed
5. App-shell, sidebar, header look identical to before
```

### Rule 10.2.3 — Hard constraints listed

```markdown
## Hard constraints

- DO NOT add other tokens
- DO NOT refactor related styles
- DO NOT touch app-shell, sidebar, header, or any layout
- DO NOT change colors of cards in other pages
- This is a 3-file change MAX
```

---

## 10.3 When IS systemic visual change appropriate?

### Rule 10.3.1 — Systemic only after pattern verified across multiple components

Don't create a "design system rule" from one bug. Wait until the same issue appears in 3+ places. Then refactor.

### Rule 10.3.2 — Systemic changes are separate prompts

If you DO need a systemic change (e.g., introducing `WsCard` to standardize card styling), it MUST be its own prompt with:
- Explicit list of every file affected
- Migration strategy (parallel old/new, then cleanup)
- Verification step after each file
- Rollback plan if it breaks something

### Rule 10.3.3 — Systemic visual changes get rolled out behind a flag if possible

If introducing a new design token system, the new tokens can be aliased to the old ones during transition:

```scss
// Phase 1: introduce new tokens, alias to old
--color-bg-surface-deep: var(--color-bg-surface-raised);

// Phase 2: change the values progressively
--color-bg-surface-deep: #x;  // new value

// Phase 3: components migrate one-by-one to use new tokens

// Phase 4: remove old token aliases once nothing uses them
```

---

## 10.4 Design token rules

### Rule 10.4.1 — All component styles use tokens

NO hardcoded colors in component SCSS. Every color references a token defined in the token file.

### Rule 10.4.2 — Adding a token requires justification

Don't add tokens for one-off uses. If a token is only used in one component, the component should use an existing token instead.

Exception: if no existing token fits AND the usage will recur, a new token is justified — but document the intended uses in the token's comment.

### Rule 10.4.3 — Tokens follow semantic naming, not literal

```scss
/* CORRECT */
--color-bg-surface: ...      /* what it represents */
--color-success: ...
--color-border-default: ...

/* FORBIDDEN */
--color-blue-500: ...        /* tied to literal value */
--color-dark-gray-300: ...
--background-1: ...          /* meaningless */
```

### Rule 10.4.4 — All 3 themes MUST have the token

When adding a new token, it MUST be defined for light, dark, AND soft themes simultaneously. Forgetting one is a bug.

---

## 10.5 Component styling rules

### Rule 10.5.1 — Reusable components live in `shared/` (or `core/`)

Components used across multiple features go in `WasnieUi/src/app/shared/components/` (or `core/components/`). Feature-specific components stay in their feature folder.

### Rule 10.5.2 — `Ws` prefix for reusable components

`WsButton`, `WsCard`, `WsDataTable`, `WsModal`, etc. Easy to identify reusable building blocks.

### Rule 10.5.3 — Component styles MUST be encapsulated

Use Angular's default view encapsulation. Avoid `:host-context` or global styles in component SCSS — those cause cross-component bleeds.

### Rule 10.5.4 — DESIGN_SYSTEM.md is the source of truth for visual rules

This file (10) governs the PROCESS of visual changes. `DESIGN_SYSTEM.md` (in WasnieUi/) governs the SUBSTANCE (tokens, components, patterns).

When in conflict, this file wins on process; DESIGN_SYSTEM.md wins on visual specifics.

---

## 10.6 Layout responsiveness

### Rule 10.6.1 — Max-width strategy

Pages MUST use a max-width strategy:

| Page type | Max width |
|---|---|
| Forms (single column) | 800px |
| Lists, dashboards | 1200px |
| Wide reports | 1400px |

Content centered horizontally. Page background fills full viewport.

### Rule 10.6.2 — Mobile responsive is Phase 8

Phase 1-7 target desktop only (1280px+). Mobile responsive comes later. This is documented in the Master Plan and is not a "we'll see" — it's deferred intentionally.

When mobile arrives, it gets its own prompt(s) following the same surgical principles.

### Rule 10.6.3 — Test on minimum viewport

Test pages at 1280px width. Things that work at 1920px+ MAY break at 1280px (sidebar takes proportionally more space).

---

## 10.7 Forbidden visual anti-patterns

(See file 14 for the consolidated list)

- Hardcoded colors in component SCSS
- "Refactor" related styles when fixing one bug
- Cards visually identical to page background (no visible elevation)
- Multiple scrollbars in nested containers
- Horizontal scroll in tables (truncate with tooltip instead)
- Heavy shadows in dark themes (use background contrast)
- Visual prompts that touch >5 components without prior planning
- Adding tokens for one-off use cases
- Color values without contrast check (4.5:1 minimum WCAG AA)

---

## Enforcement

- **Code review** checks scope of visual changes (Rule 10.1.1)
- **DESIGN_SYSTEM.md** referenced in every PR with visual changes
- **CSS linter** (Phase C5) blocks hardcoded color values
- **Visual regression testing** (Phase C5+) catches unintended visual changes via screenshot diff

---

## Bug history

- **Phase A — prompt 38:** UI Polish prompt rightfully created reusable components (`WsPageLayout`, `WsWizard`, `WsDataTable`, `WsStatCard`). Justified systemic change.
- **Phase A — prompt 44:** Surface elevation system prompt was too systemic for the bug at hand (one card background). Resulted in rolled-back changes.
- **Phase A — prompt 46:** Adjusted token values without fixing the root cause (rule violated by `--color-bg-surface-raised` being used both for outer card and inner table header).
- **Phase A — prompt 47:** SURGICAL FIX — added one new token, changed one line in one file, documented in DESIGN_SYSTEM.md. This is the model for visual fixes going forward.

**Lesson:** the same issue can be solved with a 5-line surgical fix or a 500-line systemic refactor. The 5-line fix is almost always correct. Only escalate to systemic when the pattern is proven and the cost of NOT doing so is clear.
