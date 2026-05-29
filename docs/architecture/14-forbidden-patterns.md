# 14 — Forbidden Patterns

**Reading time:** ~4 min
**Applies to:** ALL Wasnie development

---

## Purpose

This is the **consolidated list of FORBIDDEN patterns** across all sections. Use it as a quick reference checklist. Each item links to its full rule in the relevant section.

If you're about to write code that matches any pattern here, STOP. Either you're violating a rule, or the rule needs amendment.

---

## Architecture violations

### A1 — Cross-layer imports

- ❌ Domain referencing EF Core, ASP.NET Core, or any external framework (file 01, R1.1)
- ❌ Application referencing Infrastructure (file 01, R1.2)
- ❌ Presentation referencing Domain or Infrastructure directly (file 01, R1.4)
- ❌ Domain entities with `[Key]`, `[Column]`, `[JsonPropertyName]`, etc. (file 01, R1.5)

### A2 — Adding architectural layers

- ❌ Adding "Service", "Manager", "Helper" intermediate layer without amendment (file 01, R1.11)

### A3 — Bypassing architecture for "performance"

- ❌ Controller querying DbContext directly to "skip a layer" (file 01, R1.10)

### A4 — Frontend HTTP from components

- ❌ Components injecting `HttpClient` directly (file 01, R1.6)
- ❌ Business logic embedded in components instead of pure functions (file 01, R1.7)
- ❌ Financial calculations inline in templates (file 01, R1.8)

---

## SOLID violations

### B1 — Single Responsibility

- ❌ Service class > 300 lines or > 10 public methods (file 02, R2.1.1)
- ❌ Controller with business logic (file 02, R2.1.2)
- ❌ Controller > 200 lines or action method > 20 lines (file 02, R2.1.2)
- ❌ Component > 300 lines without explicit justification (file 02, R2.1.3)

### B2 — Open/Closed

- ❌ `switch (rule.Type)` or `if (rule is SomeType)` in business logic (file 02, R2.2.2)
- ❌ Modifying existing code to add a new variant when polymorphism would work

### B3 — Liskov Substitution

- ❌ Throwing `NotImplementedException` for inputs an interface declares as valid (file 02, R2.3.1)

### B4 — Interface Segregation

- ❌ Interface > 8 methods without justification (file 02, R2.4.1)
- ❌ God repository interfaces (file 02, R2.4.2)

### B5 — Dependency Inversion

- ❌ `DateTime.UtcNow` in Domain or Application layer (file 02, R2.5.3)
- ❌ `Guid.NewGuid()` in business logic (file 02, R2.5.3)
- ❌ `Random` in business logic
- ❌ Static service locators
- ❌ Concrete dependencies instead of interfaces

---

## Performance violations

### C1 — Pagination

- ❌ **Client-side pagination** — fetching all records and slicing in JS/C# (file 03, R3.2.1)
- ❌ Unbounded queries (no `Take()` / `Skip()`)
- ❌ Page size > 100

### C2 — Queries

- ❌ N+1 queries (file 03, R3.2.3)
- ❌ Queries against non-indexed columns in production paths (file 03, R3.2.2)
- ❌ String-concatenated SQL (file 04, R4.5.1)
- ❌ Unwhitelisted `OrderBy` field (file 03, R3.2.4)
- ❌ Transactions held > 5 sec (file 03, R3.2.5)
- ❌ Inserting > 1 record at a time when bulk is feasible (file 03, R3.2.6)

### C3 — Frontend

- ❌ Bundle size > 500KB initial / 200KB per lazy chunk (file 03, R3.3.2)
- ❌ Blocking HTTP calls in `ngOnInit` (file 03, R3.3.3)
- ❌ Non-debounced search input firing API calls (file 03, R3.3.4)

---

## Security violations

### D1 — Authentication

- ❌ Endpoints not protected by `[Authorize]` except the documented public list (file 04, R4.1.1)
- ❌ JWT lifetimes > documented (file 04, R4.1.2)
- ❌ Reusable refresh tokens (must be one-time use) (file 04, R4.1.2)
- ❌ Passwords stored unhashed or with reversible encryption (file 04, R4.1.5)

### D2 — Input validation

- ❌ Request body without FluentValidation (file 04, R4.4.1)
- ❌ Trusting frontend validation as security (file 04, R4.4.3)

### D3 — SQL/XSS

- ❌ String concatenation in SQL (file 04, R4.5.1)
- ❌ `[innerHTML]` with user-supplied data (file 04, R4.6.1)
- ❌ `bypassSecurityTrustHtml` (file 04, R4.6.1)
- ❌ CORS `*` in production (file 04, R4.7.2)

### D4 — Multi-tenant

- ❌ Queries without `WHERE TenantId = ...` (file 09, R9.1.1)
- ❌ Reading `TenantId` from request body (file 09, R9.1.2)
- ❌ Cache keys without tenant scope (file 09, R9.2.5)
- ❌ Cross-tenant Manager references (file 09, R9.2.4)

### D5 — Secrets

