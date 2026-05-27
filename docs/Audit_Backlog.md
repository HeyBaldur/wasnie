# Wasnie — Audit Backlog (Phase C Execution Plan)

**Generated:** 2026-05-27
**Owner:** Rodolfo Calvo
**Source:** `docs/audit/Audit_Findings.md` (27 findings from B2 audit)
**Purpose:** Convert audit findings into an ordered, dependency-aware execution plan for Phase C.

---

## How to read this document

Each finding from the audit is grouped into a **work item** (WI) — a unit of work that can be executed as a single Claude Code prompt. Findings are grouped when they share infrastructure, files, or testing context (e.g., F-005 and F-006 are both JWT lifetime fixes, so they share one work item).

Work items are sequenced into **waves**. A wave is a set of work items that can be done in parallel or in any order (no dependencies among them). The next wave cannot start until the previous wave is complete.

Each work item has:
- **WI-XX identifier** for tracking
- **Findings included** from the audit
- **Effort estimate** (hours of focused work)
- **Dependencies** on prior work items
- **Phase C subphase** mapping
- **Prompt notes** highlighting risks or special considerations

---

## Deferred Decisions

Decisions that have been explicitly postponed to a later phase, with rationale. This section is updated whenever a work item is partially deferred.

### Email Provider Integration (deferred from WI-02)

**Decision date:** 2026-05-27
**Decision:** The actual email provider (Postmark, SendGrid, AWS SES, or other) will NOT be integrated during Phase C. Email verification infrastructure will be built with a `ConsoleEmailService` placeholder implementation that logs email content rather than sending. The real provider integration is deferred to Phase 5-6 (when the first paying customer requires it).

**Rationale:**
- Project is still in solo development phase; no real users registering daily
- Mass testing of email flow not needed yet
- Setup time for email provider has no immediate value
- Architectural pattern (`IEmailService` abstraction) means real provider integration later is ~1-2 hours of work

**Implementation pattern:**
- `IEmailService` interface defined in Application layer
- `ConsoleEmailService` implementation in Infrastructure (logs email to structured logger)
- Real implementation (`PostmarkEmailService`, etc.) added later by registering different implementation in DI
- Configuration flag `RequireConfirmedEmail` controls whether verification is enforced
- Development environment: `RequireConfirmedEmail = false` (auto-confirm accounts)
- Production environment (when reached): `RequireConfirmedEmail = true` + real provider registered

**When to revisit:**
- When the first design partner or paying customer is identified
- Before any production traffic that includes real user signups
- When deliverability becomes a competitive concern (Phase 5+)

---

## Executive summary

**Total work items:** 13
**Total findings addressed:** 27 of 27
**Estimated total effort:** ~50-70 hours of focused work
**Estimated wall-clock duration:** 3-4 weeks at current pace

**Wave structure:**

| Wave | Work items | Theme | Effort | Why this order |
|---|---|---|---|---|
| 1 | WI-01, WI-02, WI-03 | Security quick wins | 4-6 hours | Highest risk, lowest effort — exploitable issues with config-level fixes |
| 2 | WI-04, WI-05 | Multi-tenant defense | 4-6 hours | Fixes real cross-tenant leak vectors before they become incidents |
| 3 | WI-06 | Clean Architecture foundation | 6-8 hours | Removes architectural violations that block IClock/IAuthService |
| 4 | WI-07 | Time and ID abstractions | 4-6 hours | Required infrastructure for testing and DI compliance |
| 5 | WI-08 | Audit trail foundation | 8-12 hours | Required for Phase 2 compliance; enables F-009 |
| 6 | WI-09 | Authorization + tier limits | 12-16 hours | Largest work item; requires audit trail (WI-08) |
| 7 | WI-10 | Validation gaps + test coverage | 6-8 hours | Cleanup work — can run during WI-09 if capacity allows |
| 8 | WI-11 | Security hardening (middleware) | 4-6 hours | Headers, rate limiting, password policy |
| 9 | WI-12 | Observability foundation | 6-8 hours | Setup for production readiness |
| 10 | WI-13 | Cleanup and Low findings | 2-4 hours | Final pass to clear remaining items |

