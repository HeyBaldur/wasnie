# Wasnie — Master Plan: Phase 1 Closure + Quality Foundations (v1.1)

**Status:** ACTIVE
**Owner:** Rodolfo A. Calvo Jaubert
**Created:** 2026-05-26
**Last Updated:** 2026-05-26 — Phase A signed off, lessons learned incorporated
**Purpose:** Close Phase 1 properly with full test coverage, then establish architectural and quality foundations before continuing to Phase 2+

This document is the single source of truth for current development priorities. All prompts must reference and respect this plan.

---

## Context — Where we are and why this exists

Phase 1 was scoped as: foundation, multi-tenant, plans, rules, payees, quotas, assignments, basic UI, import.

What was delivered (functionally): all of the above, with end-to-end working flows.

What was NOT delivered (quality / non-functional):
- ✅ Comprehensive test coverage across Import module (COMPLETE)
- ⏳ Server-side pagination (DONE in prompt 39, audited in prompt 42)
- ⏳ Claims/Authorization system (no subscription tier enforcement)
- ⏳ Audit trail for destructive operations
- ⏳ Security hardening (rate limiting, input validation review, secure headers)
- ⏳ Performance baselines verified
- ⏳ Quality gates in CI/CD
- ⏳ Observability (structured logging, metrics)
- ⏳ Architecture-level documentation enforced

**Decision:** before continuing to Phase 2 (Transactions and Calculation Engine), we close Phase 1 with **production-grade quality**. Wasnie is financial software; cutting corners now means lawsuits later.

---

## Master Plan Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│  PHASE A — Close Import feature properly       ✅ COMPLETE        │
│  ├── A1. UI polish + reusable components                ✅       │
│  ├── A2. Backend tests for Import                       ✅       │
│  ├── A3. Frontend tests for Import                      ✅       │
│  └── A4. End-to-end test of Import flow                ⏭️ Ph 9   │
│                                                                  │
│  PHASE B — Architecture & Quality Standards      (~2 days)       │
│  ├── B1. Create ARCHITECTURE.md master document   ← NEXT         │
│  ├── B2. Codebase audit against the document                     │
│  └── B3. Backlog of gaps prioritized                             │
│                                                                  │
│  PHASE C — Critical quality gaps                (~3-4 weeks)     │
│  ├── C1. Server-side pagination across all lists  ✅ done        │
│  ├── C2. Claims & Authorization (subscription tiers)             │
│  ├── C3. Audit trail standardized                                │
│  ├── C4. Security hardening                                      │
│  ├── C5. Quality gates in CI/CD                                  │
│  └── C6. Observability foundation                                │
│                                                                  │
│  PHASE D — Close Phase 1 officially              (~1 week)       │
│  ├── D1. Backend test coverage > 80% on Phase 1 modules          │
│  ├── D2. Frontend test coverage > 60% on critical components     │
│  ├── D3. End-to-end happy path tests (includes deferred A4)      │
│  └── D4. Documentation + Master Spec v2.1                        │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘

