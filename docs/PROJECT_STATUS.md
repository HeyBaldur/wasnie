# Wasnie — Project Status

**Last updated:** 2026-05-29
**Updated by:** Rodolfo Calvo (WI-DOCS-UPDATE — real-data test findings captured; domain-model backlog opened)
**Purpose:** Single source of truth for "where Wasnie is right now." Read this first when resuming work.

---

## What Wasnie is (in 2 sentences)

Wasnie is a Sales Performance Management (SPM) / Incentive Compensation Management (ICM) SaaS platform for mid-market companies (50–300 sales reps). It targets the gap between enterprise SPM tools (Xactly, SAP, Varicent — too complex and expensive) and spreadsheets (still used by ~50% of mid-market), with European + Latin American focus initially.

For full product context, see `docs/Wasnie_Product_Master_Specification.md` and `docs/Wasnie_Business_Brief.docx`.

---

## Founder + Stack

**Founder:** Rodolfo Calvo, based in Katowice, Poland. Solo founder during current development phase.

**Backend:** ASP.NET Core 8 + C#, MediatR, FluentValidation, AutoMapper, EF Core, MS SQL Server, JWT, Serilog. Clean Architecture (Domain, Application, Infrastructure, Api).

**Frontend:** Angular 20 standalone components with signals, Tailwind CSS, ngx-translate (EN/ES/PL). Testcontainers for integration tests. Jasmine/Karma for unit tests.

**Deployment:** Azure App Service (Free F1, West Europe). Azure DevOps CI/CD. Currently deploys directly from main branch on push.

**Repo location:** `C:\Users\fillo\Documents\Sales\Wasnie\` (Windows dev environment, Visual Studio 2022 + VS Code).

---

## Where we are in the Master Plan

```
✅ Phase 0 — Foundation (done before May 2026)
✅ Phase 1 — Plans, payees, quotas, assignments, basic import (done — Phase A closed 2026-05-26)

PHASE B — Architecture & Quality Standards
✅ B0 — Product docs (User Personas + Business Brief, done 2026-05-26)
✅ B1 — ARCHITECTURE.md + 14 section files (done 2026-05-26)
✅ B2 — Codebase audit (done 2026-05-27) — see docs/audit/Audit_Findings.md
✅ B3 — Prioritized backlog (Audit_Backlog.md, done 2026-05-27)

✅ PHASE C — Critical Quality Gaps — OFFICIALLY CLOSED (2026-05-27)
✅ C1 — Server-side pagination (done in Phase A via prompts 39-43)
✅ Wave 1 (Security Hardening):
     WI-01 ✅ JWT lifetimes tightened
     WI-02 ⏭️ Email verification (deferred to Phase 5-6, scope documented)
     WI-03 ✅ Logout token invalidation + Refresh validator
✅ Wave 2 (Multi-tenant):
     WI-04 ✅ Tenant-scoped import cache
     WI-05 ✅ Multi-tenant defense hardening (global filters, IgnoreQueryFilters guard, TenantContext enforcement)
⏭️ Wave 3: WI-06 Clean Architecture fixes (F-001, F-002) — DEFERRED (architectural pragma; see decisions)
✅ Wave 4: WI-07 IClock + IGuidGenerator (F-003, F-004)
✅ Wave 5: WI-08 Audit trail foundation (F-014)
✅ Wave 6a: WI-09a RBAC + tier limits — backend (F-009)
✅ Wave 6b: WI-09b Frontend RBAC integration
✅ Wave 7: WI-10 Validators + cross-tenant test coverage (F-016, F-017, F-023)
✅ Wave 8: WI-11 Security middleware: headers + rate limiting + password policy (F-010, F-011, F-013, F-025)
✅ Wave 9: WI-12 Observability foundation (F-027)
✅ Wave 10: WI-13 Cleanup of low findings (F-020, F-021, F-024)

PHASE D — Phase 1 officially closed (~1 week)
⏭️ D1 — Backend coverage > 80%
⏭️ D2 — Frontend coverage > 60%
⏭️ D3 — E2E happy path tests (includes deferred A4)
⏭️ D4 — Master Spec v2.1 + docs update