---

## WAVE 1 — Security Quick Wins (4-6 hours)

These are exploitable issues with config-level fixes. Highest priority because they ship the most security improvement per unit of effort.

### WI-01 — Tighten JWT lifetimes

**Findings addressed:** F-005, F-006
**Effort:** 1 hour
**Dependencies:** None
**Phase C subphase:** C4 (Security Hardening)

**Scope:**
- Change `JwtSettings.ExpiryMinutes` from 60 to 15 across appsettings.json, appsettings.Development.json, appsettings.Production.json
- Change `RefreshTokenLifetimeDays` constant in TokenService.cs from 30 to 7
- Update integration tests that may rely on token lifetime
- Verify refresh token rotation continues to work correctly with shorter lifetime

**Prompt notes:**
- Trivial config change; risk is in tests assuming 60-min token validity
- After this fix, frontend may need to handle 401 responses more aggressively (refresh more often)

---

### WI-02 — Email verification (infrastructure only, provider deferred)

**Findings addressed:** F-008
**Effort:** 2-3 hours
**Dependencies:** None
**Phase C subphase:** C4 (Security Hardening)

**Status:** Scope updated 2026-05-27. See "Deferred Decisions" section above for full context.

**Scope (revised):**
- Define `IEmailService` interface in `Wasnie.Application/Common/Abstractions/IEmailService.cs` with methods like `SendVerificationEmailAsync`, `SendPasswordResetEmailAsync` (defined for future use)
- Implement `ConsoleEmailService` in `Wasnie.Infrastructure/Services/Email/ConsoleEmailService.cs` that logs the email body to the structured logger rather than sending
- Register `ConsoleEmailService` as `IEmailService` in DI
- Create email verification flow infrastructure:
  - Email verification token entity (or extension of existing identity user table)
  - Token generation logic in identity service
  - `POST /api/auth/verify-email` endpoint that consumes the token
  - `POST /api/auth/resend-verification` endpoint
- Add config flag `JwtSettings.RequireConfirmedEmail` (or similar location in appsettings):
  - `appsettings.Development.json`: `false` (auto-confirm)
  - `appsettings.Production.json`: `true` (real flow)
  - `appsettings.json` (base): `false` (safe default for development)
- In `IdentityService.cs`: do NOT hardcode `EmailConfirmed = true`. Use the config flag to decide.
- In `DependencyInjection.cs`: configure `Identity.SignIn.RequireConfirmedEmail` from config flag
- Frontend (Angular):
  - Create `verify-email` route + component (consumes token from URL query param)
  - Create `resend-verification` route + component
  - On login, handle "email not verified" error response with redirect to resend page
- Integration tests:
  - Verify that when `RequireConfirmedEmail = false`, accounts are auto-confirmed at creation
  - Verify that when `RequireConfirmedEmail = true`, accounts cannot log in until verified
  - Verify token generation, verification endpoint, expiration of verification tokens
  - Verify `ConsoleEmailService` logs the expected email content

**What is NOT in scope (deferred):**
- Real email provider integration (Postmark, SendGrid, AWS SES)
- Production-grade email templates with branding
- Email tracking, bounce handling, suppression lists
- HTML email templating beyond a basic plain-text template

**Prompt notes:**
- The entire verification flow MUST be production-ready except for the actual email-sending. When we add a real provider in Phase 5-6, the only change is registering a different `IEmailService` implementation in DI.
- The `ConsoleEmailService` should output the verification link in a way that is easy to copy from logs during development testing (e.g., format the link prominently)
- DO NOT couple verification token storage to any specific Identity package internal — use the project's domain model

---

### WI-03 — Implement logout token invalidation

