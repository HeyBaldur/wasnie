# Wasnie — Master Plan: Phase 1 Closure + Quality Foundations

**Status:** ACTIVE
**Owner:** Rodolfo A. Calvo Jaubert
**Created:** 2026-05-26
**Purpose:** Close Phase 1 properly with full test coverage, then establish architectural and quality foundations before continuing to Phase 2+

This document is the single source of truth for current development priorities. All prompts must reference and respect this plan.

---

## Context — Where we are and why this exists

Phase 1 was scoped as: foundation, multi-tenant, plans, rules, payees, quotas, assignments, basic UI, import.

What was delivered (functionally): all of the above, with end-to-end working flows.

What was NOT delivered (quality / non-functional): 
- Comprehensive test coverage across modules
- Server-side pagination (currently client-side, performance disaster waiting)
- Claims/Authorization system (no subscription tier enforcement)
- Audit trail for destructive operations
- Security hardening (rate limiting, input validation review, secure headers)
- Performance baselines verified
- Quality gates in CI/CD
- Observability (structured logging, metrics)
- Architecture-level documentation enforced

**Decision:** before continuing to Phase 2 (Transactions and Calculation Engine), we close Phase 1 with **production-grade quality**. Wasnie is financial software; cutting corners now means lawsuits later.

---

## Master Plan Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                                                                  │
│  PHASE A — Close Import feature properly         (~5 days)       │
│  ├── A1. UI polish + reusable components                         │
│  ├── A2. Backend tests for Import                                │
│  ├── A3. Frontend tests for Import                               │
│  └── A4. End-to-end test of Import flow                          │
│                                                                  │
│  PHASE B — Architecture & Quality Standards      (~2 days)       │
│  ├── B1. Create ARCHITECTURE.md master document                  │
│  ├── B2. Codebase audit against the document                     │
│  └── B3. Backlog of gaps prioritized                             │
│                                                                  │
│  PHASE C — Critical quality gaps                (~3-4 weeks)     │
│  ├── C1. Server-side pagination across all lists                 │
│  ├── C2. Claims & Authorization (subscription tiers)             │
│  ├── C3. Audit trail standardized                                │
│  ├── C4. Security hardening                                      │
│  ├── C5. Quality gates in CI/CD                                  │
│  └── C6. Observability foundation                                │
│                                                                  │
│  PHASE D — Close Phase 1 officially              (~1 week)       │
│  ├── D1. Backend test coverage > 80% on Phase 1 modules          │
│  ├── D2. Frontend test coverage > 60% on critical components     │
│  ├── D3. End-to-end happy path tests                             │
│  └── D4. Documentation + Master Spec v2.1                        │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘

Total estimated time: ~5-6 weeks of focused work to truly close Phase 1
```

After Phase D, Phase 2 (Transactions) can begin with confidence.

---

## PHASE A — Close Import feature properly

**Goal:** Import is genuinely production-ready: complete UI, full test coverage, edge cases handled.

### A1. UI Polish + Reusable Components

**Status:** Prompt 38 already generated, ready to execute
**Estimated time:** 90-120 min Claude Code + 30 min verification
**Deliverables:**
- `WsPageLayout` component
- `WsWizard` + `WsWizardStep` components
- `WsDataTable` component (no horizontal scroll, ellipsis + tooltip, sticky header)
- `WsStatCard` component (variants for success/warning/danger)
- Import Wizard refactored to use all four
- Plan Detail, Payees List, Payee Detail refactored to use WsPageLayout
- DESIGN_SYSTEM.md updated with new mandatory patterns

**Acceptance:** Visual consistency across all refactored pages. No icons missing. No layout issues. No horizontal scroll in tables.

### A2. Backend Tests for Import

**Status:** Prompt to be generated
**Estimated time:** 60-90 min Claude Code
**Deliverables:**

#### FileParserService tests
- Parse CSV with various delimiters and encodings
- Parse XLSX with multiple sheets (uses first only)
- Handle empty file
- Handle file with only headers
- Handle file with >300 rows (reject)
- Handle file with malformed rows
- Handle file with BOM (byte-order mark)
- Handle UTF-8 / UTF-16 / Latin-1 encodings
- Handle file size > 5 MB (reject)
- Reject unsupported file extension

#### PayeeImportValidationService tests
- Required fields missing
- Duplicate Employee Code within file
- Duplicate Email within file
- Duplicate Email/Code against DB
- Invalid email format
- Invalid date format (various)
- Future hire date (error)
- Hire date before 1950 (error)
- Cross-row manager reference resolves correctly
- Manager code doesn't exist anywhere (error)
- Names with special characters (accents, ñ, ü) preserved
- Whitespace trimmed and collapsed

#### PayeeImportExecutionService tests
- Successful import of valid rows
- Skipped rows recorded correctly
- Cross-row manager linkage (phase 1: create all; phase 2: link managers)
- Transaction rollback if any row fails during execution
- ImportAudit record created with correct metadata
- Multi-tenant isolation (cannot leak across tenants)

#### Endpoint integration tests (with Testcontainers)
- `POST /api/imports/payees/parse` happy path
- `POST /api/imports/payees/parse` with invalid file
- `POST /api/imports/payees/validate` returns row-level issues
- `POST /api/imports/payees/execute` succeeds
- `POST /api/imports/payees/execute` with `skipRowsWithWarnings: true`
- Cross-tenant: tenant A cannot use file uploaded by tenant B (file ID isolation)
- Performance smoke test: 300-row file processed in < 5 seconds

**Acceptance:** All tests pass. Coverage of Import services > 85%.

### A3. Frontend Tests for Import

**Status:** Prompt to be generated after A1
**Estimated time:** 60-90 min Claude Code
**Deliverables:**

#### Component tests (Karma/Jasmine or Vitest)
- `PayeeImportWizardComponent` step progression
- `UploadStepComponent` file selection, validation, error display
- `MappingStepComponent` auto-detection of columns
- `MappingStepComponent` FullName composition builder (chips add/remove)
- `PreviewStepComponent` summary cards correct values
- `PreviewStepComponent` table renders rows with status badges
- `PreviewStepComponent` filter tabs (All / Errors only / Warnings only)
- `CompleteStepComponent` shows correct count

#### Service tests
- `PayeeImportService` correctly composes payload
- `PayeeImportService` handles 4xx errors gracefully
- `PayeeImportService` handles 5xx errors gracefully

#### End-to-end test (Playwright or Cypress)
- Upload sample file → mapping with auto-detect → preview → execute
- Verify created payees appear in `/payees`

**Acceptance:** All tests pass. Coverage > 70% on Import feature.

### A4. End-to-end Test with Real Sample

**Status:** After A2 and A3
**Estimated time:** 30 min setup + verification
**Deliverables:**
- Use the `NorthBridge_Sales_Team_Export.xlsx` sample file (85 rows)
- Run the full flow as automated test
- Assert all 85 rows imported correctly
- Assert cross-row manager references resolved
- Assert accented names preserved

**Acceptance:** The sample file imports cleanly end-to-end in CI.

---

## PHASE B — Architecture & Quality Standards

**Goal:** Establish a binding contract for what "production quality" means in Wasnie. All future development must comply.

### B1. Create ARCHITECTURE.md

**Status:** To be generated after Phase A
**Estimated time:** 4-6 hours of focused work (me + you)
**Deliverable:** A comprehensive markdown file in the repo root.

#### Required sections

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
- E2E: happy path of every critical user journey
- Tests must use real database (Testcontainers), not mocks for repository tests
- Tests fail the build; warnings fail the build

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
- Alerts on:
  - P95 response time > baseline
  - Error rate > 1%
  - Failed login attempts spike
  - Database connection pool exhaustion

**10. Forbidden patterns (recap)**
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

**11. Forbidden architectural drift**
- Adding new "convenience" features without tests
- Skipping security review on auth-related changes
- Bypassing audit trail for "small" operations
- Hardcoding tier limits instead of using authorization layer
- Building features without acceptance criteria

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

### C1. Server-side Pagination

**Status:** Prompt 39 already generated
**Estimated time:** 90-120 min Claude Code
**Why critical:** Current client-side pagination breaks at any meaningful scale.

### C2. Claims & Authorization

**Status:** Prompt to be generated
**Estimated time:** 4-6 hours Claude Code + verification
**Deliverables:**

#### Backend
- `User` entity with role assignments
- `Role` entity with permissions
- `SubscriptionTier` entity with limits
- `Tenant` extended with `CurrentTier` reference
- `IAuthorizationService` interface
- Implementation that checks:
  - User has role with permission for endpoint
  - User's tenant tier allows the requested action
  - Resource counts within tier limits (e.g., adding 26th payee on Starter fails)
- Decorate every endpoint with `[RequiresPermission("Payees.Create")]` or similar
- Middleware that enforces it
- 403 Forbidden with clear error: "Your plan (Starter) is limited to 25 payees. Upgrade to add more."

#### Frontend
- AuthService exposes current user's permissions
- UI hides actions user can't perform (graceful degradation)
- Upsell prompts when user attempts a tier-restricted action
- `/account/subscription` page showing current tier + limits

#### Tier limits (initial)
- Free: read-only demo, 5 payees, no plans
- Starter: 25 payees, basic plans, no integrations
- Growth: 75 payees, advanced plans, CSV/Excel import
- Scale: 150 payees, all features, API access, integrations
- Enterprise: unlimited + custom SLA

#### Tests
- Each permission tested
- Each tier limit enforced
- Upgrade flow tested
- Migration path: existing tenants default to Growth tier (free during beta)

### C3. Audit Trail Standardized

**Status:** Prompt to be generated
**Estimated time:** 3-4 hours Claude Code
**Deliverables:**

- `AuditLog` entity (append-only, never edited or deleted)
- `IAuditService` interface
- Implementation captures:
  - User (id, email)
  - Tenant
  - Action (Created, Updated, Deleted, Activated, Terminated, etc.)
  - ResourceType (Payee, Plan, Quota, etc.)
  - ResourceId
  - Before/After (JSON snapshot)
  - Timestamp
  - CorrelationId
  - UserAgent / IPAddress
- Decorator pattern: `[Auditable]` on DTOs / commands
- Background worker writes audit asynchronously (don't block API response)
- View page `/audit` with filter by user, tenant, action, date range
- Export audit log as CSV (Enterprise tier only)

### C4. Security Hardening

**Status:** Prompt to be generated
**Estimated time:** 4-6 hours Claude Code
**Deliverables:**

- Rate limiting middleware (5/min on auth, 100/min general)
- Input validation review across all endpoints
- Secure headers middleware (CSP, X-Frame-Options, etc.)
- HTTPS enforcement in production
- Secrets review: move any hardcoded values to environment variables
- Implement refresh token rotation (one-time use)
- Lockout after 5 failed login attempts (15 min cooldown)
- Password reset flow with secure tokens
- Email verification for new accounts
- 2FA optional (TOTP)
- Penetration testing checklist (manual review for now)

### C5. CI/CD Quality Gates

**Status:** Prompt to be generated
**Estimated time:** 3-4 hours
**Deliverables:**

- GitHub Actions / Azure DevOps pipeline that runs:
  - Backend tests (must pass)
  - Frontend tests (must pass)
  - Backend coverage check (> 80%)
  - Frontend coverage check (> 60%)
  - Lint check (no errors)
  - Security scan (npm audit, dotnet vulnerabilities)
  - Build warning check (none allowed)
  - Bundle size check (frontend < 500KB initial)
- Pre-commit hooks: format, lint
- Pull request template with checklist
- Branch protection rules: PR required, checks must pass, code review required

### C6. Observability Foundation

**Status:** Prompt to be generated
**Estimated time:** 3-4 hours
**Deliverables:**

- Serilog configured with structured JSON logging
- Correlation ID middleware
- Application Insights (Azure) or alternative for metrics
- OpenTelemetry instrumentation for distributed tracing
- Error tracking with Sentry (free tier sufficient initially)
- Health check endpoint with detailed dependency status
- Basic alerting (email on critical errors)

---

## PHASE D — Phase 1 Officially Closed

### D1. Backend Test Coverage > 80%

Run coverage report, identify gaps, fill them. Modules that must be tested:
- Tenants
- Payees (all CRUD + status transitions)
- Plans + Versioning
- Plan Rules
- Plan Assignments
- Quotas
- Auth + Refresh tokens
- Imports
- Audit
- Authorization

### D2. Frontend Test Coverage > 60%

Critical components:
- Login flow
- Forms (Payee, Quota, Assignment, Plan)
- All modal patterns
- All wizard patterns
- Data tables (with sort, filter, pagination)
- Pagination component
- Authorization-aware UI

### D3. End-to-End Tests for Happy Path

Playwright or Cypress tests for:
- New tenant onboarding
- Create first plan
- Add first payees
- Assign plan to payees
- Set quotas
- Verify visibility per role

### D4. Documentation Update

- Master Spec v2.1 with all learnings
- README updated
- Architecture diagrams
- API documentation (OpenAPI/Swagger reviewed)
- Deployment guide
- Local development guide
- Contribution guide

---

## Tracking & Accountability

### Status tracking

Each item in this plan has a status:
- 📋 Planned (not started)
- 🔄 In progress (Claude Code working or testing)
- ✅ Complete (verified by user)
- ⚠️ Blocked (reason documented)

### How we work going forward

1. **Every prompt must reference this plan.** Header includes: "Per Master Plan, this is item X.Y"
2. **No new features outside this plan.** Phase 2 (Transactions) does NOT start until Phase D is signed off.
3. **Audit findings must be addressed before closing Phase 1.** No exceptions for "we'll fix it later".
4. **Test coverage thresholds are non-negotiable.** Below 80% backend or 60% frontend = phase not closed.

### How I (Claude in chat) work going forward

- Every prompt I generate references the plan item it addresses
- Every prompt includes acceptance criteria that align with ARCHITECTURE.md
- If you ask for something outside the plan, I will check with you whether to update the plan or defer
- I will not generate prompts that skip security, performance, or testing requirements

### How Claude Code works going forward

When given a prompt:
- Must read ARCHITECTURE.md before starting (referenced in every prompt header)
- Must respect acceptance criteria
- Must write tests as part of feature work, not separately
- Must run lint and tests before reporting "done"
- Must update DESIGN_SYSTEM.md if a new pattern is introduced

---

## Estimated Timeline

| Phase | Duration | Working hours |
|---|---|---|
| Phase A — Close Import | 1 week | 20-30 hours |
| Phase B — Architecture standards | 2 days | 8-12 hours |
| Phase C — Critical gaps | 3-4 weeks | 60-90 hours |
| Phase D — Phase 1 closed | 1 week | 20-30 hours |
| **TOTAL** | **6-7 weeks** | **~150 hours** |

This is realistic. Cutting corners makes lawsuits, lost customers, and rebuilds. Investing now compounds positively.

---

## Updates log

| Date | Update |
|---|---|
| 2026-05-26 | Initial plan created. Phase A1 (UI polish) ready to execute via prompt 38. |

---

## Next immediate action

After prompt 38 (UI polish) completes:
1. Verify UI quality with screenshots
2. Move to A2 — generate Backend Import Tests prompt
3. Execute A2
4. Move to A3 — generate Frontend Import Tests prompt
5. Execute A3
6. Move to A4 — generate E2E test prompt
7. Phase A complete, sign off, move to Phase B

---

## Sign-off

Phase A complete: __________ (date) ____________ (your initials)
Phase B complete: __________ (date) ____________ (your initials)
Phase C complete: __________ (date) ____________ (your initials)
Phase D complete: __________ (date) ____________ (your initials)

**Phase 2 (Transactions) cannot begin until all four are signed off.**