PHASE 2+ — Transactions, Calculation Engine, Visibility, etc.
⏭️ Available to start (Phase D is optional polish, not a hard blocker)
```

For full plan details: `docs/Wasnie_Master_Plan_Phase_1_Closure.md`

---

## Audit findings summary (B2 results — Phase C closure state, 2026-05-27)

27 total findings. **23 closed as of Phase C closure.**

| Severity | Count | Open | Closed |
|---|---|---|---|
| 🔴 Critical | 8 | 1 | 7 (F-003, F-004, F-005, F-006, F-007, F-009, F-008 via deferral) |
| 🟠 High | 7 | 2 | 5 (F-010, F-011, F-012, F-014, F-015) |
| 🟡 Medium | 8 | 1 | 7 (F-013, F-015, F-016, F-017, F-018, F-019, F-023, F-025) |
| 🟢 Low | 4 | 0 | 4 (F-020, F-021, F-022, F-024) |

**23 closed:** F-003, F-004, F-005, F-006, F-007, F-008 (via deferral), F-009, F-010, F-011, F-012, F-013, F-014, F-015, F-016, F-017, F-018, F-019, F-020, F-021, F-022, F-023, F-024, F-025, F-027

**4 deferred with documented rationale:**
- **F-001 / F-002** — Clean Architecture violations (WI-06 deferred; pragmatic compromise documented in ARCHITECTURE.md §1.2; revisit when team grows or compliance demands)
- **F-008** — Email verification (WI-02 deferred to Phase 5-6; `IEmailService` abstraction ready for 1-line swap when paying customer arrives)
- **F-026** — Legacy Plan entity (`Wasnie.Domain.Entities.Plan`): reframed as intentional dual representation (EF persistence entity vs DDD domain entity); consolidation deferred; has active references throughout EF model
- **F-028** — Cross-tenant operations return 422 (UnprocessableEntity) instead of 404 for some endpoints; confirmed SYSTEMIC across Quotas/Assignments/PlanRules/Imports; deferred to future API contract standardization WI

### Phase C accomplishments

- **Tests grew from 210 → 339** (138 unit + 201 integration), zero regressions throughout
- **Security hardened end-to-end:** JWT lifetimes, token revocation, rate limiting, password policy, lockout, security headers, HSTS, CSP
- **Multi-tenant isolation production-grade:** global query filters on all 11 scoped entities, cross-tenant tests on every major endpoint
- **RBAC fully functional:** 4 roles, permission matrix, tier limits enforced, `*hasPermission` directive/pipe/guard, `/auth/me` endpoint
- **Audit trail in place:** immutable AuditLog entity with SQL trigger, 7 handlers retrofitted, 7-year retention ready
- **Observability foundation:** structured JSON logging, CorrelationId middleware, Serilog enrichment (TenantId, UserId, CorrelationId), frontend ErrorTrackingService + GlobalErrorHandler
- **Build clean throughout:** zero new warnings introduced in any WI

**Positive compliance areas** (no findings):
- All controllers are thin MediatR delegates
- Server-side pagination on every list endpoint
- EF Core global query filters on ALL 11 tenant-scoped entities (fully compliant after WI-05)
- No HttpClient in components (frontend HTTP architecture clean)
- No SQL injection (EF Core LINQ everywhere)
- Integration tests use real Testcontainers MSSQL
- No console.log in frontend
- CORS not wildcarded
- Multi-tenant isolation fully compliant (confirmed by WI-05 codebase audit)

For full audit: `docs/audit/Audit_Findings.md` | Backlog: `docs/audit/Audit_Backlog.md`

---

## Active work / current focus

**Right now we are:** Import pipeline complete and real-data tested (2026-05-29). **Next session MUST start with WI-PROD-MODEL conversation** — real-data testing with a 3,183-row retail POS export surfaced domain-model assumptions incompatible with the retail SPM target market (required email, required hire date, required PayeeId on transaction). All conversation items are in the new backlog section below. Only after WI-PROD-MODEL is resolved can further import or transaction WIs proceed without risk of rebuilding. Backend tests: 561 passing (217 unit + 344 integration). Frontend tests: 138 passing.

**Most recent significant work (2026-05-29 — WI-P2-04a-fix2: Excel native DateTime parsing):**
- **Root cause:** `cell.GetString()` on `XLDataType.DateTime` cells produced culture-dependent strings (`"4/1/2026 10:21:04 AM"`), rejected by the validator. All rows from real POS exports fail date validation.
- **Fix:** New `FileParserService.ReadCellAsString(cell)` — `DateTime` cells → ISO 8601 `"yyyy-MM-dd"` (InvariantCulture, time dropped); `Number` cells → `double.ToString(InvariantCulture)` (no currency formatting). Applies to all XLSX imports (payees and transactions).
- **Validator fix:** `TransactionImportValidationService.TryParseDate` now uses `CultureInfo.InvariantCulture` instead of `null` (thread culture) in `DateOnly.TryParseExact`.
- **Error message:** Bad date now shows the actual cell value: `"'hello' is not a recognisable date. Use YYYY-MM-DD."` (not the old misleading generic message).
- **Binding rule added:** `14-forbidden-patterns.md` — `cell.GetString()` on typed cells forbidden; `TryParseExact` must use InvariantCulture.
- **Tests:** +9 (3 parser: DateTime smoking-gun, culture independence pl-PL, Number invariant; 6 validator: formats theory, garbage message, culture independence, min boundary).
- **Test count: 561 passing (217 unit + 344 integration), 0 regressions.**

**Most recent significant work (2026-05-29 — WI-P2-04a-fix: row limit 300 → 10,000, configurable):**
- `MaxRows = 300` hardcoded constant removed from `FileParserService`. Added `ImportOptions` (Application) bound via `IOptions<ImportOptions>` from `appsettings.json` section `"Imports": { "TransactionMaxRows": 10000, "PayeeMaxRows": 300 }`.
- `IFileParserService.ParseAsync` signature updated: accepts `int maxRows` parameter. Controllers pass `Imports.TransactionMaxRows` or `Imports.PayeeMaxRows` per caller — parser stays stateless.
- Startup validation: `ValidateOnStart()` rejects limits outside [1, 100,000].
- **Payee import stays at 300** — synchronous path, Rule 3.2.5 applies.
- New endpoint `GET /api/imports/transactions/limits` returns `{ maxRows }`. Frontend upload-step fetches it on init; `CONSTRAINT_ROWS` i18n key now parameterised with `{{ count }}` in EN/ES/PL.
- **Tests:** +5 (boundary exact-at-limit CSV+XLSX, one-past-limit CSV+XLSX, transaction-limit-accepts-301-rows).
- **Test count: 552 passing (217 unit + 335 integration)** (before fix2).

**Most recent significant work (2026-05-29 — WI-P2-04b: Transaction import wizard UI):**
- 5-step wizard (`upload → map → preview → progress → complete`) at `/transactions/import`. Mirrors payee wizard; the Progress step is new.
- `TxProgressStepComponent`: polls `GET /api/jobs/{id}` every 3 s via `timer(0, 3000)` + `takeUntilDestroyed`. Stops on `Succeeded`/`Failed` (explicit `_polling.unsubscribe()`) AND on component destroy (`takeUntilDestroyed`). Transient network errors set `netError` signal without failing the job.
- Column auto-detect (`detectField()`) covers EN/ES/PL patterns for 6 transaction fields.
- Route `/transactions/import` gated to `Transactions.Create`. Sidebar entry added.
- Progress bar: LOCAL CSS only in `progress-step.component.scss` (not shared `WsProgressBar` — pending elevation to design system when 2+ features use it, per §10.3).
- **Frontend tests:** +40 (service HttpTestingController, progress step fakeAsync zombie-poll guard, mapping auto-detect).
- DESIGN_SYSTEM.md updated with 5-step async wizard variant and polling pattern.

**Most recent significant work (2026-05-28 session — WI-P2-BG-a):**
- **Architecture decision:** Hangfire 1.8.x (LGPLv3 — NOT MIT, correction from inspection) over hand-rolled SQL jobs. Azure F1 plan: no Always On, app unloads after ~20 min idle; Hangfire jobs survive recycle in SQL. B1 upgrade ($13/month, Always On) deferred to first paying customer.
- **New Domain:** `BackgroundJobRecord` entity + `JobState` enum (`Wasnie.Domain.BackgroundJobs`). EF migration `20260528135529_AddBackgroundJobs` (table + 2 indexes). Query filter tenant-isolates the table.
- **New Application interfaces:** `IBackgroundJobService`, `IJobHandler<TPayload>`, `IJobHandlerBase`, `JobHandlerBase<T>` abstract base, `JobStatusDto`, `JobContext`, `GetJobStatusQuery` (MediatR).
- **Critical DI change:** `ApplicationDbContext.CurrentTenantId` changed from eager (`{ get; } = tenantContext.TenantId`) to lazy (`=> tenantContext.TenantId`). EF evaluates query filters per-query, not at construction — allows background job scopes to set tenant before first query. **Regression discovered and fixed:** `AuthorizationService` catch-all was swallowing `UnauthorizedAccessException` from `tenantContext.TenantId`, turning expected 401s into 403s. Fixed by adding `catch (UnauthorizedAccessException) { throw; }`.
- **BackgroundJobTenantContext** (Infrastructure): Scoped, mutable, THROWS if `TenantId` is read before `SetTenant()`. Registered via factory: HTTP scope → `TenantContext`; no-HTTP scope → `BackgroundJobTenantContext`. `HangfireJobDispatcher` calls `SetTenant(tenantId)` from payload as FIRST action, before any DB access.
- **Hangfire wiring:** `HangfireJobDispatcher` (non-generic entry point), `HangfireBackgroundJobService`, `PingJobHandler` (smoke test), `HangfireDashboardAuthorizationFilter` (dev-only, blocked in Production until SystemAdmin role added — cross-tenant data risk).
- **API:** `GET /api/jobs/{id}` — authenticated, tenant-isolated (cross-tenant → 404). Dashboard at `/jobs` (dev-only, auth required).
- **Tests:** 217 unit (unchanged) + 277 integration (+34 vs 243 baseline). `BackgroundJobTenantContextTests` (5): throw-before-set, empty guid rejected, happy path, SetTenant twice. `PingJobIntegrationTests` (1): end-to-end enqueue → execute → Succeeded, cross-tenant isolation. All regression tests green.
- **TestWebApplicationFactory**: adds `ConnectionStrings:DefaultConnection` override so Hangfire uses the Testcontainers SQL Server in all integration tests.

**Most recent significant work (2026-05-28 session — WI-P2-FIX-select):**
- `ws-select.component.ts`: additive async mode — `searchFn` input (`(q: string) => Observable<SelectOption[]>`), `initialOption` input for edit-mode label resolution, `asyncOptions`/`asyncLoading` signals, `DestroyRef`-based `switchMap` pipeline with 300ms debounce + `takeUntilDestroyed`; `options` input changed from required to optional
- `ws-select.component.html`: search input shown when `searchable() || searchFn()`; animated 3-dot loading indicator (`asyncLoading()`); empty state guarded by `!asyncLoading()`
- `ws-select.component.scss`: `.ws-select__loading` (3-dot animated indicator using `@keyframes ws-select-dot`)
- `transaction-form.component.ts`: removed `PayeesStore` dependency, added `PayeesApiService` + `payeeSearchFn`; removed `OnInit`/`ngOnInit`/`payeeOptions` signal
- `payee-form.component.ts`: removed `managerOptions` signal + `_rebuildManagerOptions` + `ngOnInit`; added `PayeesApiService` + `managerSearchFn` + `managerInitialOption` computed (reads `payee().managerId/managerName/managerEmployeeCode`)
- `assignment-create.component.ts`: removed `PayeesStore`/`PlansStore`; added `PayeesApiService`/`PlansApiService` + `payeeSearchFn`/`planSearchFn`; added `preselectedPayeeOption`/`preselectedPlanOption` signals; constructor subscription for `planId.valueChanges → plansApi.getPlan() → patchValue(dateRange) + preselectedPlanOption`; `ngOnInit` handles queryParam preselection via `firstValueFrom`
- `quota-create.component.ts`: removed `PayeesStore`/`PlansStore`; added `payeeSearchFn`/`planSearchFn`; `preselectedPayeeOption` signal for payeeId queryParam
- All 4 HTML templates updated (`[searchFn]` + `[initialOption]` replacing `[options]` + `[searchable]`)
- `transaction-form.component.spec.ts`: updated to mock `PayeesApiService` instead of `PayeesStore`
- `ws-select.component.spec.ts` (NEW): 16 tests — client-side unchanged behavior (7), async debounce, loading state, empty state vs loading, initialOption fallback, asyncOptions priority over initialOption, initial load on open
- `DESIGN_SYSTEM.md`: WsSelect async mode subsection added
- **Frontend test count: 98/98 pass, build clean**

**Most recent significant work (2026-05-28 session — WI-P2-03c):**
- `transaction.model.ts`: `Transaction`, `TransactionStatus` (Pending/Eligible/Calculated/Paid/Cancelled), `TransactionSource`, `CreateTransactionRequest`
- `TransactionsApiService`: `list(params)`, `getById(id)`, `create(request)` — mirrors Payees service pattern
- `TransactionsStore`: signals-based store with `effect()` auto-reload, status filter, paginated state, `createTransaction()` + `loadTransactions()`
- `TransactionsListComponent`: status segmented-control filter, paginated table with skeleton rows, status badges (Pending=warning, Eligible=info, Calculated=brand, Paid=success, Cancelled=neutral), payee name lookup via injected `PayeesStore`, `*hasPermission="'Transactions.Create'"` gates New button
- `TransactionFormComponent`: Payee select (full row), Reference number input (full row), Transaction date (col1) + Amount+Currency amount-pair (col2); source shown as read-only info text; form validates required + amount > 0
- `TransactionCreateComponent`: thin wrapper navigates back to `/transactions` on save/cancel
- `transactions.routes.ts`: `''` → list, `'new'` → create
- **Bugs fixed:** `app.routes.ts` changed from `loadComponent` + `Reports.ViewAll` → `loadChildren` + `Transactions.Read`; `sidebar.component.ts` changed `Reports.ViewAll` → `Transactions.Read`
- **i18n:** TRANSACTIONS namespace added to EN/ES/PL (29 keys each)
- **§5b.8 disclaimer: NOT required** — raw sales transactions are objective facts, not estimated commission amounts
- **Payee name resolution:** client-side lookup via `PayeesStore.payees()` — tech debt; backend DTO enhancement (`PayeeName` field) would be cleaner
- **17 new frontend tests:** 4 service (HttpTestingController), 7 store (signals, filter, error), 6 form (validation, submit, cancel)
- **Frontend test count: 17 new (all pass)**

**Previous significant work (2026-05-28 session — WI-P2-03b):**
- `ListTransactionsQuery` + `GetTransactionByIdQuery` in Application, matching the Payees list pattern exactly
- `ListTransactionsHandler`: RBAC (`Permission.TransactionsRead`), sort whitelist (`transactionDate`/default, `amount`, `status`, `ingestedAt`, `referenceNumber`), unknown sort → safe fallback (no 500), filters: status (name-based `Enum.TryParse`), payeeId, source, dateFrom, dateTo; paginated via `ToPagedResultAsync`
- `GetTransactionByIdHandler`: RBAC, `FirstOrDefaultAsync` → global filter → cross-tenant null → 404
- `TransactionsController`: `GET /api/transactions` + `GET /api/transactions/{id}` added
- `PaginationQuery` extended with `Source`, `DateFrom`, `DateTo` (backward compatible; existing handlers ignore them)
- Migration `P2_TransactionReadIndexes`: 3 new read-path indexes — `(TenantId, TransactionDate)`, `(TenantId, Status)`, `(TenantId, IngestedAt)` per Rule 3.2.2
- **Amount sort flagged:** no index on `(TenantId, Amount)` — included in whitelist but may need index at Enterprise scale (10k+ txns). See decision #33.
- 27 integration tests in `TransactionReadEndpointsTests`: default pagination, page 2, pageSize>100 → 400, sort by each whitelisted field (asc/desc), invalid sort fallback, all 5 filters, combined filter+sort+pagination, 401, 403×3 (Manager/Rep/CompManager passthrough), cross-tenant list, cross-tenant get-by-id → 404, nonexistent → 404, happy path get-by-id
- **Test count: 488 passing (217 unit + 271 integration), 2 intentionally skipped**

**Previous significant work (2026-05-28 session — WI-P2-03a):**
- `IngestTransactionCommand` (implements `IMoneyCriticalCommand`, mutable `AuditResourceId` so handler sets it after SaveChanges)
- `IngestTransactionCommandValidator` (sync FluentValidation: ReferenceNumber not-empty/≤200, PayeeId not-empty, Amount > 0, Currency exactly 3 chars, TransactionDate ≥ 2000-01-01)
- `IngestTransactionHandler`: RBAC (`Permission.TransactionsCreate`), payee existence check (tenant-scoped), `Money.Of`, `TransactionSource.Manual`, `CompensationTransaction.Ingest`, `SaveChangesAsync`, sets `request.AuditResourceId`; no `IAuditService` injection — `AuditBehavior` handles audit atomically
- `TransactionsController`: thin MediatR delegate, `POST /api/transactions`, 201 + Location header on success
- `Permission.TransactionsCreate` + `Permission.TransactionsRead` added to Domain and granted to TenantAdmin + CompManager
- `AuditActions.TransactionIngested = "TRANSACTION_INGESTED"` added
- 16 unit tests (`IngestTransactionCommandValidatorTests`) + updated `RolePermissionsTests` (6 new inline cases)
- 14 integration tests (`TransactionsEndpointsTests`): happy path, auth, authz (3 roles), 4 validation rules, payee-not-found, 2 cross-tenant isolation, money-critical audit atomicity
- `TestDatabaseFixture.ResetTransactionsAsync()` added
- **Test count: 460 passing (217 unit + 243 integration), 2 intentionally skipped**

**Previous significant work (2026-05-28 session — WI-P2-02):**
- `CompensationTransactionStatus` enum replaced with full spec lifecycle: `Pending=0, Eligible=1, Calculated=2, Paid=3, Cancelled=4` — `Credited` removed (table was write-orphan; safe destructive replacement per §8.4.1)
- `ExternalReference` field renamed to `ExternalId` everywhere (entity, EF config, migration) — aligns code with spec §5.3.1 naming
- Migration `P2_TransactionDomainSurgery`: `sp_rename ExternalReference→ExternalId` + filtered unique index `(TenantId, Source, ExternalId) WHERE ExternalId IS NOT NULL`
- `Ingest` factory now enforces invariants: empty `TenantId`/`PayeeId` (Guid.Empty), null/blank `referenceNumber`, null/empty `ingestedBy`, `transactionDate < 2000-01-01` — all throw `DomainException`; no `DateTime.UtcNow` introduced (Rule 2.5.3)
- `MarkEligible(updatedBy, now, eventId)` added: Pending → Eligible, raises `TransactionMarkedEligibleEvent` (new event)
- `MarkCredited` removed; replaced by `MarkCalculated`/`MarkPaid` Phase 3 stubs (throw `NotSupportedException`)
- `Cancel` updated: Pending/Eligible → Cancelled allowed; Calculated/Paid → DomainException with Phase 3 clawback note
- §5b.7 gap closed: every state-change method now raises a domain event
- EF1002 warning eliminated: `MultiTenantDefenseTests.cs:47` switched from `ExecuteSqlRawAsync` (concatenated GUIDs) to `ExecuteSqlAsync(FormattableString)` (parameterized)
- 27 domain unit tests (`CompensationTransactionTests`) + 5 integration tests (`CompensationTransactionIdempotencyTests`: unique violation, null-ExternalId exemption, cross-tenant allow, global filter isolation)
- **Test count: 419 passing (190 unit + 229 integration), 2 intentionally skipped — promoted to 460 by WI-P2-03a**

**Previous significant work (2026-05-28 session — WI-P2-01b):**
- `[JsonConstructor]` removed from `Money.cs`; `using System.Text.Json.Serialization` removed — Rule 1.5 violation fully resolved
- `MoneyJsonConverter : JsonConverter<Money>` added to `Wasnie.Infrastructure.Persistence.Serialization` — wraps `DomainException` from `Money.Of()` as `JsonException` with inner exception; backward-compatible output format (`{"amount":<decimal>,"currency":"<ISO3>"}`)
- Registered in `PlanRuleConfiguration` and `PayoutLineConfiguration` via `BuildJsonOptions()` factory; registered globally in `Program.cs` via `AddControllers().AddJsonOptions(...)` (covers HTTP layer: `AddRuleToPlanCommand.Cap/Floor`)
- Two bugs fixed during the run: (1) `DomainException` not wrapped as `JsonException` inner exception — caught and re-thrown; (2) `[Trigger]` must be bracket-quoted in raw SQL — reserved keyword in SQL Server
- 17 serialization unit tests (`MoneyJsonConverterTests`) + 3 DB round-trip integration tests (`MoneyRoundTripTests`)
- Round-trip tests use `ExecuteSqlAsync(FormattableString)` — EF Core parameterizes each interpolation hole; JSON `{...}` content in variables is never treated as placeholder syntax
- **Test count: 387 passing (163 unit + 224 integration), 2 intentionally skipped**

**Previous significant work (2026-05-28 session — WI-P2-01):**
- `Money` value object refactored to full §5b.5 compliance:
  - 4-decimal internal normalization with banker's rounding (`MidpointRounding.ToEven`) in private constructor — every code path goes through it
  - `Negate()` and `Abs()` methods added
  - Four comparison operators (`>`, `<`, `>=`, `<=`) added — same-currency only, throws `DomainException` on currency mismatch
  - `GuardSameCurrency` changed to `private static` to support operator usage
- 25 new unit tests covering all new behaviors: normalization, banker's rounding midpoints, Negate, Abs, comparison operators (same + different currency), equality regression guard
- Data safety verified: grep across all .cs and .json found no persisted monetary value exceeding 4 decimals — normalization is safe
- **Test count: 367 passing (163 unit + 204 integration), 2 intentionally skipped**

**Previous significant work (2026-05-28 session — WI-P2-00):**
- `IMoneyCriticalCommand` marker interface introduced (`Wasnie.Application/Common/Interfaces`)
- `IApplicationDbContext.Database` property exposed (consistent with F-001 deferral)
- `AuditBehavior` extended with transactional money-critical path; non-money path byte-for-byte unchanged
- 3 new integration tests in `MoneyAuditTransactionTests` (own Testcontainers fixture, isolated from shared fixture)

**Previous significant work (2026-05-27 session — Phase C OFFICIAL CLOSURE):**
- WI-09a: Backend RBAC + tier limits — 4 roles, 29 handlers refactored, /auth/me endpoint, 50 new tests; 280/280 pass
- WI-09b: Frontend RBAC integration — CurrentUserService (signals), *hasPermission directive/pipe/guard, forbiddenResponseInterceptor, TierLimitModal, sidebar gating; 59/59 frontend tests pass
- WI-10: 3 new validators + 3 new integration test files (Quotas, Assignments, PlanRules), 44 tests; F-028 confirmed systemic; 324/324 pass
- WI-13: F-020 safety comments, F-021 token fix, F-024 confirmed + CONTRIBUTING.md, F-026 reframed
- WI-11: SecurityHeadersMiddleware, rate limiter, password/lockout hardening, HSTS; 7 tests
- WI-12: CorrelationIdMiddleware (first in pipeline), Serilog JSON config-driven, TenantUserCorrelationEnricher, frontend ErrorTrackingService/GlobalErrorHandler/correlationIdInterceptor; 14 tests
- **Final count: 339 tests pass (138 unit + 201 integration), 2 intentionally skipped; build clean**

**Not yet started:**
- Phase D (coverage > 80% backend / > 60% frontend, E2E tests, Master Spec v2.1) — optional
- WI-P2-03c — manual transaction entry UI (needs `DESIGN_SYSTEM.md`) ← next recommended
- Phase 2 Calculation Engine — core product IP
- Marketing / content strategy (planned for parallel work once Phase 1 fully closed)

---

## Important decisions made

1. **Document hierarchy:** ARCHITECTURE.md > Product Spec > DESIGN_SYSTEM > Master Plan. Conflicts resolved in this order.
2. **Strict architecture enforcement:** Claude (chat) acts as gatekeeper. Refuses prompts that violate ARCHITECTURE.md.
3. **All technical docs in English.** Chats in Spanish.
4. **Personal Trainer background NEVER mentioned** in Wasnie context (separate professional identity).
5. **Claude Code autonomy boundary:** auto-approve file/build/test, NEVER autonomous git operations.
6. **A4 (E2E tests) deferred to Phase 9** (Compliance & Enterprise readiness).
7. **Subscription tiers:** Free / Starter (€300) / Growth (€800) / Scale (€1,800) / Enterprise (€2,500+).
8. **Target markets in order:** Poland → Central & Eastern Europe → Iberian & LATAM markets.
9. **Mobile responsive deferred to Phase 8.** Desktop-only until then (1280px+).
10. **Email provider (WI-02) deferred to Phase 5-6.** Real email service (Postmark/SendGrid/AWS SES) integrated when first paying customer is identified. `IEmailService` abstraction + DI swap requires only infrastructure changes when ready. (2026-05-27)
11. **Multi-tenant isolation confirmed fully compliant** after WI-05. All 11 tenant-scoped entities have global query filters; only 1 `IgnoreQueryFilters()` in source (now guarded). (2026-05-27)
12. **TenantContext null-HttpContext behavior:** Returns `Guid.Empty` for null `HttpContext` (background services, test fixture cleanup scopes). Throws `UnauthorizedAccessException` only when authenticated request lacks valid `tenant_id` claim. Revisit when observability (WI-12) provides alerts on suspicious empty-result queries. (2026-05-27)
13. **WI-06 strict Clean Architecture refactor deferred:** EF Core in Application and MediatR in Domain remain as documented pragmatic compromises. Violations documented in ARCHITECTURE.md §1.2. Revisit when team grows or when external compliance requires stricter purity. (2026-05-27)
14. **Audit trail pattern is hybrid:** Explicit `IAuditService.LogAsync(...)` calls for handlers where before/after diff or post-save resource ID is needed (5 handlers); `IAuditableCommand` marker + `AuditBehavior` pipeline for commands where resource ID is in the command (2 handlers). Both patterns are valid and co-exist intentionally. (2026-05-27)
15. **`AuditLog.Id` uses BIGINT (long) instead of GUID:** Better for high-cardinality, write-only audit table — improved index performance, smaller storage. UUID distribution not needed for a sequential write table. (2026-05-27)
16. **Audit dispatcher swallows failures in Phase 1:** Acceptable per Rule 5.3.3 for non-money operations. When Phase 2 (Transactions) starts, audit failures on money operations MUST cause transactional rollback. Flagged for Phase 2 pre-work. (2026-05-27)
17. **RBAC refactored all 29 existing handlers:** All command/query handlers now call `RequireAsync(permission)`. New Phase 2 handlers must include RBAC checks from inception. (2026-05-27)
18. **Manager/Rep scoped data access deferred:** Currently they can view any payee/plan in their tenant (not scoped to direct reports). Acceptable for Phase 1 launch with small tenants. Future enhancement WI when tenant size justifies it. (2026-05-27)
19. **Tier limits are count-based only:** Max payees and max plans enforced per tier. Feature-gate architecture prepared (`IFeatureGate` interface) but no specific feature gates registered yet. (2026-05-27)
20. **F-028 (cross-tenant 422 vs 404) confirmed systemic:** Quotas, Assignments, PlanRules, and Imports endpoints all return 422 for cross-tenant mutations. Tests accept both 422 and 404 (`.Should().Contain()`). Deferred to a dedicated API contract standardization WI. (2026-05-27)
21. **F-026 (legacy Plan entity) reframed as intentional dual representation:** `Wasnie.Domain.Entities.Plan` (LegacyPlan alias in DbContext) is the EF persistence entity; `Wasnie.Domain.Compensation.Plans.Plan` is the DDD domain entity. Consolidation would require full EF model migration — not worth the cost now. NOT a duplicate to delete. (2026-05-27)
22. **Rate limit tests: 2 skipped intentionally:** Testing that limiter fires at low limits requires an isolated factory per test class with a small PermitLimit. Shared fixture with 10000 limit doesn't support this. Manual verification with curl/wrk required before first production customer. (2026-05-27)
27. **`IMoneyCriticalCommand` chosen for money-critical audit path (WI-P2-00, 2026-05-28):** Option A (marker interface extending `IAuditableCommand`) selected over Option B (flag on dispatcher). Rationale: visible at command definition site, consistent with existing `IAuditableCommand` pattern, avoids leaking the concept through `IAuditService`/`IAuditDispatcher` signatures. `AuditBehavior` wraps money-critical commands in `db.Database.BeginTransactionAsync()` — the handler's `SaveChangesAsync` and the dispatcher's `SaveChangesAsync` both participate in the same transaction; `CommitAsync()` commits both atomically. No message queue or external outbox required at current scale. `IApplicationDbContext` now exposes `DatabaseFacade Database { get; }` (consistent with F-001 deferral — EF Core already in Application). All Phase 2 Transactions/Payouts/Credits commands MUST implement `IMoneyCriticalCommand`. (2026-05-28)
28. **`Money` value object uses 4-decimal internal precision with banker's rounding (WI-P2-01, 2026-05-28):** All `Money` values are normalized to ≤4 decimal places at construction time via `Math.Round(amount, 4, MidpointRounding.ToEven)`. Every arithmetic method (Add, Subtract, Multiply, Divide, Negate, Abs) goes through the private constructor, so re-normalization is guaranteed. `ToString()` rounds the stored 4-decimal value to 2 decimal places for display only — it never mutates `Amount`. (2026-05-28)
30. **`CompensationTransactionStatus.Credited` replaced by full spec lifecycle (WI-P2-02, 2026-05-28):** `Credited` (a Credit entity was allocated) was not equivalent to spec's `Calculated` (calc engine ran). Since `CompensationTransactions` was a write-orphan with zero handlers and no stored data, a destructive replacement was safe per §8.4.1. New enum: `Pending=0, Eligible=1, Calculated=2, Paid=3, Cancelled=4`. `MarkCalculated` and `MarkPaid` are Phase 3 stubs that throw `NotSupportedException`. (2026-05-28)
31. **Idempotency for `CompensationTransaction`: filtered unique index on `(TenantId, Source, ExternalId)` WHERE `ExternalId IS NOT NULL` (WI-P2-02, 2026-05-28):** Manual transactions (null `ExternalId`) are exempt from external idempotency — the filter prevents false unique conflicts. Cross-tenant: same `(Source, ExternalId)` under different tenants is allowed (TenantId is in the key). The existing `(TenantId, ReferenceNumber)` unique index is preserved for internal reference uniqueness. (2026-05-28)
29. **`MoneyJsonConverter` in Infrastructure replaces `[JsonConstructor]` in Domain (WI-P2-01b, 2026-05-28):** Rule 1.5 (no serialization attributes in Domain) now fully compliant. `MoneyJsonConverter : JsonConverter<Money>` lives in `Wasnie.Infrastructure.Persistence.Serialization`. It reads both number and string JSON types for `amount` (backward compat), uses `OrdinalIgnoreCase` for property names, and wraps any `DomainException` from `Money.Of()` as a `JsonException` with the domain error as inner exception. Output format is identical to what `[JsonConstructor] + JsonSerializerDefaults.Web` produced — zero stored-data migration needed. Registered per EF config via `BuildJsonOptions()` factory (not a shared instance, to avoid cross-config pollution) and globally via `AddControllers().AddJsonOptions(...)` for the HTTP layer. (2026-05-28)
33. **`Transactions.Read` granted to TenantAdmin + CompManager only (WI-P2-03b, 2026-05-28):** Manager/Rep scoped access is deferred per decision #18. Granting globally now would be a security regression when scoping is added (reps would see all tenants' transactions instead of only their own). Grant widens when Manager/Rep scoped access WI is implemented. Sort-by-amount has no index — included in whitelist but a full scan at Enterprise scale (10k+ txns) may exceed the < 200ms P95 baseline. Defer adding `(TenantId, Amount)` index until customer data reaches Scale tier. (2026-05-28)
32. **`Permission.TransactionsCreate` granted to TenantAdmin + CompManager only (WI-P2-03a, 2026-05-28):** Manager and Rep cannot create transactions — consistent with all financial mutation operations across Phase 1 and Phase 2. `Permission.TransactionsRead` also added; currently not enforced on any GET endpoint (no GET endpoints yet), but reserved for WI-P2-03b so the grant/deny matrix is complete from inception. `IngestTransactionHandler` checks `Permission.TransactionsCreate` as the first operation before any DB access. (2026-05-28)
23. **TenantUserCorrelationEnricher placed in Wasnie.Api/Observability/:** Serilog package is only referenced in Wasnie.Api; adding it to Wasnie.Infrastructure would require a new package reference. Enricher registered in DI via `AddSingleton<ILogEventEnricher>(...)` and picked up by `ReadFrom.Services()`. (2026-05-27)
24. **Operational logging in handlers: zero `_logger.` calls in src/:** The audit trail (WI-08) covers all business events. Structured operational logging in individual handlers is a candidate for a future WI but is not blocking Phase 2. (2026-05-27)
25. **Serilog fully config-driven:** Hardcoded `.WriteTo.Console()` and `.WriteTo.File()` removed from `Program.cs`. All sinks, levels, and enrichers now read from `appsettings.json` / `appsettings.Production.json`. Adding Application Insights requires one sink entry in config — no code change. (2026-05-27)
26. **Phase C completed in one day instead of estimated 3-4 weeks:** Disciplined ARCHITECTURE.md + WI prompt workflow + Claude Code autonomous execution collapsed 50-70h estimate to ~10-12 effective hours. Zero regressions, only documented justified deviations. (2026-05-27)
34. **Real-data testing (2026-05-29) exposed retail SPM domain-model gaps.** A 3,183-row Reserved Polska / Galeria Katowice POS export surfaced multiple domain-model assumptions that hold for B2B-tech SPM but not for retail SPM, which is the actual target market: (a) `Payee.Email` required blocks seasonal/temporary workers with no corporate email; (b) `Payee.HireDate` required blocks migrating tenants who only have "period of plan activity," not historical hire date; (c) `CompensationTransaction.PayeeId` required blocks e-commerce / house-pool / system-processed-return rows. **WI-PROD-MODEL conversation is the next session's first topic.** Further import or transaction WIs must not proceed without resolving this — building on broken invariants multiplies rework. (2026-05-29)

---

## Key naming conventions

- **Backend projects:** `Wasnie.Domain`, `Wasnie.Application`, `Wasnie.Infrastructure`, `Wasnie.Api` (note: not "Presentation", but `.Api`)
- **Frontend:** `WasnieUi/`
- **Reusable Angular components:** `Ws` prefix (`WsButton`, `WsCard`, `WsDataTable`, `WsPageLayout`)
- **Languages supported:** English (primary), Spanish, Polish
- **Image folder convention:** `WasnieUi/public/` (Angular 17+ pattern), not `src/assets/`

---

## Working conventions

- **Prompts to Claude Code:** Must reference ARCHITECTURE.md sections explicitly. Long prompts include the Autonomy Footer (file 13).
- **Visual changes:** Surgical, not systemic. Numerical specs only ("15% lighter," not "subtle"). Inspect existing structure before specifying replacement.
- **Breaking changes:** All consumers updated in same PR. Run FULL test suite, not partial.
- **Multi-tenant testing:** Every endpoint MUST have a cross-tenant test.
- **No commits by Claude Code.** Ever. Code/files/deps OK, git operations user-only.

---

## Open questions / pending decisions

(Update this section as questions emerge that need answers before proceeding)

- **⚠️ WI-PROD-MODEL — NEXT SESSION'S FIRST TOPIC (2026-05-29).** Real-data testing surfaced retail SPM domain gaps. See backlog section below. All further import and transaction WIs are soft-blocked until the required/optional field decisions are made.
- **Phase D or Phase 2 next?** Phase D adds coverage and docs polish but does not unblock new features. Phase 2 is the core product (Transactions + Calculation Engine). Recommended: Phase 2 directly; Phase D items can land incrementally alongside Phase 2.
- **WI-06 approach:** Strict purity refactor (remove MediatR from Domain, EF Core from Application) deferred. If revisited, must update ARCHITECTURE.md §1.2 and run full regression suite. This is a 6-8h investment with no user-visible value — justify against backlog priority at that time.
- **F-028 standardization timing:** Cross-tenant mutations return 422 (not 404) across Quotas/Assignments/PlanRules/Imports. Deferred to a dedicated API contract standardization WI. Decision needed: standardize on 404 (ARCHITECTURE.md §9.3.3) or document 422 as intentional? Recommend 404 when the WI runs.
- **Operational logging in handlers:** Zero `_logger.LogX` calls in src/. The observability infrastructure (WI-12) is in place but no operational log statements have been added to handlers yet. Candidate for a focused WI or as part of Phase 2 handler development.
- **Manager/Rep scoped data access:** Currently Managers and Reps can view all data in their tenant. Needs scoping to assigned payees/teams as tenant size grows. Future WI when justified by customer feedback.
- ~~**Phase 2 audit hardening:**~~ **RESOLVED (WI-P2-00, 2026-05-28).** `IMoneyCriticalCommand` marker interface introduced. `AuditBehavior` wraps money-critical commands in an explicit EF Core transaction — audit failure rolls back the business write and propagates the exception. Non-money behavior unchanged. 3 new integration tests (Testcontainers) prove both paths. See decision #27.
- **Rate limit manual verification:** 2 skipped rate limit tests require manual curl/wrk verification before first production customer. Run: `for i in {1..10}; do curl -s -o /dev/null -w "%{http_code}\n" -X POST /api/auth/login; done` and verify 429 after limit.

---

## Backlog — Discovered during real-data testing (2026-05-29)

Real POS export: Reserved Polska / Galeria Katowice, April 2026, 3,183 rows. After the two parser/date bugs were fixed, Upload + Map + Preview completed. Preview was blocked by expected "payee not found" (payees were not pre-loaded — intentional test). No further bugs in the wizard itself. The following items are **product/domain design decisions**, not code bugs.

---

### WI-PROD-MODEL — Retail SPM domain model review ⚠️ NEXT TOPIC

**Status:** Conversation pending. Blocks WI-PROD-A, and influences WI-P2-05 (calc engine).

**Problem:** Current domain model encodes B2B-tech SPM assumptions that conflict with retail SPM (the actual target market):

| Field | Current | Retail reality | Gap |
|---|---|---|---|
| `Payee.Email` | Required | Seasonal/temporary staff frequently have no corporate email | Blocks realistic payee import for retail |
| `Payee.HireDate` | Required | Migrating tenants often only have plan-activity dates, not historical hire dates | Blocks onboarding of existing workforce |
| `CompensationTransaction.PayeeId` | Required (FK, not nullable) | E-commerce self-service, house-pool sales, and system-processed returns are valid transactions with no salesperson | Blocks import of ~10-15% of a typical retail POS file |

**Proposed direction (owner to confirm):**

- `Email` → optional (nullable in DB, nullable in domain). Validation still enforces format if provided.
- `HireDate` → optional OR renamed to `ActiveFrom` (plan-period start date, not historical HR date). Decision affects how the Calculation Engine scopes eligibility — must be decided together.
- `PayeeId` on Transaction → configurable per tenant via a `RequirePayeeOnTransactions` boolean setting (default `true` for B2B-tech compat). Transactions with `PayeeId = null` when setting is `false` go to a house-pool or are skipped by the engine depending on Phase 3 design.

**Phase 3 cross-dependency (DO NOT defer):** The Calculation Engine (WI-P2-05) must decide how it handles `PayeeId = null` transactions BEFORE engine design starts. Options: (a) skip silently, (b) attribute to a "house pool" payee entity, (c) fail the calculation run with a clear error. This choice is part of WI-PROD-MODEL, not a Phase 3 concern to discover mid-implementation.

---

### WI-PROD-A — `RequirePayeeOnTransactions` tenant setting

**Depends on:** WI-PROD-MODEL decision on `PayeeId` optionality.

**Scope:** Backend: add `RequirePayeeOnTransactions` boolean to tenant configuration. Default `true`. `IngestTransactionCommand` validator and `TransactionImportValidationService` both read this setting — if `false`, `PayeeId` missing is a Warning (skipped row attribution) not an Error. Frontend: checkbox in account settings page.

---

### WI-PROD-B — Multi-sheet Excel: user-driven sheet selection

**Status:** Bug. Not yet implemented.

**Problem:** Both import wizards silently use `wb.Worksheet(1)` — the first sheet. The Reserved test file has three sheets; if the data sheet had not been first, the wizard would have silently imported junk.

**Proposed fix:** Between Upload and Map Columns, if the workbook has > 1 sheet, show a sheet-picker UI with the sheet names and a 3-row preview of each. Default to the first sheet but require explicit user confirmation. Applies to both Payee and Transaction wizards (same root — `FileParserService.ParseXlsx`).

---

### WI-PROD-C — First-import onboarding "valley of death"

**Status:** UX gap. Conversation pending.

**Problem:** A new comp manager uploads a transaction CSV before creating payees → 3,000+ "payee code not found" errors → product abandonment. The required order (payees first, transactions second) is invisible in the UI. During the Reserved test, the Import Transactions sidebar item was chosen instead of Import Payees; the wizard accepted the file and returned all errors.

**Three candidate directions (owner to choose):**
1. **Guided onboarding:** Detect empty-tenant state (zero payees). Redirect to payee wizard on first transaction import attempt with a clear message.
2. **Combined import:** Excel template with two named sheets — "Employees" and "Transactions" — imported in one wizard flow (parse both, create payees first, then transactions).
3. **"Create missing payees" in Preview:** At the transaction Preview step, if there are "payee not found" errors, offer an action to auto-create minimal payee records (code + name only) and re-validate. Simplest UX change; no pre-created payees required.

**Also needed:** Sidebar disambiguation. "Import Payees" and "Import Transactions" have equal visual weight — misleads first-time users into picking the wrong wizard. Consider grouping or labeling them.

---

### WI-PROD-D — Promote `WsProgressBar` to design system

**Status:** Deferred. Low urgency.

**Current state:** Progress bar is LOCAL CSS inside `transaction-import-progress-step.component.scss`. Pattern documented in `DESIGN_SYSTEM.md` as a step to elevate.

**Trigger for promotion:** When a second feature needs a progress bar (likely Phase 3 calculation engine runs, or payout processing). Promoting early adds design-system overhead for a single consumer.

---

### WI-PROD-E — Actionable "payee not found" error message

**Status:** Minor UX improvement. Mini-WI.

**Current:** `"Payee code not found."`

**Better:** `"Payee code 'EMP005' not found in this tenant. Create the payee first or correct the code in your file."`

**Scope:** 1-line change in `TransactionImportValidationService`. Add payee code to the error message. No model changes needed.

---

### WI-PROD-CURRENCY — Multi-currency UX design

**Status:** Design conversation needed before code. Connected to WI-PROD-MODEL.

**Problem:** Spec §5b.5 already decided no implicit currency conversion (cross-currency operations throw `DomainException`). However, the transaction list today mixes €, $, and PLN symbols inconsistently in the same Amount column — sometimes with two decimal places, sometimes without — because there is no enforced display convention for multi-currency rows.

**What needs deciding:** How multi-currency amounts are grouped, presented, and totaled in the transaction list and future reports. Questions: Does the list show a total? If yes, grouped by currency or suppressed entirely when currencies mix? What is the canonical display format per row?

**Likely resolution direction:** ISO-code prefix formatting per row (`PLN 376.99`, `EUR 8,500.00`), always two decimal places, no cross-currency totals in the list footer (total suppressed or replaced by per-currency breakdown). This matches Xactly / beqom conventions for multi-currency tenants.

**Scope when decided:** Display pipe/helper update, list column formatting, possibly a `CurrencyDisplayPipe` shared primitive. No domain changes (the domain already enforces same-currency arithmetic).

---

### WI-PROD-F — Server-side payee name resolution in the transaction list

**Status:** High priority. Visual confidence breaker for demos and customer trust.

**Problem:** The transactions list currently resolves payee names client-side via `PayeesStore` in memory. When the payee is not in the currently loaded page of the store, the UI displays the raw GUID (e.g. `b2d8ab2b-92ec-4e25-98bd-d8bd651e2ea0`). This was flagged as a tech-debt risk during WI-P2-03c and has now manifested in real testing.

**Fix direction:** Return a resolved `PayeeName` (or `PayeeDisplayName`) field in the `ListTransactionsHandler` DTO via an EF JOIN, not a client-side lookup. The client receives the display name directly and never needs to cross-reference the payee store. This is strictly a read-path DTO change — no domain or write-path changes required.

**Scope:** `ListTransactionsHandler` (add JOIN on Payees, project `p.FullName`), `TransactionListItemDto` (add `PayeeName` field), `TransactionsApiService` model, `TransactionsListComponent` template (remove `PayeesStore` dependency). One integration test update.

---

### WI-PROD-G — Tenant test-data reset mechanism

**Status:** Low priority. Developer convenience.

**Problem:** Manual testing accumulated noise rows — garbage transactions, GUIDs used as payee codes, mixed-currency amounts in the millions — that complicate clean re-tests and make screenshots unusable for demos.

**Proposed direction:** A developer/admin endpoint or seed-and-reset script scoped to a named test tenant. Options: (a) a `DELETE /api/dev/reset-tenant-data` endpoint (dev-only, behind environment guard), or (b) a standalone EF seeder script that wipes and re-seeds the test tenant to a known clean state. Not a migration — data-only operation.

**Scope:** Dev-only surface; zero Production impact. Can be a simple SQL script in `/scripts` rather than application code.

---

### WI-PROD-H — "New Transaction" button placement consistency ✅ DONE (2026-05-29)

**Status:** ~~Low complexity. Small UI polish.~~ **Closed.**

**Problem:** The "New Transaction" button on the transactions list page is positioned inconsistently relative to the "Add Payee" button on the payees list page. The Payees pattern is the canonical reference (CLAUDE.md §5.1).

**Fix:** Move the button to match the exact placement used on the Payees list. No UX design conversation needed — Payees is the model.

---

### WI-PROD-I — Search typeahead for the transactions list

**Status:** Medium priority.

**Problem:** The transactions list has no search input. Users cannot filter by reference number, payee name, or amount without scrolling through all rows. The payees list already implements the canonical pattern (debounced 300 ms, server-side filter, `WsInput` with `prefixIcon="search"`).

**Fix direction:** Mirror the payees list search pattern exactly. `ListTransactionsHandler` already accepts a `search` filter parameter (added in WI-P2-03b); the frontend just needs the input wired up. No backend changes required.

**Scope:** `TransactionsListComponent` — add search `WsInput`, debounce 300 ms via `Subject`, call `store.setSearch()`. `TransactionsStore` already has `setSearch`. Effort: ~1 h.

---

### WI-PROD-J — Transactions page summary widget: per-currency totals + time-series chart

**Status:** Higher complexity. Deferred until WI-PROD-CURRENCY is resolved.

**Problem:** The transactions list currently shows raw rows with no aggregate view. A comp manager needs to see at a glance: total revenue per currency in the selected period, and a trend over time.

**Proposed direction:** Add a summary section above the list with (a) per-currency total cards (no implicit cross-currency conversion — per Spec §5b.5 and WI-PROD-CURRENCY), and (b) a time-series chart of transaction volume by date with date-range filtering. Currency grouping and display format must follow the convention decided in WI-PROD-CURRENCY.

**Dependencies:** WI-PROD-CURRENCY (display convention must be decided first). Chart library not yet chosen — options include Chart.js (lightweight), Apache ECharts (richer), or ng2-charts wrapper. Library choice is a separate design decision.

**Scope when ready:** New `GET /api/transactions/summary` endpoint (aggregates server-side, grouped by currency + date bucket); summary component above the list; chart primitive decision.

---

```
docs/
├── PROJECT_STATUS.md                              ← THIS FILE
├── SESSION_LOG.md                                 ← session history
├── ARCHITECTURE.md                                ← master technical law
├── architecture/                                  ← 14 architecture section files
├── audit/
│   └── Audit_Findings.md                          ← B2 audit results
├── Wasnie_Product_Master_Specification.md         ← product spec
├── Wasnie_Master_Plan_Phase_1_Closure.md          ← operational plan (v1.1)
├── Wasnie_User_Personas.md                        ← user personas + JTBD
├── Wasnie_Business_Brief.docx                     ← external presentation
└── Wasnie_Informe_Tecnico.docx                    ← original market analysis (Spanish, historical)

WasnieUi/
└── DESIGN_SYSTEM.md                               ← frontend visual rules
```

---

## How to resume work in a new chat

When starting a new conversation with Claude, send this as your first message:

```
Hola. Soy Rudolf, founder de Wasnie (SPM/ICM SaaS).
Para retomar contexto rápido, por favor lee primero:
1. docs/PROJECT_STATUS.md (este archivo)
2. docs/SESSION_LOG.md (últimas 3 entradas)
3. docs/ARCHITECTURE.md (master, no las secciones)

Después dime "listo" y arrancamos con [tu tarea].
```

Claude will read these, summarize where we are, and you can proceed without re-explaining context.

---

## Update protocol

**This file MUST be updated:**
- After every significant work session (>30 min of progress)
- After every Phase completion or new Phase start
- After any major architectural decision
- After any audit, review, or status check

**Format:** Update the relevant sections directly. Bump the "Last updated" date. Append a brief note to SESSION_LOG.md.