**Findings addressed:** F-012, F-015 (refresh token validator)
**Effort:** 1-2 hours
**Dependencies:** None
**Phase C subphase:** C4 (Security Hardening)

**Scope:**
- Logout endpoint MUST mark the user's refresh token(s) as revoked in the database
- Add `RefreshTokenCommandValidator` (FluentValidation) requiring non-empty token
- Refresh token table needs `RevokedAt` field if not present
- Integration tests: post-logout refresh token attempt returns 401

**Prompt notes:**
- Per Rule 4.1.3, refresh tokens must be revocable. Database is the source of truth.
- If using in-memory or stateless refresh tokens currently, this is a schema change

---

## WAVE 2 — Multi-Tenant Defense (4-6 hours)

Fixes real cross-tenant leak vectors. WI-04 fixes the only exploitable cross-tenant issue (F-007). WI-05 hardens defense in depth.

### WI-04 — Tenant-scoped import cache + temporary storage

**Findings addressed:** F-007
**Effort:** 1-2 hours
**Dependencies:** None
**Phase C subphase:** C4 (Security Hardening) + multi-tenant

**Scope:**
- Change `ImportCacheService.CacheKey` from `import:payees:{fileId}` to `import:payees:{tenantId}:{fileId}`
- Verify ALL operations on import cache (set, get, delete) use the tenant-scoped key
- Update test fixtures that interact with import cache
- Add cross-tenant integration test for import (Tenant A uploads, Tenant B attempts retrieval — must fail)

**Prompt notes:**
- The actual exploitability is low (fileId is a Guid, hard to guess). But the rule is explicit and the fix is trivial. Don't defer.
- Same pattern applies to any other cache or temporary storage in the codebase — audit cache keys broadly during this work item

---

### WI-05 — Multi-tenant defense hardening

**Findings addressed:** F-018, F-019, F-022
**Effort:** 3-4 hours
**Dependencies:** None
**Phase C subphase:** C4 + multi-tenant

**Scope:**
- F-018: Add explicit `Where(x => x.TenantId == currentTenantId)` clause to ListPayeesHandler's IgnoreQueryFilters block
- F-019: Add HasQueryFilter for ImportAudit entity in DbContext OnModelCreating
- F-022: TenantContext.TenantId must throw UnauthorizedException (or return null with caller-side enforcement) when claim is missing, NOT return Guid.Empty silently
- Add a test that verifies unauthenticated/malformed-token requests return 401, not 200 with empty data
- Audit ALL `IgnoreQueryFilters()` usages in the codebase — each one needs explicit tenant filter

**Prompt notes:**
- F-022 is the highest-impact of these three: silent Guid.Empty is the kind of bug that produces "why are my queries returning empty?" mysteries weeks later
- Throwing is preferable to returning null (fail fast, explicit signal)

---

## WAVE 3 — Clean Architecture Foundation (6-8 hours)

Architectural violations that must be fixed before introducing IClock, IAuthorizationService, and other abstractions.

### WI-06 — Remove forbidden dependencies from Domain and Application

**Findings addressed:** F-001, F-002
**Effort:** 6-8 hours
**Dependencies:** None
**Phase C subphase:** C7 (new — Clean Architecture fixes)

**Scope:**
- F-001: Remove MediatR PackageReference from Wasnie.Domain.csproj
- F-001: Refactor `IDomainEvent` to be a pure marker interface (no MediatR.INotification inheritance) — domain events become POCOs
- F-001: Update Application layer to handle domain event dispatching (DomainEventDispatcher service that wraps domain events as MediatR notifications when needed)
- F-002: Remove `Microsoft.EntityFrameworkCore` PackageReference from Wasnie.Application.csproj
- F-002: Refactor `IApplicationDbContext` to not expose `DbSet<T>` — use repository interfaces or query services owned by Application
- F-002: Move EF Core-specific imports to Infrastructure layer
- Run full test suite after refactor — coverage must remain at current level
- Verify build succeeds with new project references

