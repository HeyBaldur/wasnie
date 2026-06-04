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

## Bulk import violations

- ❌ **`cell.GetString()` on typed XLSX cells** (WI-P2-04a-fix2). For `XLDataType.DateTime` cells, `GetString()` produces a culture-dependent string (`"4/1/2026 10:21:04 AM"`) that downstream validators cannot parse. Use `ReadCellAsString(cell)` in `FileParserService` which handles `DateTime` → ISO 8601 and `Number` → `InvariantCulture` decimal string explicitly.
- ❌ **`null` (thread culture) in `DateOnly.TryParseExact` calls** (WI-P2-04a-fix2). Always pass `CultureInfo.InvariantCulture` explicitly. `null` resolves to `CultureInfo.CurrentCulture` at runtime — parsing breaks on non-EN-US servers and in test runs with non-default cultures.
- ❌ Bulk money import (transaction CSV) that swallows audit-batch failures (file 05, WI-P2-04a). Unlike payee imports, transaction import audit failures MUST throw — the job is marked Failed and Hangfire retries.
- ❌ Transaction import job that writes per-row `AuditLog` entries inside each chunk transaction instead of in a single end-of-job batch (WI-P2-04a). Batch at the end; per-chunk writes multiply transaction cost by N chunks.
- ❌ Transaction import that does NOT re-validate rows at job start (WI-P2-04a). DB state may change between the validate endpoint call and job execution; re-validation inside the handler is mandatory.
- ❌ **Multi-format date parsing for user-supplied transaction dates** (WI-PROD-T-FIX-4, 2026-06-03). Accepting `DD/MM/YYYY`, `MM/DD/YYYY`, or other locale formats alongside ISO 8601 creates silent cultural ambiguity — `04/05/2026` could be April 5 (US) or May 4 (EU), and the system stores the wrong date without any error. A wrong transaction date determines the wrong Plan period, Quota period, and Payout cycle — a direct financial error. **Rule:** Date parsing for user-supplied transaction and update dates MUST use `DateOnly.TryParseExact` with format `"yyyy-MM-dd"` and `CultureInfo.InvariantCulture` exclusively. All other formats are rejected with the message: `"Date '{value}' is not in the required ISO 8601 format (YYYY-MM-DD). Examples: 2026-05-15, 2026-12-31."` Excel DateTime cells are pre-converted to ISO 8601 by `FileParserService.ReadCellAsString` — they arrive as `"yyyy-MM-dd"` strings and are unaffected by this rule.

---

## Background job violations

- ❌ Background job that accesses the database WITHOUT calling `SetTenant(tenantId)` first (file 09, R9.4.3). The Hangfire dispatcher MUST call `tenantCtx.SetTenant(payload.TenantId)` as its very first action before resolving any service that touches EF Core.
- ❌ Catching or swallowing the `InvalidOperationException` thrown by `BackgroundJobTenantContext.TenantId` when `SetTenant()` has not been called (R9.4.3). It throws by design — suppressing it would let Guid.Empty pass silently through query filters.
- ❌ Hangfire dashboard exposed without an authorization filter (file 04, security). The dashboard shows cross-tenant job data. In Production it MUST be blocked until a global SystemAdmin role/claim is in place.
- ❌ Hangfire (or any background-job library) referenced in Application or Domain layer (file 01, R1.1/R1.4). Hangfire is an Infrastructure concern; Application defines `IBackgroundJobService` + `IJobHandler<T>` abstractions only.
- ❌ Background job that silently returns `Guid.Empty` from a tenant-context instead of throwing (R9.4.3). Every multi-tenant query filter would match zero rows, creating ghost-data bugs. `BackgroundJobTenantContext` exists precisely to prevent this.
- ❌ **`IJobHandler<TPayload>` implementation NOT registered in `Wasnie.Infrastructure/DependencyInjection.cs`** (WI-CALC-A.2.5-FIX, 2026-06-03). `HangfireJobDispatcher` resolves the handler by its `IJobHandler<TPayload>` interface type at runtime — if the registration is missing the error is "No service for type IJobHandler`1[...]" at dispatch time, not at startup. **Checklist:** every `class XyzJobHandler : JobHandlerBase<XyzPayload>` MUST have a corresponding `services.AddScoped<IJobHandler<XyzPayload>, XyzJobHandler>()` entry in the `// Register job handlers` block of `DependencyInjection.cs`.

