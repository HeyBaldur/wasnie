# 03 — Performance Baselines

**Reading time:** ~7 min
**Applies to:** Backend, Frontend, Database

---

## Why this matters

Performance is not a feature — it is a **trust signal**. A sales rep checking their commission dashboard at 9 AM Monday expects to see the result in under a second. If Wasnie takes 8 seconds, the rep concludes the system is broken. Trust lost.

Wasnie's target market is small-to-mid enterprises (20-300 reps). At that scale, performance issues mean:

- A 200-rep dashboard takes 30 seconds to load → SaaS replacement decision in 6 months
- A monthly payout calculation takes 4 hours → can't run during business hours → forced into nights → operations team unhappy
- A transaction import lock takes 30 seconds → other users see the app frozen

The rules below are **numerical and verifiable**. No adjectives. "Fast" is not a metric.

---

## 3.1 API response time

### Rule 3.1.1 — P95 baselines (production)

| Operation | P95 baseline |
|---|---|
| Read endpoints (list, detail) | **< 200 ms** |
| Write endpoints (create, update, delete) | **< 500 ms** |
| Bulk write endpoints (import, batch update) | **< 5 sec** for 300 records |
| Authentication endpoints (login, refresh) | **< 300 ms** |
| Calculation endpoints (commission preview) | **< 1 sec** |
| Report generation endpoints (Phase 2+) | **< 3 sec** for typical |

These are P95, not average. Average masks tail latency. The 5% of slow requests destroy trust.

### Rule 3.1.2 — How performance is measured

- Application Insights or equivalent (Phase C6 — Observability)
- Logged per endpoint with correlation ID
- Reviewed weekly during active development
- Alerts when P95 > 1.5× baseline for 1 hour

### Rule 3.1.3 — Degradation is a bug

A change that pushes P95 above baseline is a bug, regardless of correctness. Must be optimized before merge.

---

## 3.2 Database

### Rule 3.2.1 — Pagination is server-side ONLY

**FORBIDDEN:** fetching all records and paginating client-side. See file 14 for the forbidden list.

Max page size: **100**. Default: **25**.

### Rule 3.2.2 — Every query MUST be indexed

Any column used in `WHERE`, `ORDER BY`, or `JOIN` MUST have a database index. No exceptions.

Migrations that introduce queries without indexes are bugs.

### Rule 3.2.3 — N+1 queries are FORBIDDEN

If a request causes one query to fetch a list, then one query per item, that is an N+1. Always use:

- `.Include()` in EF Core for related data
- Projection (`.Select(...)`) when full entities aren't needed
- Explicit join or grouping when projection isn't enough

Code review MUST verify no N+1 in any new endpoint. Tests SHOULD count queries (e.g., using a counter interceptor) for critical paths.

### Rule 3.2.4 — `OrderBy` MUST use a whitelisted field

Sort fields must be validated against an explicit whitelist. NEVER pass user input directly to `OrderBy`. SQL injection through sort field is a real attack.

### Rule 3.2.5 — Transactions MUST be short

A database transaction that holds locks for > 5 seconds blocks other users. Forbidden in production code paths. If a long operation needs atomicity, design it as a saga or use eventual consistency.

### Rule 3.2.6 — Bulk operations MUST use batch inserts/updates

Inserting 300 records one at a time is forbidden. Use:
- EF Core `AddRange` + single `SaveChangesAsync`
- For very large batches (>1000), use `Microsoft.Data.SqlClient` bulk copy

---

## 3.3 Frontend

### Rule 3.3.1 — Initial render baseline

| Metric | Target |
|---|---|
| Time to First Byte (TTFB) | < 200 ms |
| First Contentful Paint (FCP) | < 1.5 sec |
| Time to Interactive (TTI) | < 3 sec on 3G |
| Largest Contentful Paint (LCP) | < 2.5 sec |
| Cumulative Layout Shift (CLS) | < 0.1 |

Measured with Lighthouse on the main pages (Dashboard, Payees list, Payee detail).