**Prompt notes:**
- This is the largest refactor of the backlog. Affects multiple files across Domain, Application, Infrastructure
- Risk: introducing this poorly creates a layer of abstraction (`IDbSet<T>` wrappers) that adds complexity without benefit
- Alternative simpler approach: leave EF Core in Application but agree to amend ARCHITECTURE.md section 01 Rule 1.2 to allow it (with reasoning documented). Decide before starting.
- DECISION POINT for Rodolfo: strict purity (long refactor) vs pragmatic amendment (short fix, document compromise)

---

## WAVE 4 — Time and ID Abstractions (4-6 hours)

Required infrastructure for testing and DI compliance. Must come before audit trail and authorization (which use IClock).

### WI-07 — Introduce IClock and IGuidGenerator

**Findings addressed:** F-003, F-004
**Effort:** 4-6 hours
**Dependencies:** WI-06 (Application must not reference EF Core to allow clean DI)
**Phase C subphase:** C7

**Scope:**
- Define `IClock` interface in `Wasnie.Application/Common/Abstractions/IClock.cs` (or wherever Application abstractions live)
- Implement `SystemClock : IClock` in `Wasnie.Infrastructure/Common/SystemClock.cs`
- Define `IGuidGenerator` interface similarly with `SystemGuidGenerator` implementation
- Register both in DependencyInjection.cs
- Refactor all Domain entities to receive IClock and IGuidGenerator via factory methods (NOT injected directly into entities — use them in Application use cases that pass timestamps/Ids to entity factories)
- Files affected (per audit): Payee.cs, PlanAssignment.cs, CompensationPayout.cs, Quota.cs, Plan.cs, and the legacy Entities/Plan.cs (legacy may be deleted in WI-13 instead)
- Update all unit tests to inject fake clocks/guid generators using NSubstitute
- Verify no `DateTime.UtcNow` or `Guid.NewGuid()` remains in Domain or Application code (CI rule to add later in C5)

**Prompt notes:**
- Pure Domain doesn't get IClock injected directly — factories in Application pass the timestamp in
- Example: `Payee.Create(IClock clock, ...)` is wrong; `Payee.Create(DateTime now, ...)` called by use case with `clock.UtcNow` is right
- Tests get massively easier after this — current tests that depend on time become deterministic

---

## WAVE 5 — Audit Trail Foundation (8-12 hours)

Establishes the immutable audit log required for financial compliance. Prerequisite for authorization (WI-09) which writes audit records on permission denials.

### WI-08 — Implement IAuditService and generic audit trail

**Findings addressed:** F-014
**Effort:** 8-12 hours
**Dependencies:** WI-07 (IClock for timestamps)
**Phase C subphase:** C3 (Audit Trail)

**Scope:**
- Create `IAuditService` interface in Application with `LogAsync(action, resourceType, resourceId, before, after, metadata)` method
- Create `AuditLog` entity in Domain with fields per ARCHITECTURE.md section 05 Rule 5.2.1
- Implement `AuditService` in Infrastructure backed by database
- Database migration: AuditLogs table with indexes per section 05 Rule 5.7.2
- Configure database trigger or row-level permissions to prevent UPDATE/DELETE on AuditLogs table (per Rule 5.3.1, 5.3.2)
- Implement MediatR pipeline behavior `AuditBehavior<TRequest, TResponse>` that wraps commands implementing `IAuditableCommand`
- Mark all existing commands that perform destructive or monetary operations as `IAuditableCommand`
- Add per-resource audit history endpoint pattern: `GET /api/{resource}/{id}/audit`
- Implement async background queue for audit writes (do not block user operations per Rule 5.3.3)
- Integration tests: verify audit records created for create/update/delete operations on Payees, Plans, Quotas, Assignments

**Prompt notes:**
- Audit log MUST NOT contain secrets (per Rule 5.3.4) — when serializing entities, exclude sensitive fields
- For money operations (Phase 2+), use transactional outbox pattern — audit record written in same DB transaction as business change
- Phase 1 audit operations are mostly non-monetary, so async queue is acceptable now; revisit in Phase 2

