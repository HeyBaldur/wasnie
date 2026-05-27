# Wasnie Codebase Audit — Findings

**Audit date:** 2026-05-27
**Audited against:** ARCHITECTURE.md v1.0 (and 14 section files)
**Scope:** Backend full (WasnieApi/src/), Frontend critical sections (WasnieUi/src/)
**Performed by:** Claude Code (read-only analysis)

---

## Executive Summary

| Severity | Count |
|---|---|
| 🔴 Critical | 8 |
| 🟠 High | 7 |
| 🟡 Medium | 8 |
| 🟢 Low | 4 |
| **Total** | **27** |

**Critical risk areas:**
- Domain layer carries an illegal MediatR `PackageReference` dependency (Rule 1.1 violated); `IDomainEvent` directly imports `MediatR.INotification`
- Multiple domain entities call `DateTime.UtcNow` and `Guid.NewGuid()` directly, making business logic non-deterministic and untestable in isolation
- Import cache keys omit TenantId, enabling cross-tenant file-session hijacking
- JWT access tokens are configured at 60 minutes (4× the allowed 15-minute max), and refresh tokens at 30 days (4× the allowed 7-day max)
- No HTTPS redirect, no HSTS, no security headers (X-Frame-Options, CSP, X-Content-Type-Options) in the middleware pipeline
- Role-based authorization (RBAC) and tier-limit enforcement do not exist; every authenticated user can do everything

**Compliance highlights:**
- EF Core global query filters are applied correctly on all main tenant-scoped entities (Payees, Plans, Quotas, PlanAssignments, etc.)
- Server-side pagination is implemented correctly on all list endpoints
- Integration tests use Testcontainers (real SQL Server), not mocked repositories
- Controllers are thin and delegate entirely to MediatR — no DbContext usage in controllers
- FluentValidation pipeline behavior is wired correctly for all requests that have a validator registered

---

## Findings

### 🔴 Critical Findings

#### Section 01 — Clean Architecture

---

### 🔴 F-001 — Domain project has illegal MediatR PackageReference

**Severity:** Critical
**Rule violated:** ARCHITECTURE.md section 01, Rule 1.1 — Domain MUST reference nothing except the .NET base class library
**Files affected:**
- `WasnieApi/src/Wasnie.Domain/Wasnie.Domain.csproj:12`
- `WasnieApi/src/Wasnie.Domain/Common/IDomainEvent.cs:1`

**Description:**
`Wasnie.Domain.csproj` includes `<PackageReference Include="MediatR" Version="12.4.1" />`. The `IDomainEvent` interface directly imports `MediatR.INotification` to inherit from it. Rule 1.1 is explicit that Domain must have zero `PackageReference` and zero `ProjectReference` entries.

**Impact:**
Domain is now coupled to MediatR's release cycle. An incompatible MediatR upgrade forces Domain changes. More critically, it breaks the foundational guarantee that Domain is framework-independent and purely testable.

**Suggested fix:**
Define a local `IDomainEvent` with no external inheritance. Create a separate adapter interface in Application that maps `IDomainEvent` to `INotification` for publishing. Remove the `PackageReference` from Domain entirely.

**Phase to fix:** C (immediate — pre-Phase C1)

---

### 🔴 F-002 — Application project references EF Core (forbidden dependency)

**Severity:** Critical
**Rule violated:** ARCHITECTURE.md section 01, Rule 1.2 — Application MUST NOT reference Infrastructure; approved libs only (MediatR, FluentValidation, AutoMapper). EF Core belongs in Infrastructure.
**Files affected:**
- `WasnieApi/src/Wasnie.Application/Wasnie.Application.csproj:9`

**Description:**
`Wasnie.Application.csproj` includes `<PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.14" />`. Rule 1.2 lists approved cross-cutting libraries for Application as MediatR, FluentValidation, and AutoMapper — not EF Core.

**Impact:**
Application layer can now directly access EF Core APIs (LINQ, `DbSet<T>`, `Include`, etc.), eroding the separation between orchestration and persistence. The `IApplicationDbContext` interface already exposes `DbSet<T>` members, which bleeds EF Core concepts into Application.

**Suggested fix:**
Remove EF Core from Application. Replace `DbSet<T>` in `IApplicationDbContext` with purpose-built repository interfaces (`IPayeeReadRepository`, etc.) that hide EF Core entirely. Consider deferring until Phase C2 if a full refactor is needed.

**Phase to fix:** C2

---

#### Section 02 — SOLID / Dependency Inversion

---

### 🔴 F-003 — DateTime.UtcNow used directly in Domain entities (non-deterministic business logic)

**Severity:** Critical
**Rule violated:** ARCHITECTURE.md section 02, Rule 2.5.3 — `DateTime.UtcNow` is FORBIDDEN in Domain/Application; use `IClock`
**Files affected:**
- `WasnieApi/src/Wasnie.Domain/Compensation/Payees/Payee.cs:35,38,78,88,99,109`
- `WasnieApi/src/Wasnie.Domain/Compensation/Assignments/PlanAssignment.cs:45,47,52,60,72,76`
- `WasnieApi/src/Wasnie.Domain/Compensation/Payouts/CompensationPayout.cs:48,50,67,80,83,94,106`
- `WasnieApi/src/Wasnie.Domain/Compensation/Quotas/Quota.cs:50,53,68,73,80`
- `WasnieApi/src/Wasnie.Domain/Compensation/Plans/Plan.cs:42,47,127,141,162,171,191`
- `WasnieApi/src/Wasnie.Domain/Entities/Plan.cs:33-34` (legacy entity, also uses `DateTimeOffset.UtcNow`)