- ❌ **Calling `hangfireClient.Enqueue(...)` while the `BackgroundJobRecord` is inside an uncommitted DB transaction** (WI-PROD-T-FIX-8, 2026-06-03). With `QueuePollInterval = TimeSpan.Zero`, Hangfire picks up enqueued jobs in ~2ms. If the row is not yet committed (e.g. because `EnqueueAsync` is called inside `AuditBehavior`'s money-critical transaction), the Hangfire worker calls `MarkRunningAsync`, finds no row, throws `InvalidOperationException`, and Hangfire retries in 26 seconds — turning 100ms of real work into a 40-second user-perceived delay. The symptom in logs is `"Background job record {id} not found"` on the first attempt followed by a retry after the retry delay.

  **Fix already in place:** `MarkRunningAsync` now retries up to 5 × 100ms (500ms window) before throwing. This tolerates the race because the outer transaction commits within a few ms after Hangfire picks up the job.

  **Future prevention:** If adding new job-enqueueing paths, ensure the `BackgroundJobRecord` INSERT is committed BEFORE `hangfireClient.Enqueue()` is called. This means either: (a) calling `EnqueueAsync` OUTSIDE of any wrapping DB transaction, or (b) using a fresh `IDbContextFactory<ApplicationDbContext>` connection for the record INSERT so it commits independently.

---

## Frontend data-fetching violations

- ❌ **Resolving referenced entities client-side from a paginated in-memory store** (WI-PROD-F, 2026-05-30). When a list endpoint is server-paginated, a client-side lookup via an in-memory store (e.g. `PayeesStore.payees().find(...)`) silently falls back to the raw foreign-key value (GUID) for any entity not present on the current page. This is a trust-destroying visual bug. **Rule:** Any entity name or display attribute referenced in a server-paginated list MUST be resolved server-side via a JOIN or batch-fetch in the handler and returned in the DTO. The client must never consult an in-memory store to resolve a field from a paginated dataset.
- ❌ **Rendering a raw GUID or empty string as a user-visible fallback** (WI-PROD-F, 2026-05-30). When a nullable name field is absent (null or empty string), the UI MUST render a localized "Unassigned" / "Sin asignar" / "Bez przypisania" string — never the raw UUID, never an empty cell.

---

## Validation error message violations

- ❌ **Generic validation error messages that omit the offending value** (WI-PROD-E, 2026-06-01). Every `ValidationIssue` emitted in an import validator MUST include the actual offending value when it is meaningfully displayable (e.g. the bad code, the duplicate email, the unparseable date string). "Payee code not found." is forbidden; `"Payee code 'EMP999' not found in this tenant."` is required. The user must be able to identify the cell to fix without cross-referencing row numbers.
- ❌ **Validation error messages that omit the corrective action for reference errors** (WI-PROD-E, 2026-06-01). Reference errors (entity not found, duplicate) MUST suggest the corrective action: "Create the payee first or correct the code in your file." A message that only states the fact leaves the user guessing what to do.
- ❌ **`ValidationIssue` emitted without a `Category`** (WI-PROD-E, 2026-06-01). The `Category` field on `ValidationIssue` (enum: `Reference`, `Format`, `Required`, `Other`) defaults to `Other` but every emit site SHOULD set an explicit category so the UI can render distinct visual treatment. "Other" is the fallback for truly unclassifiable issues only.

---

## Field requirement configuration violations

- ❌ **Hardcoding required/optional in validators for any field listed in the `FieldRequirementSettings` catalog** (WI-PROD-A.1, 2026-06-01). The catalog currently contains: `Payee.Email`, `Payee.HireDate`. Validators MUST consult `IFieldRequirementService.IsRequiredAsync(entityName, fieldName, ct)` for these fields instead of using `NotEmpty()` or `NotNull()` unconditionally. Format validation (email format, date range) is ALWAYS enforced when a value is present — only the presence check is configurable.
- ❌ **Using the synchronous `ValidationBehavior.Validate()` with validators that contain `MustAsync` rules** (WI-PROD-A.1, 2026-06-01). FluentValidation throws `InvalidOperationException` when a validator with async rules is executed synchronously. `ValidationBehavior` MUST use `ValidateAsync()`. Any new MediatR pipeline behavior that runs validators must also use `ValidateAsync()`.
- ❌ **Adding new fields to the `FieldRequirementSettings` catalog in the validator without also adding a migration seed entry** (WI-PROD-A.1, 2026-06-01). Every catalog field requires: (a) a migration that inserts a row per existing tenant with the appropriate default, and (b) a seed entry in `TestDatabaseFixture.SeedTestTenantsAsync` for integration tests. Missing seed entries cause integration tests to treat the field as Optional regardless of the intended default.

## Frontend component overflow violations

- ❌ **Custom `overflow` CSS on modal SCSS to work around `ws-select` dropdown clipping** (WI-PROD-R, 2026-06-01). `WsSelectComponent` is already modal-aware: `openDropdown()` detects the nearest `.ws-modal__dialog` via `Element.closest()` and uses its bounds for upward-flip and height-constraint logic. Adding `overflow-y: auto` or custom dropdown positioning in modal component SCSS is forbidden — it duplicates the logic already in `ws-select` and creates split-brain behaviour.
- ❌ **Absent/null values in data table cells styled with the same color and weight as real values** (WI-PROD-R, 2026-06-01). When a table cell holds a placeholder for an absent value (e.g. "Unassigned"), it MUST be visually distinct from real values. Accepted treatment: `font-style: italic` + `color: var(--color-text-tertiary)`. Forbidden: rendering "Unassigned" or "None" in `col-secondary` or `col-primary` — a comp manager scanning a dense list cannot distinguish an action-required row from a populated one.

## Domain null-check violations

- ❌ **Using `== null` or `!= null` with `Entity` or `ValueObject` subclasses** (WI-CALC-A.1, 2026-06-02). Both `Entity.operator ==` and `ValueObject.operator ==` return `false` when both operands are null (they require both operands to be non-null AND equal). Consequently, `null == null` → `false` and `null != null` → `true`. This means:
  - `if (entity == null) return;` does NOT return when `entity` IS null — the null check is bypassed.
  - `if (valueObject != null) access.Property;` throws `NullReferenceException` when `valueObject` IS null.
  
  **Rule:** Always use C# pattern matching for null checks on `Entity` and `ValueObject` subclasses: `is null` and `is not null`. Never use `== null` or `!= null`.

  ```csharp
  // FORBIDDEN:
  if (assignment == null) return;     // does NOT return when assignment is null
  if (plan == null) return;           // same bug
  if (effectivePeriod != null) ...;   // does NOT guard against null
  
  // CORRECT:
  if (assignment is null) return;
  if (plan is null) return;
  if (effectivePeriod is not null) ...;
  ```

## Frontend reactive form enum binding violations

- ❌ **Patching an Angular reactive form with raw enum values from the API without coercing to numbers first** (WI-FRONTEND-FIX-1, 2026-06-02). The backend adds `JsonStringEnumConverter` globally; all C# enum values serialize to string names in the HTTP response (`"Revenue"`, `"Flat"`, `"Sum"` rather than `0`, `0`, `0`). `WsSelect` uses strict equality (`===`) when matching the form control value against option values. If options have `value: 0` (number) but the form control has `"Revenue"` (string), NO option matches — the dropdown is silently blank.

  **Rule:** Any `_loadExistingRule()` / `_patchFormFromDto()` method that patches a form whose controls hold enum values MUST coerce each API value from string to its numeric enum value before calling `patchValue()`. Use the `_enumToNumber<T extends Record<string, unknown>>(enumObj: T, value: unknown): number` pattern:

  ```typescript
  private _enumToNumber<T extends Record<string, unknown>>(enumObj: T, value: unknown): number {
    if (typeof value === 'number') return value;
    if (typeof value === 'string') {
      const n = enumObj[value];
      return typeof n === 'number' ? n : 0;
    }
    return 0;
  }

  // Usage in patchValue:
  this.form.patchValue({
    measurement: {
      type: this._enumToNumber(MeasurementType, rule.measurement.type),
      aggregation: this._enumToNumber(MeasurementAggregation, rule.measurement.aggregation),
    },
    rateTable: {
      type: this._enumToNumber(RateTableType, rule.rateTable.type),
    },
  });
  ```

  This applies to: all `WsSelect` enum dropdowns, `rateTableType()` computed signals, and any TypeScript code that compares an API-sourced enum value to a numeric enum literal.

## EF Core owned-type nullable violations

- ❌ **Calling `.IsRequired(false)` on a sub-property of a nullable owned value object when the sub-property type is a struct (e.g. `DateOnly`, `int`, `decimal`)** (WI-CALC-A.0, 2026-06-02). EF Core 8 throws at design time: "The property cannot be marked as nullable/optional because the type is not a nullable type." For nullable owned types where sub-properties are structs, the correct pattern is to express the optional nature only on the **navigation**: `builder.Navigation(r => r.EffectivePeriod).IsRequired(false)`. EF Core then allows both mapped columns to be `null` in the DB when the owned object is null. Sub-properties of a reference type (`string`) CAN still call `IsRequired(false)` directly. Example: `OwnsOne(r => r.EffectivePeriod, ep => { ep.Property(d => d.Start).HasColumnName("EffectivePeriodStart").HasColumnType("date"); ... }); builder.Navigation(r => r.EffectivePeriod).IsRequired(false);`

---

## Currency validation violations

- ❌ **Validating currency codes with a format-only regex (`^[A-Z]{3}$`) without a whitelist** (WI-PROD-T-FIX-10, 2026-06-03). A regex check alone accepts any 3-letter uppercase combination: XXX, ABC, ZZZ, AAA — all valid by regex, all invalid as real ISO 4217 codes. A user can corrupt financial data by typing three random uppercase letters. The fix is a whitelist of known codes, not a regex.

  **Rule:** Currency code validation MUST use `CurrencyConstants.KnownCurrencies` (defined in `Wasnie.Application.Common.Constants`). Apply this check at every currency entry point: transaction imports (`TransactionFieldValidators.ValidateCurrency`), transaction updates (same shared helper), and plan creation (`CreatePlanCommandValidator`). The regex `^[A-Z]{3}$` alone is FORBIDDEN as the sole currency check.

  To add a new supported currency: add it to `CurrencyConstants.KnownCurrencies` and redeploy. No migration needed.

---

## Export/list filter divergence violations

- ❌ **Calling `store.toQueryParams()` (URL-sync shorthand keys) to build the export POST body** (WI-PROD-T-FIX-1, 2026-06-03). `toQueryParams()` produces abbreviated URL keys (`txFrom`, `txTo`, `ref`, `amtMin`, etc.) for browser address-bar sync — these are NOT the field names the backend `PaginationQuery` expects (`DateFrom`, `DateTo`, `Reference`, `AmountMin`, etc.). Sending these as a JSON body silently drops all non-matching fields. **Rule:** Use `store.toExportFilter()` (which calls `_buildFilterRecord`) to build any export payload. `toQueryParams()` is only for URL display.
- ❌ **List and export endpoints applying different filter predicates** (WI-PROD-T-FIX-1 + WI-PROD-I.2). `TransactionsStore._buildFilterRecord(f)` is the single source of truth for `TransactionFilter → PaginationQuery field names`. Both `_loadInternal` and `toExportFilter` call it. If you add a new filter field to `TransactionFilter`, you MUST add it to `_buildFilterRecord` — it will then automatically apply to both list and export.
- ❌ **Adding a `TransactionFilter` field without updating ALL eight locations** (WI-PROD-T-FIX-11, 2026-06-04). The root cause of the critical payeeIds bug: `payeeIds` existed in the filter state and URL chip but was missing from `_buildFilterRecord` — the list API call silently ignored it, returning the wrong payee's transactions. A user could click "Process Pending" on results belonging to a completely different payee. **Checklist when adding any new filter field:** (1) `TransactionFilter` interface; (2) `EMPTY_FILTER`; (3) `_buildFilterRecord` ← **list API path, most commonly missed**; (4) `toExportFilter`; (5) `toQueryParams` + `loadFromQueryParams` (URL sync); (6) backend `PaginationQuery`; (7) `ListTransactionsHandler` WHERE predicate; (8) `ExportTransactionsHandler` WHERE predicate. Missing (3) is a critical financial data-integrity bug: the UI chip looks correct, the URL looks correct, but the query returns wrong data.

---

## Chunked-job N+1 query violations

- ❌ **Calling `ICreditAllocationService.AllocateAsync(transaction, ct)` (single-tx path) inside a per-row loop in a chunked background job** (WI-PROD-T-FIX-7, 2026-06-03). The single-transaction overload makes 2 DB roundtrips per invocation (PlanAssignments + Plan+Rules). In a loop of N rows this is an N+1 pattern. On Azure F1 (5-DTU SQL) this was the root cause of 10–60s wall-clock for a 2-transaction Process Pending job.

  **Rule:** Any chunked job (N rows in a loop) that calls `AllocateAsync` MUST use the batch overload: pre-load all assignments and plans for the chunk in **2 queries total** before the per-row loop, then pass the pre-loaded dictionaries to `AllocateAsync(transaction, assignmentsByPayee, plansById, ct)`. The batch overload performs 0 DB queries per invocation.

  ```csharp
  // FORBIDDEN in per-row loops:
  var credits = await creditAllocationService.AllocateAsync(transaction, ct);  // 2 DB queries each time

  // REQUIRED:
  // Pre-load BEFORE the loop (2 queries total for the chunk):
  var chunkAssignments = await db.PlanAssignments.IgnoreQueryFilters()
      .Where(a => a.TenantId == tenantId && chunkPayeeIds.Contains(a.PayeeId)).ToListAsync(ct);
  var assignmentsByPayee = chunkAssignments.GroupBy(a => a.PayeeId)
      .ToDictionary(g => g.Key, g => (IReadOnlyList<PlanAssignment>)g.ToList());
  var plansInChunk = await db.CompensationPlans.IgnoreQueryFilters().Include(p => p.Rules)
      .Where(p => p.TenantId == tenantId && chunkPlanIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

  // Inside the loop (0 DB queries):
  var credits = await creditAllocationService.AllocateAsync(transaction, assignmentsByPayee, plansInChunk, ct);
  ```

- ❌ **Issuing one DB query per assignment to load transaction IDs in `LoadByPlanAsync`** (WI-PROD-T-FIX-7, 2026-06-03). Loading full assignment entities in one query, then looping to issue one `CompensationTransactions` query per assignment is N+1. **Rule:** Load all pending transactions for the full set of payee IDs in one query, then filter in memory by assignment period. EF Core 8 does not reliably translate `DateOnly` on owned `DateRange` in WHERE clauses — in-memory filtering after a set-based fetch is the correct workaround (consistent with `CreditAllocationService`'s own approach for assignment date matching).

## Batch operation abort violations

- ❌ **`ProcessPendingTransactionsJobHandler` (or any batch job handler) re-throwing `DomainException` from a per-item call** (WI-PROD-T, 2026-06-03). A `DomainException` from `CreditAllocationService.AllocateAsync()` (e.g. currency mismatch) must be caught per-transaction — the transaction is skipped (stays Pending) and the job continues with remaining transactions. Re-throwing aborts the entire batch. **Rule:** Per-transaction validation failures inside a batch job MUST be caught by a `catch (DomainException)` block, logged with TransactionId + reason, counted in `skipReasonCounts`, and treated as a skip — never as a job failure.

- ❌ **Batch operations on transactions (import, process pending, recalculate) that abort the entire batch on a single-row validation failure** (WI-PROD-T-FIX-3, 2026-06-03). The correct pattern is **skip-and-continue**. User-visible error surfaces as one of: (a) per-row `IssueCategory` error shown in the Preview step (Step 3 of the import wizard), or (b) per-row skip logged in the job's `ResultSummary.skipDetails`. **Never abort the batch.** Specifically: currency mismatch between a transaction's currency and its assigned plan's currency MUST be caught in `TransactionImportValidationService.ValidateAsync()` as a per-row `Error` with `IssueCategory.Reference` — so the row is excluded from the import before execution. `TransactionImportJobHandler` also has a defensive `catch (DomainException)` as belt-and-suspenders in case the check is bypassed (e.g. assignment created between validate and execute).

## Skip and audit log identifier violations

- ❌ **Skip logs, audit logs, or result summaries that contain only internal Guids without human-readable identifiers** (WI-PROD-T-FIX-5, 2026-06-03). A Guid alone is unactionable — the user cannot match `491afc5a-...` to a real invoice without querying the DB. **Rule:** Every skip entry MUST include at minimum the `ReferenceNumber` (the business identifier the user chose when creating the transaction) alongside the internal ID. Enriched entries should also include payee name/code, transaction date, and amount so the user can identify the row without leaving the current screen. Applies to: `ProcessPendingTransactionsJobHandler.skipDetails`, any future batch job that skips rows, import skip summaries.

## Excel re-upload immutability violation

- ❌ **Making `ReferenceNumber` writable in the Update-from-Excel mapping step** (WI-PROD-T, 2026-06-03). `ReferenceNumber` is the immutable identity key used to locate the existing transaction during re-upload. If the user modifies it, the update would silently target the wrong transaction. **Rule:** The `ReferenceNumber` column in `TransactionUpdateColumnMapping` is a fixed key — it MUST be auto-detected and locked in the UI mapping step, never offered as a user-configurable target field. The validation service MUST return an `Error` row for any row where `ReferenceNumber` is blank or not found.

## Cross-wizard validation divergence violations

- ❌ **Duplicating field-level validation logic across the IMPORT and UPDATE wizards** (WI-PROD-T-FIX-9, 2026-06-03). Each wizard has its own `ValidationService`, and it is easy for them to diverge over time. The reported case: `TransactionUpdateValidationService` had no currency format check — "3SD2F13SD" passed the UPDATE preview as `WillUpdate`, then failed silently at apply time inside `Money.Of` (which only validates length-3), leaving the user thinking the update applied when it silently skipped.

  **Rule:** All field-level validation rules (format, range, minimum, maximum) for `amount`, `currency`, `transactionDate`, and any other shared fields MUST be implemented in `TransactionFieldValidators` (static class in `Wasnie.Application.Services.Imports`) and called identically by BOTH `TransactionImportValidationService` and `TransactionUpdateValidationService`. Never inline a validation expression directly in a validator if the same expression is needed in another wizard.

  **Checklist when adding a validation rule to one wizard:**
  1. Does this rule apply to the same field in the other wizard? → Yes → add to `TransactionFieldValidators` and use from BOTH wizards.
  2. Does this rule only make sense in one context (e.g. file-level duplicate check)? → Keep inline in that wizard only.
  3. Does the UPDATE wizard need a `today` reference for future-date check? → Inject `IClock` (already done as of FIX-9).

## Filter/count query alignment violations

- ❌ **A list-endpoint filter query and its corresponding count query using DIFFERENT predicates** (WI-PROD-I.2, 2026-06-03). If `GetPendingTransactionsCountQuery.CountByPayeeAndPeriod` filters `Status==Pending && PayeeId==id && TransactionDate between start and end`, then the Transactions list endpoint with the equivalent filter combination MUST produce the same count. Duplicate WHERE clauses between count and list queries cause the plan-page "77 Pending" badge to disagree with the filter result total — a trust-destroying bug. **Rule:** Extract the predicate into a shared helper or verify alignment by code review whenever either the count handler or the list handler is modified.

---

## Calculated-value invisibility violations

- ❌ **Any value calculated and persisted by the system with no UI to inspect it** (WI-PROD-CREDITS-VISIBILITY, 2026-06-04). Hiding calculations in the database forces users (and developers) to run SQL queries to verify correctness. This destroys trust and makes bugs invisible until they cause a financial error. **Rule:** Every entity that holds a calculated financial value (Credit, Payout, Tax line, etc.) MUST have: (a) a list page filtered/searchable by the relevant dimensions, (b) a detail page showing all 5 of: the result, the source data, the rule/formula applied, a "show your work" step-by-step trace, and audit information. A count or aggregate on a dashboard is not a substitute for inspectability — users need to see individual rows, trace them back to source transactions, and verify the math.

---

## Financial action opacity violations

- ❌ **Showing a count of affected items before an action without showing which items** (WI-PROD-T-FIX-12, 2026-06-04). A badge that says "3 Pending eligible for processing" without a visible list of those 3 transactions is a black box. In a financial system this destroys trust: the user cannot verify what will be acted on, cannot cross-reference with their Excel records, and cannot detect a misconfiguration before it corrupts commission records. **Rule:** Any "eligible / applicable / affected" count displayed before a user-triggered financial action (Process Pending, batch write, recalculate) MUST be backed by an inline, inspectable list of the exact items the action WILL target. The list MUST use the SAME predicate as the count — if they diverge, the list is misleading. Caps (e.g. "first 200, see filter for rest") are acceptable; a count-only display is not.

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