---

## WAVE 6 — Authorization + Tier Limits (12-16 hours)

The largest work item. Authorization is essential for any sellable product. Required for tier-based pricing and role-based access.

### WI-09 — Implement IAuthorizationService, RBAC, and tier limit enforcement

**Findings addressed:** F-009
**Effort:** 12-16 hours
**Dependencies:** WI-07 (IClock for token expiry checks), WI-08 (audit trail for permission denial logs)
**Phase C subphase:** C2 (Claims & Authorization)

**Scope:**
- Define `IAuthorizationService` interface in Application
- Implement `AuthorizationService` in Infrastructure with permission resolution
- Define role-to-permission matrix per ARCHITECTURE.md section 06 (TenantAdmin: all; CompManager: Payees.*, Plans.*, etc.; Manager: read-only on team; Rep: self only)
- Define `TierFeatures` table or constant class with hardcoded limits per tier (Free: 5 payees; Starter: 25; Growth: 75; Scale: 150; Enterprise: unlimited)
- Implement `ITierLimitChecker` service for tier-based limits
- Refactor all destructive/creation use case handlers to call `IAuthorizationService.Require(permission)` BEFORE business logic
- Refactor entity creation handlers to call `ITierLimitChecker.EnsureLimitNotExceeded(tenantId, ResourceType)` BEFORE creation
- Tier limit exceeded returns specific error code 403 with `error: "TierLimitExceeded"` (per Rule 6.1.1 format)
- Implement scoped permissions: Manager role can only read their direct/indirect reports
- Frontend: hide UI elements user lacks permission for; show upgrade prompts when tier limit hit
- Frontend: fetch user permissions from `/auth/me` and cache in service
- Integration tests: every endpoint has authorization test (per role, per tier)
- Audit log entries for denied permissions (per Rule 5.1.4)

**Prompt notes:**
- This is the largest work item. May benefit from splitting into WI-09a (backend authorization) and WI-09b (tier limits + frontend)
- Manager role scoped permissions (only direct/indirect reports) requires org hierarchy traversal — verify org tree exists in domain model
- Critical for monetization: without tier limits, the pricing model is unenforceable
- Add cross-role integration tests: TenantAdmin can do X, Rep cannot do X, etc.

---

## WAVE 7 — Validation Gaps + Test Coverage (6-8 hours)

Cleanup work that can run in parallel with WI-09 if capacity allows.

### WI-10 — Add missing validators and missing test files

**Findings addressed:** F-016, F-017, F-023
**Effort:** 6-8 hours
**Dependencies:** WI-04 (cross-tenant tests need tenant-scoped import cache)
**Phase C subphase:** C2 (validators) + C5 (tests)

**Scope:**
- F-016: Create FluentValidation validators for UpdatePayeeCommand, UpdateQuotaCommand, UpdateAssignmentNotesCommand
- F-017: Create integration test files for Quotas endpoints (QuotasEndpointsTests.cs)
- F-017: Create integration test files for Assignments endpoints (AssignmentsEndpointsTests.cs)
- F-017: Verify Import tests include cross-tenant scenarios
- F-023: Add integration tests for PlanRulesController endpoints
- Each new test file: happy path, validation failure, authentication required, authorization required (after WI-09), cross-tenant isolation
- Verify coverage thresholds: backend >80%, calculation logic >95% — measure with `dotnet test /p:CollectCoverage=true`

**Prompt notes:**
- This work item is "boring but important" — easy to deprioritize, but accumulates as technical debt
- Tests written here become safety net for all subsequent refactoring

---

## WAVE 8 — Security Hardening (4-6 hours)

Middleware-level security improvements. These are configuration changes, not architectural.

### WI-11 — Security headers + rate limiting + password policy