Original estimate: 6-7 weeks
Adjusted estimate: 5-6 weeks (Phase A completed ahead of schedule, A4 deferred)
```

After Phase D, Phase 2 (Transactions) can begin with confidence.

---

## PHASE A — ✅ CLOSED

**Status:** Signed off 2026-05-26 by Rudolf

**Deliverables completed:**

### A1. UI Polish + Reusable Components ✅
- `WsPageLayout`, `WsWizard`, `WsWizardStep`, `WsDataTable`, `WsStatCard` components created
- Import Wizard refactored
- Plan Detail, Payees List, Payee Detail use WsPageLayout
- Surface elevation token system in place
- DESIGN_SYSTEM.md updated

### A2. Backend Tests for Import ✅
- ~85 tests across FileParserService, PayeeImportValidationService, PayeeImportExecutionService
- Integration tests with Testcontainers (real SQL Server)
- Coverage > 85% on Import services
- Cross-tenant isolation verified
- Audit trail records verified
- Performance smoke tests pass

### A3. Frontend Tests for Import ✅
- 59 tests across FullName composer, column auto-detection, import service
- Coverage 95-97% on tested files
- HttpTestingController used for HTTP mocking
- Tests run in <300ms
- Logic extracted to pure functions for testability

### A4. End-to-end Test ⏭️ DEFERRED to Phase 9
- **Rationale:** Backend + frontend unit tests provide ~90% coverage of the feature. E2E framework adoption (Playwright vs Reqnroll) is a larger investment that doesn't pay off until Enterprise customers require formal audit trails. Will revisit in Phase 9 (Compliance & Enterprise readiness).
- **Documented for future:** Playwright recommended as the modern choice over Cypress and SpecFlow/Reqnroll. To revisit when first Enterprise customer signs.

---

## PHASE B — Architecture & Quality Standards (NEXT)

**Goal:** Establish a binding contract for what "production quality" means in Wasnie. All future development must comply.

### B1. Create ARCHITECTURE.md ← CURRENT

**Status:** To start after this update
**Estimated time:** 4-6 hours of focused work (Rudolf + Claude in chat)
**Deliverable:** A comprehensive markdown file in the repo root.

The document is built collaboratively — not generated by Claude Code. We work section by section, Rudolf approves each.

#### Required sections (with lessons learned from Phase A)

**1. Clean Architecture rules**
- Layer separation: Domain / Application / Infrastructure / Presentation
- Dependency direction: outer layers depend on inner, never reverse
- Domain entities have no framework dependencies
- Application services orchestrate; never contain business logic
- Forbidden: ASP.NET attributes on Domain entities, EF Core attributes on Domain entities
- Allowed: validation attributes on DTOs, EF configuration via Fluent API

**2. SOLID principles enforcement**
- Single Responsibility: each class one reason to change
- Open/Closed: extend via interfaces, not modification
- Liskov: derived types must be substitutable
- Interface Segregation: small focused interfaces, not god interfaces
- Dependency Inversion: depend on abstractions

**3. Performance baselines (non-negotiable)**
- API endpoint P95 response time: < 200ms for reads, < 500ms for writes
- Pagination: server-side ONLY, max 100 items per page
- Database queries: must have indexes on filtered/sorted columns
- N+1 queries: forbidden (use Include or projection)
- Frontend initial render: < 3 seconds on 3G
- Bundle size: max 500KB initial, max 200KB per lazy chunk

**4. Security requirements**
- All endpoints require authentication except `/auth/*` and `/health`
- All endpoints with mutations require authorization checks
- Tenant isolation enforced at query filter level
- Input validation on every request body
- SQL injection: only parameterized queries / EF Core, never string concatenation
- XSS prevention: Angular's built-in sanitization, no `[innerHTML]` with user content
- CSRF: tokens on state-changing requests
- Rate limiting on auth endpoints (5 attempts / 15 min)
- Secrets in environment variables / Azure Key Vault, never in code
- HTTPS only in production
- Secure headers: CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy

**5. Audit trail (financial compliance)**
- Every destructive operation logged: who, when, what, before/after values
- Every monetary operation logged with full context
- Audit log is append-only; never deleted, never edited
- Retention: minimum 7 years (financial compliance)
- Searchable by user, by tenant, by date range, by resource

**6. Authorization model**
- Subscription tiers: Free, Starter, Growth, Scale, Enterprise
- Each tier has feature flags and resource limits
- Limits enforced at API level (not just UI)
- Examples:
  - Starter: max 25 payees, no API access, no Salesforce integration
  - Growth: max 75 payees, basic API, no Salesforce
  - Scale: max 150 payees, full API, all integrations
  - Enterprise: unlimited, custom features
- User → Role → Permissions (role-based access control)
- Roles: Admin, Comp Manager, Manager, Rep
- Each role has explicit permissions matrix

**7. Testing standards**
- Backend: unit tests for all services, integration tests for all endpoints, > 80% coverage
- Frontend: component tests for all interactive components, > 60% coverage
- E2E: happy path of every critical user journey (Phase 9)
- Tests must use real database (Testcontainers), not mocks for repository tests
- Tests fail the build; warnings fail the build
- Pure function logic extracted from components for testability
- **Lesson from Phase A:** Every paginated endpoint MUST have explicit tests for filter, sort, search, AND combination scenarios

**8. CI/CD quality gates**
- Build fails if tests fail
- Build fails if coverage drops below thresholds
- Build fails if linter has errors
- Build fails if `npm audit` has high/critical vulnerabilities
- Build fails if dotnet has CVEs
- Build fails if there are `TODO` comments without issue links
- Build fails if there are `console.log` in production code
- Build fails if there are TypeScript `any` without justification comment
- Static analysis: SonarCloud or similar
- Security scan: Snyk, npm audit, dotnet outdated

**9. Observability**
- Structured logging (Serilog) with correlation IDs
- Every request has a correlation ID
- Logs in JSON format
- Levels: Trace / Debug / Info / Warning / Error / Critical
- Metrics: Prometheus / OpenTelemetry
- Distributed tracing: OpenTelemetry
- Error tracking: Sentry or similar
- Alerts on: P95 response time > baseline, error rate > 1%, failed login attempts spike, database connection pool exhaustion

**10. Breaking change protocol (NEW — lesson from Phase A)**

When a change modifies the signature of an endpoint, the shape of a DTO, or the behavior of a service, ALL consumers must be audited and updated in the same prompt. This includes:
- Integration tests
- Unit tests
- Frontend components/services consuming the endpoint
- OpenAPI/Swagger documentation
- Any generated API client

**Why this rule exists:** prompt 39 changed pagination endpoint shape and prompt 42 fixed filters. Both broke existing tests/frontend that were not part of the scope. Future prompts must explicitly enumerate ALL consumers in the acceptance criteria.

**11. Multi-tenant isolation testing (NEW — codified)**

Every endpoint with tenant-scoped data MUST have a test that proves:
- Tenant A user cannot see Tenant B's data
- Tenant A user cannot reference Tenant B's data (even by ID)
- File IDs / cache keys cannot be used across tenants

This is NOT optional. It's the #1 rule of multi-tenant SaaS.

**12. Claude Code Autonomy Boundary (NEW — codified)**

Claude Code may operate autonomously on:
- File system operations within the project
- Code generation, modification, deletion
- Dependency management (npm, dotnet)
- Build, test, lint commands
- Reading documentation and code

Claude Code may NEVER operate autonomously on:
- Git operations of any kind
- Production deployments
- Database operations against non-test databases
- External API calls that cost money or modify external state

The principle: Claude Code can modify the working copy but never committed state or external systems.

Every long prompt must include the autonomy footer (template in repo).

**13. Visual changes protocol (NEW — lesson from Phase A)**

Visual bugs are local. Visual prompts must be:
- Quirúrgico, no sistémico
- File list explicit (DO NOT touch app-shell, sidebar, header, etc.)
- Numerical values, not adjectives ("15% lighter", not "subtle elevation")
- Specific component named (not "all related components")

A visual issue in one component is NEVER an excuse to refactor 9 components.

**14. Forbidden patterns (recap)**
- Client-side pagination
- Mocking repositories in integration tests
- Storing secrets in code or git
- N+1 queries
- `any` in TypeScript without justification
- Console.log in production
- Modifying audit log records
- TODO comments without ticket links
- Skipping tenant filter
- String-concatenated SQL
- Unbounded `OrderBy` (sort field not in whitelist)
- Visual prompts that touch >5 components
- "Refactor" related styles when fixing one bug
- Nested filter query params (`?filter[X]=Y`) — use flat (`?X=Y`)
- Adding tokens for one-off use cases
- Hardcoded color values for backgrounds

### B2. Codebase audit against ARCHITECTURE.md

**Status:** After B1
**Estimated time:** 2-3 hours
**Deliverable:** `Audit_Findings.md` listing every violation found in current code.

Categories:
- Critical (security, data integrity)
- High (performance, compliance)
- Medium (maintainability)
- Low (cosmetic)

### B3. Prioritized backlog

**Status:** After B2
**Deliverable:** Backlog of work items ordered by priority. Becomes the Phase C plan.

---

## PHASE C — Critical Quality Gaps

**Goal:** Address every Critical and High finding from the audit before continuing to Phase 2.

### C1. Server-side Pagination ✅ DONE
Completed via prompts 39, 41, 42, 43. Standardized on flat query param format. Tests passing.

### C2. Claims & Authorization
**Status:** Pending
**Why critical:** Without this, any user can do anything. Subscription tier limits don't exist. Multi-tenant isolation exists but granular authorization doesn't.

(Details unchanged from v1.0)

### C3. Audit Trail Standardized
**Status:** Pending. Partially exists in ImportAudit (Phase A); needs generalization.

### C4. Security Hardening
**Status:** Pending

### C5. CI/CD Quality Gates
**Status:** Pending

### C6. Observability Foundation
**Status:** Pending

---

## PHASE D — Phase 1 Officially Closed

(Unchanged from v1.0)

### D3 update: includes A4 (deferred E2E test)

The deferred A4 E2E test from Phase A is incorporated into D3. Framework decision (Playwright vs Reqnroll) made at the start of D3.

---

## Lessons Learned (from Phase A)

These are non-negotiable, codified into ARCHITECTURE.md:

1. **Breaking changes must update ALL consumers in the same prompt.**
   Example: prompt 39 changed pagination shape but didn't update tests, leading to regressions caught later.

2. **Acceptance criteria "no regressions" must be verifiable.**
   Just saying "no regressions" doesn't catch them. CI/CD must run ALL tests, not just the affected module.

3. **Coverage % is necessary but not sufficient.**
   Tests must actually assert meaningful behavior. 90% coverage with weak assertions is worse than 70% with strong ones.

4. **Performance baselines must be numerical, not adjectival.**
   "Fast" is not a metric. "P95 < 200ms" is.

5. **Multi-tenant isolation is test rule #1.**
   Every cross-cutting endpoint test must verify tenant boundaries.

6. **Visual changes are local, not systemic.**
   Don't refactor a system to fix a single component bug.

7. **Numerical values > adjectives in visual specs.**
   "15% more luminance" > "subtle elevation"

8. **Hard constraints in prompts prevent scope creep.**
   "DO NOT touch X, Y, Z" must be explicit.

9. **Claude Code autonomy boundary: code yes, git no.**
   Codified in every prompt footer.

10. **Pure functions are dramatically easier to test than embedded component logic.**
    Extract logic to pure functions for testability.

---

## Tracking & Accountability

### Status tracking

Each item in this plan has a status:
- 📋 Planned (not started)
- 🔄 In progress (Claude Code working or testing)
- ✅ Complete (verified by user)
- ⏭️ Deferred (with reason and target phase)
- ⚠️ Blocked (reason documented)

### How we work going forward

1. **Every prompt must reference this plan.** Header includes: "Per Master Plan, this is item X.Y"
2. **No new features outside this plan.** Phase 2 (Transactions) does NOT start until Phase D is signed off.
3. **Audit findings must be addressed before closing Phase 1.** No exceptions for "we'll fix it later".
4. **Test coverage thresholds are non-negotiable.** Below 80% backend or 60% frontend = phase not closed.
5. **Visual prompts must be surgical**, not systemic. Numerical specs, file lists, DO NOT clauses.
6. **Every long prompt includes the autonomy footer.** Never grants git access.

---

## Estimated Timeline (Adjusted)

| Phase | Original | Adjusted | Status |
|---|---|---|---|
| Phase A — Close Import | 1 week | 1 day | ✅ DONE |
| Phase B — Architecture standards | 2 days | 2 days | ← NEXT |
| Phase C — Critical gaps | 3-4 weeks | 2-3 weeks (C1 done) | Pending |
| Phase D — Phase 1 closed | 1 week | 1 week | Pending |
| **TOTAL** | **6-7 weeks** | **4-5 weeks** | **17% complete** |

Phase A was faster than estimated due to focused prompts and pure-logic test scope.

---

## Updates log

| Date | Update |
|---|---|
| 2026-05-26 | Initial plan created. Phase A1 (UI polish) ready to execute via prompt 38. |
| 2026-05-26 | Phase A completed. A4 deferred to Phase 9. Lessons learned codified for ARCHITECTURE.md. Moving to B1. |

---

## Next immediate action

**B1 — Create ARCHITECTURE.md collaboratively**

This is NOT a Claude Code task. This is Rudolf + Claude (chat) working together section by section. Each section is reviewed and approved before moving to the next.

Recommended approach:
1. Start with section 1 (Clean Architecture rules)
2. Each section: Claude proposes, Rudolf reviews, refine, approve, move on
3. Each section gets concrete examples from the existing Wasnie codebase
4. Forbidden patterns get tied to real bugs we've seen (so they're not abstract)
5. End-to-end completion: ~4-6 hours of focused work, possibly across 2-3 sessions

When complete, ARCHITECTURE.md becomes the law of the project.

---

## Sign-off

✅ Phase A complete: 2026-05-26 — Rudolf (Rodolfo A. Calvo Jaubert)
__ Phase B complete: __________ (date) ____________ (initials)
__ Phase C complete: __________ (date) ____________ (initials)
__ Phase D complete: __________ (date) ____________ (initials)

**Phase 2 (Transactions) cannot begin until all four are signed off.**