- ❌ Secrets in git (file 04, R4.9.1)
- ❌ Connection strings in code (file 04, R4.9.1)
- ❌ Secrets visible in logs (file 04, R4.9.4)
- ❌ `appsettings.json` with production secrets

---

## Audit violations

- ❌ Destructive operation without audit log entry (file 05, R5.1.1)
- ❌ Money operation without audit log entry (file 05, R5.1.2)
- ❌ `UPDATE` or `DELETE` on audit table (file 05, R5.3.1, R5.3.2)
- ❌ Audit log entries containing secrets (file 05, R5.3.4)
- ❌ Domain entity state-change method that does NOT raise a domain event (§5b.7). Every method that transitions an aggregate's status MUST call `RaiseDomainEvent(...)` before returning. Phase 3+ stubs that throw `NotSupportedException` immediately (and thus do not change state) are exempt; document them as stubs. (WI-P2-02, 2026-05-28)

---

## Testing violations

- ❌ Mocking repositories in INTEGRATION tests (file 07, R7.3.1)
- ❌ Real HTTP calls in unit tests (file 07, R7.3.5)
- ❌ Tests that depend on each other (file 07, R7.5.2)
- ❌ Flaky tests left in the suite (file 07, R7.5.3)
- ❌ Endpoint without cross-tenant test (file 09, R9.3.1)
- ❌ Endpoint without authentication test (file 07, R7.4.1)

---

## Breaking change violations

- ❌ Endpoint signature change without updating ALL consumers in the same PR (file 08, R8.2.2)
- ❌ "no regressions" claimed without running the FULL test suite (file 08, R8.2.3)
- ❌ Rename column in a single deploy (not backwards-compatible) (file 08, R8.4.1)

---

## Code style violations

- ❌ `any` in TypeScript without justification comment
- ❌ `dynamic` in C# without justification comment
- ❌ `console.log` in production code (file 11, R11.1.7)
- ❌ `Console.WriteLine` in production code (file 11, R11.1.7)
- ❌ Hardcoded color values in component SCSS (file 10, R10.4.1)
- ❌ TODO comments without issue links (file 11, R11.1.8)
- ❌ Commented-out code (file 11, R11.1.9)
- ❌ Build warnings (file 11, R11.1.2)

---

## Visual change violations

- ❌ Systemic refactor for a local visual bug (file 10, R10.1.1)
- ❌ Adjectives in visual specs ("subtle", "clear") instead of numerical values (file 10, R10.1.2)
- ❌ "Refactor related components" without explicit file list (file 10, R10.1.3)
- ❌ Cards visually identical to page background (file 10, anti-patterns)
- ❌ Horizontal scroll in tables (file 10, anti-patterns)
- ❌ Multiple scrollbars in one container (file 10, anti-patterns)

---

## Background job violations

- ❌ Background job that accesses the database WITHOUT calling `SetTenant(tenantId)` first (file 09, R9.4.3). The Hangfire dispatcher MUST call `tenantCtx.SetTenant(payload.TenantId)` as its very first action before resolving any service that touches EF Core.
- ❌ Catching or swallowing the `InvalidOperationException` thrown by `BackgroundJobTenantContext.TenantId` when `SetTenant()` has not been called (R9.4.3). It throws by design — suppressing it would let Guid.Empty pass silently through query filters.
- ❌ Hangfire dashboard exposed without an authorization filter (file 04, security). The dashboard shows cross-tenant job data. In Production it MUST be blocked until a global SystemAdmin role/claim is in place.
- ❌ Hangfire (or any background-job library) referenced in Application or Domain layer (file 01, R1.1/R1.4). Hangfire is an Infrastructure concern; Application defines `IBackgroundJobService` + `IJobHandler<T>` abstractions only.
- ❌ Background job that silently returns `Guid.Empty` from a tenant-context instead of throwing (R9.4.3). Every multi-tenant query filter would match zero rows, creating ghost-data bugs. `BackgroundJobTenantContext` exists precisely to prevent this.

---

## Claude Code autonomy violations

- ❌ Claude Code performing ANY git operation (file 13, R13.2)
- ❌ Claude Code deploying to staging or production (file 13, R13.2)
- ❌ Claude Code making external API calls with side effects (file 13, R13.2)
- ❌ Claude Code mass-deleting files outside the project (file 13, R13.2)

---

## How to use this list

### Before writing code

Check: am I about to do any of these? If yes, STOP. Either fix your approach or amend the rule via the process in `ARCHITECTURE.md`.

### Before generating a prompt

Check: does my prompt instruct Claude Code to do any of these? If yes, the prompt is wrong. Fix it.

### During code review

Check: does the PR contain any of these? If yes, request changes.

### After a bug is found

Add to this list. Every bug that violates an existing rule is a code review failure; every bug that doesn't is a candidate for a new rule.

---

## Maintenance

This file is the canonical FORBIDDEN list. Other section files contain the same rules with full context; this file consolidates them for quick reference.

When a new rule is added to any section, it MUST be reflected here in the same change set.