**Findings addressed:** F-010, F-011, F-013
**Effort:** 4-6 hours
**Dependencies:** None
**Phase C subphase:** C4 (Security Hardening)

**Scope:**
- F-010: Add security headers middleware to Program.cs (CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Permissions-Policy, HSTS in production)
- F-010: Enable HTTPS redirect (UseHttpsRedirection) and HSTS (UseHsts) in production environment
- F-011: Add ASP.NET Core rate limiter to Program.cs with policies:
  - Login: 5 attempts / 15 min per IP, 5 / 15 min per email
  - Refresh: 60 / hour per refresh token
  - Forgot password: 3 / hour per email
  - Default policy: 100 requests / minute per authenticated user
- F-013: Update Identity options: `RequiredLength = 10`, `RequireNonAlphanumeric = true`
- F-013: Frontend password input — show password requirements clearly to user
- Integration tests: rate limiter behavior, headers present in responses

**Prompt notes:**
- ASP.NET Core has built-in rate limiter middleware (Microsoft.AspNetCore.RateLimiting) since .NET 7 — use it, don't reinvent
- CSP is the highest-impact header for XSS protection — define carefully to not break legitimate inline scripts/styles
- Password policy change may break existing users with weak passwords — they continue to work until next password reset, then must meet new policy

---

## WAVE 9 — Observability Foundation (6-8 hours)

Logging and metrics infrastructure for production readiness.

### WI-12 — Structured logging, correlation IDs, error tracking

**Findings addressed:** F-027
**Effort:** 6-8 hours
**Dependencies:** None
**Phase C subphase:** C6 (Observability)

**Scope:**
- Add Serilog sinks for production destination (Application Insights, Seq, or similar — Rodolfo decision based on hosting/budget)
- Configure structured JSON logging across the application (verify all log calls use placeholders, not string interpolation)
- Add correlation ID middleware (X-Correlation-ID header generation and propagation)
- Configure log enrichers: TenantId, UserId, CorrelationId on every log entry automatically
- Add Sentry (or equivalent) for frontend error tracking
- Configure frontend global error handler to send to Sentry
- Define what to redact (passwords, tokens, sensitive fields per Rule 4.9.4)
- Set log levels appropriately (Trace/Debug OFF in production)
- Frontend Real User Monitoring (RUM) — Phase C6 minimum, full implementation in Phase D

**Prompt notes:**
- For mid-market SaaS budget, recommend: Serilog → Seq or Better Stack ($10-30/month) for backend; Sentry free tier for frontend
- Application Insights works well if Azure-hosted but can be expensive at scale
- OpenTelemetry distributed tracing can be deferred to Phase 8 — too much investment for current scale

---

## WAVE 10 — Cleanup and Low Findings (2-4 hours)

Final pass to clear remaining items.

### WI-13 — Cleanup and minor fixes

**Findings addressed:** F-020, F-021, F-024, F-025, F-026
**Effort:** 2-4 hours
**Dependencies:** None
**Phase C subphase:** Various — opportunistic cleanup

**Scope:**
- F-020: Replace `bypassSecurityTrustHtml` in icon/empty-state components with DOMPurify, OR document explicitly that the SVG strings are compile-time constants (preferred — add inline comment)
- F-021: Replace `color: #fff;` in ws-wizard.component.scss with `color: var(--color-text-inverse);`
- F-024: Move dev JWT secret to `dotnet user-secrets`; remove from appsettings.Development.json; verify .template file shows the placeholder pattern
- F-025: Already addressed in WI-11 (password policy)
- F-026: Delete legacy `Wasnie.Domain.Entities.Plan` if confirmed unused; otherwise document why both exist (likely deletable since `Wasnie.Domain.Compensation.Plans.Plan` is the canonical version)

**Prompt notes:**
- F-026 (legacy entity) is a quick win — verify no references via `grep` before deleting
- F-020 (bypassSecurityTrustHtml) is "false positive in spirit" — the SVGs are not user-supplied. Documentation comment is sufficient. Don't introduce DOMPurify just for this.
- F-024 (dev secret): once moved to user-secrets, every new developer setup gets their own; document the setup step in CONTRIBUTING.md