### Rule 3.3.2 — Bundle size budgets

- Initial bundle (main + vendor): **< 500 KB** gzipped
- Per lazy-loaded chunk: **< 200 KB** gzipped
- Per route: justified if larger

CI MUST fail if bundle exceeds budget (Phase C5).

### Rule 3.3.3 — No blocking calls in component initialization

Components MUST NOT block their `ngOnInit` waiting for data. Show skeletons / loading states immediately, resolve data asynchronously.

### Rule 3.3.4 — Pagination requests MUST be debounced for search

When user types in a search box, the resulting API call MUST be debounced 300ms to avoid hammering the backend.

### Rule 3.3.5 — Stale-while-revalidate for non-critical reads

For data that doesn't need to be perfectly fresh (e.g., user list in a dropdown), the frontend SHOULD show cached data while fetching fresh in background.

---

## 3.4 Real-time visibility (payees)

**Strategic note:** the Informe Técnico identifies that "only 52% of companies offer real-time commission tracking." Wasnie differentiates by being 100% transparent. This sets specific performance requirements:

### Rule 3.4.1 — Earnings dashboards MUST load < 1 sec

A payee viewing their current month earnings is the most common operation in Wasnie. It MUST feel instant.

Implementation strategy:
- Pre-computed earnings snapshots updated on transaction ingestion
- Cached for 60 seconds (acceptable staleness for non-finalized earnings)
- Materialized view in DB for very large data sets (Phase 2+)

### Rule 3.4.2 — Forecast calculations MUST complete < 2 sec

When a payee asks "what if I close deal X?", the answer MUST come within 2 seconds. This requires:
- Calculation engine optimized for incremental computation
- No round trips to external systems during forecast
- Pure in-memory rule application

---

## 3.5 Async / background processing

### Rule 3.5.1 — Operations > 5 sec MUST be async

Any operation that takes more than 5 seconds MUST be moved to background processing (hosted service, queue, worker). User gets immediate response with a job ID.

**Phase 1 exception:** Import is sync but limited to 300 rows. Phase 2 will move large imports to async.

### Rule 3.5.2 — Background jobs MUST be observable

Every background job MUST:
- Log start, progress, and completion
- Be queryable for status by user
- Surface failures with actionable error messages
- Support retry on transient failures (with exponential backoff)

---

## 3.6 Sizing the Wasnie target

| Tier | Payees | Transactions/month | Storage / customer / year |
|---|---|---|---|
| Starter | 25 | 500 | < 100 MB |
| Growth | 75 | 1,500 | < 500 MB |
| Scale | 150 | 3,000 | < 1.5 GB |
| Enterprise | 500+ | 10,000+ | < 10 GB |

Wasnie MUST handle Scale tier with the response times above. Enterprise tier optimization may require additional work in Phase 8+, but no architectural rewrites.

---

## 3.7 Forbidden performance anti-patterns

Repeated from file 14 for emphasis:

- Client-side pagination
- N+1 queries
- Unindexed queries in production paths
- Synchronous operations > 5 sec
- Storing computed values that should be cached (e.g., recomputing same calculation every request)
- Loading entire entity graphs when only a few fields are needed
- Blocking the UI thread waiting for HTTP

---

## Enforcement

- **Application Insights / OpenTelemetry** (Phase C6) tracks P95 per endpoint
- **CI bundle size check** (Phase C5) fails build if frontend bundle exceeds budget
- **EF Core query interceptors** in test environment count queries; tests fail on N+1
- **Lighthouse CI** (Phase C5) verifies frontend metrics on main pages
- **Weekly performance review** during active development

---

## Bug history

- **Phase 1 (May 2026):** Client-side pagination identified across all list endpoints. Fixed via prompts 39, 42, 43. Codified here as Rule 3.2.1.
- **Phase A audit:** Tests revealed filter parameters not applied → backend returned unfiltered results. Standardized to flat query params and explicit filter implementation.