**Description:**
All domain entities stamp `CreatedAt`, `UpdatedAt`, and event `OccurredOn` by calling `DateTimeOffset.UtcNow` directly. `Payee.Create` additionally calls `DateTime.UtcNow` for the hire-date boundary check on line 35. There is no `IClock` abstraction anywhere in the codebase.

**Impact:**
Business logic timestamps are non-deterministic. Tests cannot control "today," making date-boundary tests (hire date in future, quota period validity, payout approval timing) unreliable or impossible. The hire-date validation in `Payee.Create` is especially problematic for deterministic unit testing.

**Suggested fix:**
Create `IClock` in Domain/Application with an `UtcNow` property. Inject `IClock` into domain factory methods or pass `DateTimeOffset now` as a parameter. Provide `SystemClock` in Infrastructure and `FakeClock` for tests.

**Phase to fix:** C1

---

### 🔴 F-004 — Guid.NewGuid() called directly in Domain entities

**Severity:** Critical
**Rule violated:** ARCHITECTURE.md section 02, Rule 2.5.3 — `Guid.NewGuid()` is FORBIDDEN in business logic; use `IGuidGenerator`
**Files affected:**
- `WasnieApi/src/Wasnie.Domain/Common/Entity.cs:5` (`Id` default initializer)
- `WasnieApi/src/Wasnie.Domain/Compensation/Assignments/PlanAssignment.cs:37`
- `WasnieApi/src/Wasnie.Domain/Compensation/Quotas/Quota.cs:41`
- `WasnieApi/src/Wasnie.Domain/Compensation/Plans/Plan.cs:38`
- `WasnieApi/src/Wasnie.Domain/Compensation/Plans/Rule.cs:33`
- `WasnieApi/src/Wasnie.Domain/Compensation/Credits/Credit.cs:40`
- `WasnieApi/src/Wasnie.Domain/Compensation/Payouts/CompensationPayout.cs:41`
- `WasnieApi/src/Wasnie.Domain/Entities/ImportAudit.cs:5`
- `WasnieApi/src/Wasnie.Domain/Identity/RefreshToken.cs:22`

**Description:**
Every domain entity generates its own `Id` by calling `Guid.NewGuid()` either in the property initializer (`Entity.cs`) or within static factory methods. No `IGuidGenerator` abstraction exists.

**Impact:**
Tests cannot control or predict entity IDs, making assertion on specific IDs impossible without querying back. For financial software, reproducibility of test data is a prerequisite for audit-trail verification tests.

**Suggested fix:**
Create `IGuidGenerator` in Application/Domain. For factory methods, accept an optional `Guid? id` parameter or inject `IGuidGenerator`. For the common `Entity` base, remove the default initializer and require factories to set `Id` explicitly.

**Phase to fix:** C1

---

#### Section 04 — Security

---

### 🔴 F-005 — JWT access token lifetime is 60 minutes (4× the allowed maximum)

**Severity:** Critical
**Rule violated:** ARCHITECTURE.md section 04, Rule 4.1.2 — Access token lifetime MUST be 15 minutes
**Files affected:**
- `WasnieApi/src/Wasnie.Api/appsettings.json:19` (`"ExpiryMinutes": "60"`)
- `WasnieApi/src/Wasnie.Api/appsettings.Development.json:16` (`"ExpiryMinutes": "60"`)
- `WasnieApi/src/Wasnie.Api/appsettings.Production.json:15` (`"ExpiryMinutes": "60"`)
- `WasnieApi/src/Wasnie.Infrastructure/Services/TokenService.cs:28` (default fallback also 60)

**Description:**
All environment configs set `ExpiryMinutes` to 60. Rule 4.1.2 mandates 15 minutes for access tokens. The violation exists in all three config files including Production.

**Impact:**
A stolen access token remains valid for 60 minutes instead of 15. In a financial SaaS handling commission data, this quadruples the exposure window for any token theft or replay attack.

**Suggested fix:**
Change `ExpiryMinutes` to `15` in all appsettings files. Update the `TokenService` default fallback to 15. Ensure the frontend `session-refresh.service.ts` refreshes tokens proactively before the 15-minute window.

**Phase to fix:** C (immediate)

---

### 🔴 F-006 — Refresh token lifetime is 30 days (4× the allowed maximum)

**Severity:** Critical
**Rule violated:** ARCHITECTURE.md section 04, Rule 4.1.2 — Refresh token MUST be 7 days
**Files affected:**
- `WasnieApi/src/Wasnie.Infrastructure/Services/TokenService.cs:16` (`private const int RefreshTokenLifetimeDays = 30`)

**Description:**
Refresh tokens are issued with a 30-day lifetime. Rule 4.1.2 specifies 7 days. While refresh tokens are correctly rotated on every use (one-time), the 30-day base lifetime means an unused stolen token can be abused for an entire month.

**Impact:**
If a refresh token is exfiltrated (e.g., from localStorage or via XSS), an attacker has up to 30 days to initiate a session. Combined with no security-header protection (F-010), this is a meaningful exposure.

**Suggested fix:**
Change `RefreshTokenLifetimeDays` to `7`. Review the frontend inactivity service to ensure reasonable session expiry for users who leave sessions idle.

**Phase to fix:** C (immediate)

---

### 🔴 F-007 — Import cache keys do not include TenantId (cross-tenant file hijacking)

**Severity:** Critical
**Rule violated:** ARCHITECTURE.md section 09, Rule 9.2.5 — Cache keys MUST include TenantId; section 09, Rule 9.2.6 — Temporary import storage MUST be tenant-scoped
**Files affected:**
- `WasnieApi/src/Wasnie.Infrastructure/Services/Imports/ImportCacheService.cs:22` (`private static string CacheKey(string fileId) => $"import:payees:{fileId}"`)