---

## Dependency graph

```
                      WI-01 (JWT)──┐
                      WI-02 (Email)─┼─── Wave 1 (parallel)
                      WI-03 (Logout)┘
                                ↓
                      WI-04 (Cache)─┐
                      WI-05 (Tenant)┘── Wave 2 (parallel)
                                ↓
                      WI-06 (Clean Arch)── Wave 3
                                ↓
                      WI-07 (IClock)──── Wave 4
                                ↓
                      WI-08 (Audit)───── Wave 5
                                ↓
                      WI-09 (RBAC)────── Wave 6
                                ↓
                      WI-10 (Validators + Tests)── Wave 7 (can parallel WI-09)
                                ↓
                      WI-11 (Security HTTP)─── Wave 8
                                ↓
                      WI-12 (Observability)── Wave 9
                                ↓
                      WI-13 (Cleanup)──────── Wave 10
```

---

## Open decisions before starting Phase C

The following decisions need Rodolfo's confirmation before specific work items start:

1. **WI-06 (Clean Arch)** — Strict purity refactor (long) vs amend ARCHITECTURE.md to allow EF Core in Application (short)? Recommended: strict purity, but understand the cost is real
2. **WI-02 (Email verification)** — Which email provider? SendGrid free tier sufficient for now? Alternative providers Postmark, AWS SES, Mailgun
3. **WI-09 (RBAC + tiers)** — Split into WI-09a (backend only) and WI-09b (frontend integration)? Single large work item or two smaller ones?
4. **WI-12 (Observability)** — Which logging destination? Seq self-hosted vs Better Stack vs Application Insights vs Sentry-only-frontend
5. **WI-08 (Audit trail)** — Separate database for audit or schema in main DB? Recommended: schema in main DB for now, separate when scale demands it

---

## Estimated timeline

Assuming similar pace to Phase A (1 work item per 1-2 days of focused work):

```
Week 1: Wave 1 (WI-01, WI-02, WI-03) + Wave 2 (WI-04, WI-05) + Wave 3 start (WI-06)
Week 2: Wave 3 (WI-06 finish) + Wave 4 (WI-07) + Wave 5 start (WI-08)
Week 3: Wave 5 (WI-08 finish) + Wave 6 (WI-09) — biggest week
Week 4: Wave 6 (WI-09 finish) + Wave 7 (WI-10) + Wave 8 (WI-11) + Wave 9 (WI-12)
Week 5: Wave 10 (WI-13) + buffer for unanticipated issues
```

Total: ~5 weeks for Phase C if no significant blockers.

This estimate assumes:
- Solo development with Claude Code as accelerator (current model)
- No new feature work added to Phase C
- No major architectural amendments mid-phase
- Tests run cleanly throughout (no flaky test debugging)

---

## Phase C completion criteria

Phase C is complete when:

- All 13 work items are executed
- All 27 audit findings are addressed (verified by re-audit prompt)
- Coverage thresholds met: backend > 80%, calculation logic > 95%
- All Critical and High findings show ✅ in updated audit
- PROJECT_STATUS.md reflects "Phase C complete"
- SESSION_LOG.md has entries for each work item with date and outcome

After Phase C completes, Phase D begins: final test coverage push, documentation refresh, and Phase 1 official sign-off.

---

## Maintenance

This backlog is a living document. After each work item is completed:

1. Mark the work item as ✅ in this file
2. Update the date next to the work item indicating completion
3. Update PROJECT_STATUS.md
4. Append entry to SESSION_LOG.md
5. If new findings are discovered during the work, add them to this backlog as WI-NN with appropriate wave placement

If significant deviations from the plan emerge (e.g., a work item turns out to be 3x larger than estimated, or a dependency changes), pause and reassess the remaining waves before continuing.