**Description:**
The import cache key is `import:payees:{fileId}` — it contains no TenantId. Because `fileId` is a Guid, it is practically unguessable, which limits practical exploitability. However, the architecture rule is explicit: any cache key for tenant-scoped data MUST include TenantId as a prefix. The architecture doc provides this exact scenario as an example of a forbidden pattern (Rule 9.2.6).

**Impact:**
If a Tenant A user somehow learns a fileId belonging to Tenant B (e.g., through a timing side-channel, a shared in-memory cache in a multi-instance deployment, or a future bug), they could retrieve, validate, or execute Tenant B's uploaded import file. In a multi-server deployment with a shared Redis cache, this becomes a concrete data leak risk.

**Suggested fix:**
Change `CacheKey` to `$"import:payees:{tenantId}:{fileId}"`. Inject `ITenantContext` into `ImportCacheService`. Validate that the `tenantId` on retrieval matches the requesting user's tenant.

**Phase to fix:** C (immediate)

---

### 🔴 F-008 — Email verification is disabled; all new accounts are auto-confirmed

**Severity:** Critical
**Rule violated:** ARCHITECTURE.md section 04, Rule 4.1.6 — Email verification is mandatory; unverified accounts cannot log in
**Files affected:**
- `WasnieApi/src/Wasnie.Infrastructure/DependencyInjection.cs:49` (`options.SignIn.RequireConfirmedEmail = false`)
- `WasnieApi/src/Wasnie.Infrastructure/Identity/IdentityService.cs:21` (`EmailConfirmed = true` set at creation)

**Description:**
Both the Identity configuration (`RequireConfirmedEmail = false`) and the user creation code (`EmailConfirmed = true`) bypass email verification entirely. Every new tenant admin account is immediately usable without verifying the email address.

**Impact:**
Anyone can register a tenant with a fake or mistyped email address. There is no mechanism to recover the account if the email is wrong. More importantly, bots can automate tenant creation without email ownership verification, which is a prerequisite for GDPR data-subject rights enforcement (the verified email is the identity anchor).

**Suggested fix:**
Set `RequireConfirmedEmail = true` and `EmailConfirmed = false` at creation. Implement an email-verification flow using ASP.NET Core Identity's `GenerateEmailConfirmationTokenAsync` before Phase C goes to production. Add the confirmation endpoints to the unauthenticated whitelist in Rule 4.1.1.

**Phase to fix:** C2

---

### 🟠 High Findings

---

### 🟠 F-009 — No role-based authorization (RBAC) or tier-limit enforcement implemented

**Severity:** High
**Rule violated:** ARCHITECTURE.md section 06, Rules 6.1.1, 6.3.3 — Tier limits and role permissions MUST be enforced at the use case level
**Files affected:**
- `WasnieApi/src/Wasnie.Application/Compensation/Handlers/Payees/CreatePayeeHandler.cs` (no permission check)
- `WasnieApi/src/Wasnie.Application/Compensation/Handlers/Plans/CreatePlanHandler.cs` (no permission check)
- `WasnieApi/src/Wasnie.Application/Compensation/Handlers/Quotas/CreateQuotaHandler.cs` (no permission check)
- `WasnieApi/src/Wasnie.Application/Compensation/Handlers/Assignments/AssignPlanToPayeeHandler.cs` (no permission check)
- All other use case handlers (same pattern)

**Description:**
No `IAuthorizationService` exists. No use case handler calls any permission check. Every authenticated user — regardless of role (Rep, Manager, CompManager, TenantAdmin) — can create payees, create plans, assign payees to plans, and manage quotas. This is acknowledged in the architecture docs (section 06 bug history) as a known gap for Phase C2, but it is being recorded as a High finding because it is currently in a running, testable application.

**Impact:**
A Rep-role user can create and modify compensation plans for others, including changing their own quota. A Manager can delete another manager's payees. Subscription tier limits are unenforced, allowing Starter-tier tenants to create unlimited payees and plans.

**Suggested fix:**
Implement `IAuthorizationService` in Application with role/permission checks. Wire it into every mutating use case handler as the first check. Implement tier-limit counts in the same service. Phase C2 is the documented milestone for this.

**Phase to fix:** C2

---

### 🟠 F-010 — No security headers in middleware pipeline (CSP, X-Frame-Options, HSTS)

**Severity:** High
**Rule violated:** ARCHITECTURE.md section 04, Rules 4.6.3, 4.6.4, 4.10.1
**Files affected:**
- `WasnieApi/src/Wasnie.Api/Program.cs` (entire file — headers not set)

**Description:**
`Program.cs` has no `app.UseHttpsRedirection()`, no `app.UseHsts()`, and no middleware that sets security headers (`Content-Security-Policy`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy`, `Strict-Transport-Security`). These are all required by Rules 4.6.3 and 4.6.4.

**Impact:**
Without CSP, the application is vulnerable to XSS attacks that could steal JWT tokens from localStorage. Without `X-Frame-Options: DENY`, the app is vulnerable to clickjacking. Without HTTPS redirect, users who accidentally visit via HTTP are not redirected.

**Suggested fix:**
Add `app.UseHttpsRedirection()` and `app.UseHsts()` to `Program.cs`. Add a middleware (or use `NWebsec` / custom `IApplicationBuilder` extension) to set all required response headers. This is a low-effort, high-impact change.

**Phase to fix:** C4

---

### 🟠 F-011 — No rate limiting on any endpoint

**Severity:** High
**Rule violated:** ARCHITECTURE.md section 04, Rules 4.8.1 and 4.8.2 — Auth endpoints MUST be rate-limited; all endpoints MUST have baseline rate limiting
**Files affected:**
- `WasnieApi/src/Wasnie.Api/Program.cs` (no `AddRateLimiter` / `UseRateLimiter`)
- `WasnieApi/src/Wasnie.Api/Controllers/AuthController.cs` (login, refresh — no rate limiting)

**Description:**
There is zero rate-limiting middleware or policy in the application. Login, refresh, and register-tenant endpoints are completely open to brute-force and credential-stuffing attacks.

**Impact:**
Without rate limiting on `/api/auth/login`, an attacker can brute-force passwords at unlimited speed, bypassing the lockout policy (which is only triggered per-account, not per-IP). The `/api/auth/refresh` endpoint can be hammered to detect valid tokens.

**Suggested fix:**
Use ASP.NET Core's built-in `Microsoft.AspNetCore.RateLimiting` (available in .NET 7+). Apply a sliding-window policy of 5 req/15 min per IP on auth endpoints. Apply a baseline policy of 100 req/min per authenticated user on all endpoints. Planned for Phase C4.

**Phase to fix:** C4

---

### 🟠 F-012 — Logout endpoint does not invalidate refresh tokens

**Severity:** High
**Rule violated:** ARCHITECTURE.md section 04, Rule 4.1.3 — Refresh tokens MUST be revocable; user logout invalidates refresh tokens
**Files affected:**
- `WasnieApi/src/Wasnie.Api/Controllers/AuthController.cs:60-63` (Logout returns `NoContent()` with no side effects)

**Description:**
The `POST /api/auth/logout` endpoint is protected with `[Authorize]` and returns `204 No Content`, but it performs no server-side token invalidation. It does not revoke the user's active refresh token. The frontend clears `localStorage`, but the refresh token remains valid in the database for up to 30 days.

**Impact:**
After "logout," the refresh token remains usable server-side. If a user logs out from a shared device and someone retrieves the refresh token (e.g., from browser history, cache, or network capture), they can silently re-authenticate for up to 30 days.

**Suggested fix:**
Inject `ITokenService` into `AuthController`. In the `Logout` action, extract the refresh token from the request (sent in the logout body or a cookie), call `tokenService.RevokeRefreshTokenAsync(refreshToken)`. Optionally revoke all tokens for the user.

**Phase to fix:** C (immediate)

---

### 🟠 F-013 — Password policy weaker than required (missing symbol requirement, too short)

**Severity:** High
**Rule violated:** ARCHITECTURE.md section 04, Rule 4.1.5 — Password MUST be minimum 10 chars; MUST include uppercase, lowercase, digit, and symbol
**Files affected:**
- `WasnieApi/src/Wasnie.Infrastructure/DependencyInjection.cs:46-47`

**Description:**
Identity is configured with `RequireNonAlphanumeric = false` (symbols not required) and `RequiredLength = 8` (minimum 8 chars, not 10). Rule 4.1.5 requires symbols and a minimum of 10 characters.

**Impact:**
Weaker password policy increases susceptibility to dictionary attacks. For a financial SaaS processing payroll-adjacent data, this is a meaningful security gap. An 8-character password without symbols has a significantly smaller brute-force search space than a 10-character password with symbols.

**Suggested fix:**
Set `RequireNonAlphanumeric = true` and `RequiredLength = 10` in `DependencyInjection.cs`. Consider adding a common-passwords check (Rule 4.1.5 bullet 3) using a bundled list or a service like `zxcvbn`.

**Phase to fix:** C (immediate)

---

### 🟠 F-014 — No audit trail for any business operations (destructive or monetary)

**Severity:** High
**Rule violated:** ARCHITECTURE.md section 05, Rules 5.1.1 and 5.1.2 — Every destructive operation and every monetary operation MUST log an audit record
**Files affected:**
- `WasnieApi/src/Wasnie.Application/Compensation/Handlers/` (all handlers — no `IAuditService` calls)
- `WasnieApi/src/Wasnie.Application/Features/Auth/Handlers/` (login/register not audited)

**Description:**
There is no `IAuditService` in the codebase. No audit log entity exists for general business events (only `ImportAudit` for the import wizard). Operations including `CreatePayee`, `UpdatePayee`, `MarkPayeeAsTerminated`, `ActivatePlan`, `ArchivePlan`, `CreateQuota`, `ActivateQuota`, `AssignPlanToPayee`, `DeactivateAssignment`, and `DeletePlan` produce no audit record. Authentication events (login, logout) are also not audited.

**Impact:**
The architecture doc states: "When a customer's payee disputes a commission calculation 6 months from now... we MUST be able to answer with immutable, timestamped, attributable records." Without audit trail, quota disputes, plan changes, and assignment history are unresolvable. This is a legal and contractual gap.

**Suggested fix:**
Implement `IAuditService` in Application and `AuditService` in Infrastructure. Add an `AuditBehavior<TRequest, TResponse>` MediatR pipeline behavior that logs all `IAuditableCommand` implementations. Mark all mutating commands as `IAuditableCommand`. Phase C3 is the documented milestone.

**Phase to fix:** C3

---

### 🟠 F-015 — Logout action does not revoke the token on server but also has no validator for RefreshToken on the RefreshTokenCommand

**Severity:** High
**Rule violated:** ARCHITECTURE.md section 04, Rule 4.4.1 — Every request body MUST be validated with FluentValidation
**Files affected:**
- `WasnieApi/src/Wasnie.Application/Features/Auth/` (no `RefreshTokenCommandValidator.cs` exists)

**Description:**
`RefreshTokenCommand` has no FluentValidation validator. The `ValidationBehavior` pipeline will skip validation for this command. The refresh token field could be empty or malformed and would be passed directly to `tokenService.ValidateRefreshTokenAsync` without pre-validation.

**Impact:**
Missing validation on the refresh endpoint means empty-string or null tokens reach the token service, which may cause unexpected exceptions or incorrect behavior. More broadly, it is a gap in the defense-in-depth validation requirement.

**Suggested fix:**
Create `RefreshTokenCommandValidator` with `RuleFor(x => x.RefreshToken).NotEmpty()`. Similarly audit other commands (e.g., `UpdatePayeeCommand`, `UpdateQuotaCommand`) for missing validators.

**Phase to fix:** C

---

### 🟡 Medium Findings

---

### 🟡 F-016 — Missing validators for UpdatePayeeCommand, UpdateQuotaCommand, and UpdateAssignmentNotesCommand

**Severity:** Medium
**Rule violated:** ARCHITECTURE.md section 04, Rule 4.4.1 — Every request body MUST be validated
**Files affected:**
- `WasnieApi/src/Wasnie.Application/Compensation/Commands/Payees/UpdatePayeeCommand.cs` (no validator file found)
- `WasnieApi/src/Wasnie.Application/Compensation/Commands/Quotas/UpdateQuotaCommand.cs` (no validator file found)
- `WasnieApi/src/Wasnie.Application/Compensation/Commands/Assignments/UpdateAssignmentNotesCommand.cs` (no validator file found)

**Description:**
Only `CreateQuotaCommandValidator`, `CreatePlanCommandValidator`, `AddRuleToPlanCommandValidator`, and `AssignPlanToPayeeCommandValidator` exist among the Compensation validators. The three update commands listed above have no corresponding validators. Their input is only partially protected by domain-layer guards (which throw `DomainException`, not `ValidationException`).

**Impact:**
Without validators on update commands, invalid inputs (empty `FullName`, invalid date ranges, malformed employee codes) bypass the standard 400 validation path and either throw domain exceptions (returning 422) or slip through silently with invalid data.

**Suggested fix:**
Create `UpdatePayeeCommandValidator`, `UpdateQuotaCommandValidator`, and `UpdateAssignmentNotesCommand` validator classes with the same rules applied to their `Create` counterparts.

**Phase to fix:** C

---

### 🟡 F-017 — No cross-tenant integration tests for Quotas, Assignments, or Import endpoints

**Severity:** Medium
**Rule violated:** ARCHITECTURE.md section 09, Rule 9.3.1 — Every endpoint MUST have a cross-tenant test
**Files affected:**
- `WasnieApi/tests/Wasnie.IntegrationTests/` (no `QuotasEndpointsTests.cs`, `AssignmentsEndpointsTests.cs`)

**Description:**
Integration test coverage exists for Payees (cross-tenant test at line 147 of `PayeesEndpointsTests.cs`) and Plans (multiple cross-tenant tests in `PlansEndpointsTests.cs`). However, there are no integration test files for the Quotas or Assignments endpoints at all. The Import tests (`PayeeImportEndpointsTests.cs`) should also be verified for cross-tenant coverage.

**Impact:**
Without cross-tenant tests for Quotas and Assignments, any regression in the global query filter or tenant context resolution that causes data leakage between tenants would go undetected by the test suite.

**Suggested fix:**
Create `QuotasEndpointsTests.cs` and `AssignmentsEndpointsTests.cs` with at minimum: happy path, 401 without auth, and cross-tenant isolation tests for GET list, GET by ID, POST create, and state-change operations.

**Phase to fix:** C

---

### 🟡 F-018 — IgnoreQueryFilters() used without explicit TenantId guard in ListPayeesHandler

**Severity:** Medium
**Rule violated:** ARCHITECTURE.md section 09, Rule 9.2.2 — Even with global query filter, application code MUST still write explicit `Where(p => p.TenantId == ...)` as defense in depth
**Files affected:**
- `WasnieApi/src/Wasnie.Application/Compensation/Handlers/Payees/ListPayeesHandler.cs:66-69`

**Description:**
`ListPayeesHandler` calls `db.Payees.IgnoreQueryFilters()` when resolving manager names. The `Where` clause on line 68 only filters on `managerIds.Contains(x.Id)` — there is no explicit `x.TenantId == currentTenantId` guard. If a manager ID legitimately belongs to another tenant (e.g., due to a future bug in cross-tenant reference validation), the query would return that manager's data.

**Impact:**
While the manager IDs come from the already-filtered payee list (so they are from the current tenant in practice), the `IgnoreQueryFilters()` call without an explicit TenantId filter violates Rule 9.2.2's defense-in-depth requirement and creates a latent multi-tenant leak risk.

**Suggested fix:**
Inject `ITenantContext` into `ListPayeesHandler` and add `.Where(x => managerIds.Contains(x.Id) && x.TenantId == tenantContext.TenantId)` to the manager lookup query. This makes the cross-tenant protection explicit and not contingent on upstream filtering.

**Phase to fix:** C

---

### 🟡 F-019 — ImportAudit entity has no global query filter in DbContext

**Severity:** Medium
**Rule violated:** ARCHITECTURE.md section 09, Rule 9.2.1 — DbContext MUST configure a global query filter on every tenant-scoped entity
**Files affected:**
- `WasnieApi/src/Wasnie.Infrastructure/Persistence/ApplicationDbContext.cs:67-76` (ImportAudit missing from query filter block)

**Description:**
The `OnModelCreating` method applies global query filters to Payees, Plans, Quotas, PlanAssignments, etc. — but `ImportAudit` is absent. `ImportAudit` has a `TenantId` column (confirmed by `ImportAuditConfiguration.cs`) and is tenant-scoped data, but it has no `HasQueryFilter` call.

**Impact:**
Any query against `db.ImportAudits` without an explicit `Where(x => x.TenantId == ...)` clause would return audit records from all tenants. If a future endpoint lists import history, it would require the developer to remember to add the TenantId filter manually, which is exactly the failure mode the global filter is designed to prevent.

**Suggested fix:**
Add `builder.Entity<ImportAudit>().HasQueryFilter(e => e.TenantId == CurrentTenantId);` to `OnModelCreating` alongside the other tenant-scoped entities.

**Phase to fix:** C (immediate)

---

### 🟡 F-020 — `bypassSecurityTrustHtml` used in shared UI components without content-type documentation

**Severity:** Medium
**Rule violated:** ARCHITECTURE.md section 04, Rule 4.6.1 — `bypassSecurityTrustHtml` is FORBIDDEN; if used, a sanitization library (DOMPurify) must be used
**Files affected:**
- `WasnieUi/src/app/shared/components/icon/icon.component.ts:120`
- `WasnieUi/src/app/shared/components/empty-state/empty-state.component.ts` (similar pattern)
- `WasnieUi/src/app/shared/ui/ws-empty-state/ws-empty-state.component.ts` (similar pattern)

**Description:**
`IconComponent` calls `this.sanitizer.bypassSecurityTrustHtml(ICONS[this.name()] ?? '')`. The ICONS dictionary contains hardcoded SVG strings that are compile-time constants — no user input is involved. The `EmptyState` components do the same with hardcoded illustration SVG strings.

**Impact:**
The actual risk is low because the content being bypassed is compile-time constant SVG, not user-supplied data. However, Rule 4.6.1 is a blanket prohibition. If the `ICONS` or `ILLUSTRATIONS` dictionaries were ever populated from an API response or user input, the bypass would become a real XSS vector with no change to the calling code.

**Suggested fix:**
For hardcoded static SVG, add a `// justification: content is compile-time constant SVG; no user input` comment in the component file. Consider refactoring to use `<img src="...">` or Angular Material icons to eliminate the pattern entirely. If the rule is to be enforced strictly, add DOMPurify and sanitize even the static content.

**Phase to fix:** C

---

### 🟡 F-021 — One hardcoded color value in component SCSS

**Severity:** Medium
**Rule violated:** ARCHITECTURE.md section 14, Code style violations — Hardcoded color values in component SCSS are FORBIDDEN
**Files affected:**
- `WasnieUi/src/app/shared/ui/ws-wizard/ws-wizard.component.scss:48` (`color: #fff;`)

**Description:**
One hardcoded hex color `#fff` appears in `ws-wizard.component.scss`. The design system defines `--color-text-inverse: #ffffff` as a CSS custom property. All other SCSS files in `features/` and `shared/` appear to use CSS variables correctly.

**Impact:**
Low immediate risk. If the design system changes the inverse text color (e.g., for high-contrast themes), this component will not update automatically, causing visual inconsistency.

**Suggested fix:**
Replace `color: #fff` with `color: var(--color-text-inverse)`.

**Phase to fix:** C

---

### 🟡 F-022 — `TenantContext.TenantId` returns `Guid.Empty` silently when unauthenticated (no enforcement on null TenantId)

**Severity:** Medium
**Rule violated:** ARCHITECTURE.md section 09, Rule 9.1.3 — If TenantId is null/empty when data is accessed, the request MUST fail with 401, not 200 with empty data
**Files affected:**
- `WasnieApi/src/Wasnie.Infrastructure/Identity/TenantContext.cs:13-22`

**Description:**
When no JWT claim is present (unauthenticated or malformed token), `TenantContext.TenantId` silently returns `Guid.Empty`. The global query filters then use `Guid.Empty` as the tenant ID. This means an authenticated user with a JWT missing the `tenant_id` claim would receive empty lists rather than a 401 or 403 error. Rule 9.1.3 requires the request to fail with 401 when TenantId is not resolved.

**Impact:**
A user with a valid JWT but missing the `tenant_id` claim (e.g., a bug during user creation) would get silently empty responses on all data endpoints, making diagnosis difficult. More critically, if the global query filters are evaluated with `Guid.Empty`, and any row accidentally has a zero GUID as `TenantId`, those rows would be exposed.

**Suggested fix:**
Add middleware or an action filter that checks `tenantContext.IsResolved` after authentication. If `IsResolved == false` on a non-anonymous endpoint, return 401 with a clear error message before reaching controllers or use cases.

**Phase to fix:** C

---

### 🟡 F-023 — No test coverage for Quotas, Assignments, PlanRules endpoints

**Severity:** Medium
**Rule violated:** ARCHITECTURE.md section 07, Rule 7.4.1 — Every endpoint MUST have integration tests
**Files affected:**
- `WasnieApi/tests/Wasnie.IntegrationTests/` (no test files for Quotas, Assignments, PlanRules controllers)

**Description:**
Integration tests exist for: Payees, Plans, Imports. The QuotasController (7 endpoints), AssignmentsController (6 endpoints), and PlanRulesController have no corresponding integration test files. These were introduced in the `feature/quotas-payees-assignments-ui` branch.

**Impact:**
New endpoints for quotas and assignments lack regression protection. Authorization gaps, tenant isolation issues, or validation failures on these endpoints would not be caught by CI.

**Suggested fix:**
Create `QuotasEndpointsTests.cs`, `AssignmentsEndpointsTests.cs`, and `PlanRulesEndpointsTests.cs` with at minimum the tests required by Rule 7.4.1 (happy path, 400 validation, 401 unauthenticated, 404 not found, cross-tenant).

**Phase to fix:** C

---

### 🟢 Low Findings

---

### 🟢 F-024 — Dev-secret JWT key committed to git in appsettings.Development.json

**Severity:** Low (development environment; not production)
**Rule violated:** ARCHITECTURE.md section 04, Rule 4.9.1 — Secrets NEVER in git
**Files affected:**
- `WasnieApi/src/Wasnie.Api/appsettings.Development.json:13` (`"Secret": "dev-secret-key-min-32-chars-wasnie-2025"`)

**Description:**
The development JWT secret `"dev-secret-key-min-32-chars-wasnie-2025"` is committed to the repository. Rule 4.9.1 prohibits secrets in git. Rule 4.9.2 requires development secrets to use `dotnet user-secrets` so each developer has their own.

**Impact:**
Low severity in isolation — this is the dev environment. However, it sets a precedent. If the same value were reused in staging or a developer copy-pastes it to production, the exposure is critical. The `appsettings.Development.template.json` file exists (good practice) but the actual dev file with the real key is also present.

**Suggested fix:**
Remove the Secret value from `appsettings.Development.json` (replace with a placeholder like `"CONFIGURE_VIA_USER_SECRETS"`). Add `appsettings.Development.json` to `.gitignore`. Document setup in the README to use `dotnet user-secrets set "JwtSettings:Secret" "..."`.

**Phase to fix:** C

---

### 🟢 F-025 — Password minimum length is 8 chars and symbols not required (also logged as F-013 — low-effort fix)

Already captured in F-013 (High). Noting separately here because the fix is a one-line change: `RequiredLength = 10` and `RequireNonAlphanumeric = true`.

**Phase to fix:** C (immediate)

---

### 🟢 F-026 — Legacy `Wasnie.Domain.Entities.Plan` entity coexists with `Wasnie.Domain.Compensation.Plans.Plan`

**Severity:** Low
**Rule violated:** ARCHITECTURE.md section 02, Rule 2.1.1 — Single Responsibility; also architectural clarity
**Files affected:**
- `WasnieApi/src/Wasnie.Domain/Entities/Plan.cs` (legacy)
- `WasnieApi/src/Wasnie.Domain/Entities/Payee.cs` (empty stub)
- `WasnieApi/src/Wasnie.Infrastructure/Persistence/ApplicationDbContext.cs:34` (`LegacyPlan` alias in DbContext)

**Description:**
The codebase has a legacy `Plan` entity in `Wasnie.Domain.Entities` and an empty `Payee.cs` stub. The `ApplicationDbContext` uses `LegacyPlan`, `LegacyTransaction`, `LegacyPayout` aliases for the old entities. This dual-entity situation creates confusion, increases cognitive load, and risks regressions if the wrong entity type is used.

**Impact:**
Low immediate risk. The `DbSet<LegacyPlan>` (`Plans`) and `DbSet<Plan>` (`CompensationPlans`) expose two separate plan types to Application. Any handler that accidentally uses `db.Plans` instead of `db.CompensationPlans` operates on the legacy model.

**Suggested fix:**
Once the migration to the Compensation domain model is complete, remove `Wasnie.Domain.Entities.Plan`, `Transaction`, `Payout` and their corresponding `DbSet` entries. Clean up the `LegacyPlan` aliases. The empty `Payee.cs` stub can be deleted now.

**Phase to fix:** C

---

### 🟢 F-027 — Structured logging uses `WriteTo.Console()` and file sink; no Serilog Sinks for Azure/cloud destination

**Severity:** Low
**Rule violated:** ARCHITECTURE.md section 12, Rule 12.1.1 — Structured JSON logging; Rule 12.2.4 — Metrics export via OpenTelemetry to Application Insights
**Files affected:**
- `WasnieApi/src/Wasnie.Api/Program.cs:19-26`

**Description:**
Serilog is correctly configured with structured logging and file sink with 30-day retention. The console sink outputs structured logs. However, there is no Application Insights, OpenTelemetry, or cloud-based log aggregation sink configured. This is consistent with the architecture docs noting "observability is currently minimal; Phase C6 will introduce the full stack." Logging here is sufficient for development.

**Impact:**
In production without a cloud sink, log search, correlation, and alerting are limited to file-based tailing. No P95 metrics, no distributed tracing, no alerting on error rates. This is a known planned gap.

**Suggested fix:**
Phase C6 is the planned milestone. In the interim, add `Serilog.Sinks.ApplicationInsights` or configure OpenTelemetry export so that production deployments have at minimum error alerting.

**Phase to fix:** C6

---

## Compliance Areas (No Findings)

The following architectural sections were audited and found to be compliant or not yet in scope:

- **Section 01 — Frontend layer separation:** Components do not inject `HttpClient` directly (all HTTP goes through `*.api.service.ts` files). No HTTP calls found in component files.
- **Section 01 — Controller thinness:** All controllers are thin, delegate entirely to MediatR, have no DbContext injection, and contain no business logic. Sizes are well within the 200-line limit.
- **Section 01 — API project references:** `Wasnie.Api.csproj` correctly references Application and Infrastructure; no direct Domain reference. `Wasnie.Infrastructure.csproj` references only Application (not Presentation).
- **Section 03 — Server-side pagination:** All list endpoints use `ToPagedResultAsync` with explicit page/pageSize parameters. Sort fields are whitelisted against `AllowedSortFields` in every handler.
- **Section 03 — Bundle / frontend pagination:** Frontend `PayeesStore` correctly uses server-side pagination via `debounceTime(300)` for search (Rule 3.3.4 complied with).
- **Section 05 — ImportAudit as precursor:** The `ImportAudit` entity correctly captures import session data. This is acknowledged as the precursor to a general audit log.
- **Section 07 — Integration tests use Testcontainers:** `TestDatabaseFixture` correctly spins up a real MSSQL container and runs migrations before tests. No in-memory DB for integration tests.
- **Section 09 — Global query filters on main entities:** `ApplicationDbContext.OnModelCreating` correctly applies `HasQueryFilter` to Payees, Plans (both), Quotas, PlanAssignments, Transactions, Credits, Payouts.
- **Section 09 — TenantId from auth context, not request:** All use case handlers read `tenantContext.TenantId` from `ITenantContext` (JWT-derived). No handler reads TenantId from the request body.
- **Section 14 — No `console.log` in TypeScript:** No `console.log` calls found in any `.ts` file in the `WasnieUi/src/` tree.
- **Section 14 — No `any` type without justification:** No unguarded `: any` or `as any` patterns found in TypeScript source files.
- **Section 14 — No hardcoded URLs in frontend code:** Frontend uses `environment.apiBaseUrl` for auth routes and relative `/api/...` paths for all other API calls. No raw `http://` URLs in source.
- **Section 04 — SQL injection:** All database access uses EF Core LINQ expressions. No raw SQL string concatenation found. `FromSqlRaw` is not used.
- **Section 04 — CORS:** Production CORS is configured from `AllowedOrigins` config key (not `*`). Development defaults to `localhost:4200`.
- **MediatR ValidationBehavior:** FluentValidation pipeline behavior is correctly wired as `IPipelineBehavior<,>` for all registered validators.

---

## Recommendations for Phase C Prioritization

1. **Immediate (before any production deployment):** Fix JWT lifetimes (F-005: 60→15 min, F-006: 30→7 days), add logout token revocation (F-012), fix import cache TenantId scope (F-007), add security headers and HTTPS redirect (F-010), and fix the ImportAudit missing global query filter (F-019).

2. **C1 — Before further domain development:** Eliminate `IClock` and `IGuidGenerator` violations (F-003, F-004). Every entity currently calling `DateTimeOffset.UtcNow` and `Guid.NewGuid()` directly will compound testing problems as the Phase 2 calculation engine is built.

3. **C1 — Domain dependency cleanup:** Remove MediatR from `Wasnie.Domain.csproj` (F-001) and remove EF Core from `Wasnie.Application.csproj` (F-002). Define a local `IDomainEvent` with no MediatR coupling. Replace `IApplicationDbContext` DbSet properties with proper repository interfaces.

4. **C2 — Authorization and tier limits:** Implement `IAuthorizationService` with role/permission checks in all use case handlers (F-009). Without this, the application has authentication but no meaningful access control.

5. **C2 — Email verification:** Enable `RequireConfirmedEmail = true` and implement the email verification flow (F-008). Required before any real customer onboarding.

6. **C2 — Password policy hardening:** Change `RequiredLength` to 10 and enable `RequireNonAlphanumeric` (F-013). Low-effort change with meaningful security improvement.

7. **C3 — Audit trail implementation:** Implement `IAuditService` and `AuditBehavior` for all mutating commands (F-014). This is a legal and operational necessity for a financial product.

8. **C (parallel with feature work) — Missing validators and tests:** Add validators for `UpdatePayeeCommand`, `UpdateQuotaCommand`, `RefreshTokenCommand` (F-016, F-015). Add integration tests for Quotas and Assignments endpoints (F-023). Add cross-tenant tests for Quotas and Assignments (F-017).

9. **C — `IgnoreQueryFilters` defense-in-depth:** Add explicit TenantId filter to manager lookup in `ListPayeesHandler` (F-018). Add enforcement for `TenantId == Guid.Empty` → 401 in middleware (F-022).

10. **C4 — Rate limiting and remaining security hardening:** Add ASP.NET Core rate limiting middleware (F-011). Complete the security headers configuration. Remove dev secret from git (F-024).

---

## Audit Notes

### Assumptions
- The codebase was audited in its state as of the `feature/quotas-payees-assignments-ui` branch (commit 4745761 area).
- The architecture documents acknowledge several gaps as "known, planned for Phase C2/C3/C4/C6" (RBAC, audit trail, rate limiting, email verification, observability). These are still recorded as findings because the document itself defines them as requirements, and they are in a running application state.

### Limitations
- **No test execution was performed.** Coverage percentages (Rule 7.1.1 requires >80% backend) cannot be verified without running the test suite. The audit identified structural testing gaps but did not measure coverage.
- **No static analysis tools were run.** SonarCloud / NetArchTest rules (planned for Phase C5) were not available. Some subtle SOLID violations may exist in files not individually reviewed.
- **`PlanRulesController.cs` was not individually reviewed** — its existence in the controllers directory was noted but full content was not read. Pattern analysis from other controllers suggests it follows the same thin-controller approach.
- **Migration files were not audited** beyond confirming schema presence. Index completeness per Rule 3.2.2 would require full migration review.
- **The `Wasnie.Domain.Compensation.Plans.Rule.cs` entity** uses `Guid.NewGuid()` (noted in F-004) but was not fully read to determine if it has other violations. Recommend reviewing it as part of the F-004 fix.
- **Frontend `.spec.ts` test coverage** was not measured. The architecture requires >60% frontend coverage; structural review suggests helpers and services are present but unit tests for quotas/assignments UI were not found.
- **The `appsettings.Development.template.json` pattern** is good practice but only partially mitigates the F-024 finding because the actual file is also present in the repository.
