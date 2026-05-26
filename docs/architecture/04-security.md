# 04 — Security Requirements

**Reading time:** ~10 min
**Applies to:** Backend, Frontend, Infrastructure

---

## Why this matters

Wasnie handles payroll-adjacent data. A security breach means:

- Salary data of every customer's sales team exposed
- Commission plans revealed (competitive intelligence theft)
- Customer trust destroyed (a SPM vendor cannot recover from this)
- Legal liability under GDPR (€20M or 4% of revenue, whichever higher)
- Class-action lawsuits

The rules below are the minimum. They are written assuming bad actors actively try to break Wasnie.

---

## 4.1 Authentication

### Rule 4.1.1 — Every endpoint requires authentication

ONLY these endpoints MAY be unauthenticated:
- `POST /auth/login`
- `POST /auth/register-tenant`
- `POST /auth/refresh`
- `POST /auth/forgot-password`
- `POST /auth/reset-password`
- `POST /auth/verify-email`
- `GET /health` (basic health check)
- `GET /health/detailed` (Phase C6; auth-gated even though "health")

All others MUST require a valid JWT.

### Rule 4.1.2 — JWT tokens MUST be short-lived

| Token | Lifetime |
|---|---|
| Access token | **15 minutes** |
| Refresh token | **7 days** (rotated on use) |
| Email verification token | **24 hours** |
| Password reset token | **1 hour** |

Refresh tokens MUST be **rotated on every use** (one-time use). The old refresh token is invalidated when a new pair is issued.

### Rule 4.1.3 — Refresh tokens MUST be revocable

Storage in DB (not just signed JWT). User logout invalidates refresh tokens. Admins can invalidate all sessions for a user.

### Rule 4.1.4 — Lockout after failed attempts

- 5 failed login attempts within 15 minutes → account locked 15 minutes
- 10 failed attempts within 1 hour → account locked 1 hour, admin notified
- 20 failed attempts within 24 hours → account locked, mandatory password reset

### Rule 4.1.5 — Password requirements

- Minimum 10 characters
- MUST include uppercase, lowercase, digit, symbol
- MUST NOT match top 10,000 most common passwords
- MUST NOT match user's email or tenant name
- Hashed with **Argon2id** (preferred) or **bcrypt** (cost factor >= 12)
- NEVER stored in plain text or reversible encryption
- NEVER logged

### Rule 4.1.6 — Email verification mandatory

New tenants/users MUST verify email before access. Unverified accounts cannot log in.

### Rule 4.1.7 — 2FA optional in Phase 1, mandatory for Scale+ tiers

TOTP-based 2FA (Google Authenticator, Authy, 1Password). Phase 1 makes it optional. Scale and Enterprise tiers eventually enforce it (Phase C2 or later).

---

## 4.2 Authorization

(Full detail in file 06 — Authorization)

### Rule 4.2.1 — Every mutation requires authorization check

NEVER trust the user. Even authenticated users may not have permission for every action.

### Rule 4.2.2 — Authorization is server-side ONLY

Hiding UI buttons is convenience, not security. The backend MUST enforce permissions regardless of what the frontend sends.

---

## 4.3 Multi-tenant isolation

(Full detail in file 09 — Multi-tenant isolation)

### Rule 4.3.1 — Every query touching tenant data MUST filter by tenant

ZERO exceptions. See file 09 for enforcement mechanisms.

### Rule 4.3.2 — Tenant ID MUST come from the auth context, NOT the request

If the frontend can send `tenantId` in a query and the backend uses it, a malicious user can read another tenant's data. Tenant ID MUST be derived from the JWT claims.

---

## 4.4 Input validation

### Rule 4.4.1 — Every request body MUST be validated

Use FluentValidation (backend) and reactive forms / signal validators (frontend).

Validation rules MUST be defined per DTO in Application layer:

```csharp
public class CreatePayeeRequestValidator : AbstractValidator<CreatePayeeRequest>
{
    public CreatePayeeRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(200);
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(255);
        RuleFor(x => x.EmployeeCode)
            .NotEmpty()
            .MaximumLength(50)
            .Matches("^[A-Za-z0-9_-]+$");  // ← explicit allowed chars
        RuleFor(x => x.HireDate)
            .Must(d => d >= new DateOnly(1950, 1, 1))
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow));
    }
}
```

### Rule 4.4.2 — Validation FAILS closed

If validation logic throws unexpectedly, the request MUST be rejected (HTTP 400), not allowed.

### Rule 4.4.3 — Frontend validation is convenience, NOT security

Frontend MUST validate too, but ALL validation MUST be re-checked server-side. Never trust client-side validation alone.

### Rule 4.4.4 — File uploads MUST be sandboxed

- Validate file extension (whitelist, not blacklist)
- Validate MIME type
- Validate file size (max 5 MB for imports)
- Validate magic bytes (file content matches claimed extension)
- Store outside web root
- NEVER execute uploaded files
- Scan with antivirus (Phase C4)

---

## 4.5 SQL injection prevention

### Rule 4.5.1 — NEVER concatenate SQL strings

FORBIDDEN:
```csharp
var sql = $"SELECT * FROM Payees WHERE Email = '{email}'";
```

REQUIRED:
```csharp
var payee = await _dbContext.Payees
    .Where(p => p.Email == email)
    .FirstOrDefaultAsync();
```

OR (raw SQL when needed):
```csharp
var payee = await _dbContext.Payees
    .FromSqlInterpolated($"SELECT * FROM Payees WHERE Email = {email}")
    .FirstOrDefaultAsync();
// FromSqlInterpolated parameterizes correctly. Direct string concat does NOT.
```

### Rule 4.5.2 — Dynamic LINQ is FORBIDDEN

`System.Linq.Dynamic.Core` and similar libraries allow `OrderBy("name")` with string field names. This is **only** acceptable if the string is from a strict whitelist. Otherwise FORBIDDEN.

---

## 4.6 XSS prevention

### Rule 4.6.1 — Angular's sanitization MUST NOT be bypassed

NEVER use `[innerHTML]` with user-supplied data. NEVER use `bypassSecurityTrustHtml()`.

If rich text is required (Phase 2+), use a sanitization library like DOMPurify.

### Rule 4.6.2 — User-supplied URLs MUST be validated

`<a [href]="userUrl">` is unsafe if `userUrl` is not validated. Whitelist schemes (http, https, mailto) and validate domain when relevant.

### Rule 4.6.3 — Content Security Policy (CSP) MUST be set

The backend MUST send CSP headers (Phase C4):

```
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; connect-src 'self';
```

### Rule 4.6.4 — Other security headers MUST be set

- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy: geolocation=(), camera=(), microphone=()`
- `Strict-Transport-Security: max-age=31536000; includeSubDomains` (only in production HTTPS)

---

## 4.7 CSRF protection

### Rule 4.7.1 — State-changing endpoints MUST have CSRF protection

For session-based auth: CSRF tokens. For JWT-based auth: SameSite cookies + custom header verification.

Wasnie uses JWT in `Authorization: Bearer ...` header → CSRF is mostly mitigated, but custom header verification is still required as defense in depth.

### Rule 4.7.2 — CORS MUST be restrictive

Production CORS MUST whitelist exact origins:
- `https://app.wasnie.com`
- `https://*.wasnie.com` (if subdomain-per-tenant later)

NEVER `Access-Control-Allow-Origin: *` in production.

---

## 4.8 Rate limiting

### Rule 4.8.1 — Auth endpoints MUST be rate-limited

- `/auth/login`: 5 attempts / 15 min per IP, 5 / 15 min per email
- `/auth/forgot-password`: 3 / hour per email
- `/auth/refresh`: 60 / hour per refresh token (anything more is suspicious)
- `/auth/verify-email`: 5 / hour per token

### Rule 4.8.2 — All endpoints MUST have a baseline rate limit

100 requests / minute per authenticated user. Sensitive endpoints (imports, bulk operations) lower.

Implementation: middleware (Phase C4). Headers `X-RateLimit-*` returned with each response.

---

## 4.9 Secrets management

### Rule 4.9.1 — Secrets NEVER in code or git

- DB connection strings
- JWT signing keys
- Third-party API keys (Groq, SendGrid, Stripe, etc.)
- OAuth secrets
- Encryption keys

NEVER committed to git. NEVER in `appsettings.json` for production. NEVER in environment variables on a shared host.

### Rule 4.9.2 — Secrets MUST be in Azure Key Vault (or equivalent)

Production: Azure Key Vault. Development: User Secrets (`dotnet user-secrets`). Each developer has their own.

### Rule 4.9.3 — JWT signing key MUST be rotated regularly

Rotation policy: every 90 days. Old keys remain valid for 7 days (to support in-flight tokens).

### Rule 4.9.4 — Secrets MUST be redacted from logs

Application logs MUST redact:
- Passwords (even hashed)
- Tokens (access, refresh, reset, verification)
- API keys
- Anything matching pattern `Authorization: Bearer ...`

---

## 4.10 HTTPS

### Rule 4.10.1 — Production MUST be HTTPS only

HTTP MUST redirect to HTTPS. HSTS header set with 1-year max-age.

### Rule 4.10.2 — TLS 1.2 minimum

TLS 1.0 and 1.1 disabled at infrastructure level. Prefer TLS 1.3.

---

## 4.11 PII handling

### Rule 4.11.1 — Collect only what is needed

Wasnie processes commission data, not full HR data. We DO NOT store:

- Date of birth (unless legally required somewhere — currently none)
- Nationality
- Gender
- Marital status
- Social Security Number / Tax ID
- Banking data (IBAN, BIC)
- Personal addresses

Even if customers send these in imports, we **explicitly ignore them** with a clear UI message.

### Rule 4.11.2 — Email is PII

Treat employee email as PII:
- Encrypted at rest (DB-level encryption is acceptable)
- Redacted in logs (only show domain, not local part, in non-critical logs)
- Right to be forgotten supported (GDPR Article 17)

### Rule 4.11.3 — Data retention

- Active payees: indefinite while customer subscription active
- Terminated payees: 7 years (financial compliance)
- Soft-deleted records: 30 days, then hard delete
- Audit logs: 7 years minimum, immutable

### Rule 4.11.4 — Data export and deletion

Users MUST be able to:
- Export their own data (GDPR Article 15)
- Request deletion of their data (GDPR Article 17, subject to legal retention)

Implementation in Phase 8 (likely; can be earlier if customer demands).

---

## 4.12 Dependency security

### Rule 4.12.1 — No known-vulnerable dependencies

CI MUST run:
- `npm audit` (frontend) — fails on high/critical
- `dotnet list package --vulnerable` (backend) — fails on any vulnerability
- Snyk or GitHub Dependabot enabled

### Rule 4.12.2 — Dependency updates reviewed

Updates that touch security-related packages (Identity, JWT, EF Core, Angular, anything authentication-related) MUST be reviewed manually before merging.

---

## Enforcement

- **CI security scans** (Phase C5) fail builds on vulnerabilities
- **Middleware** enforces auth, rate limiting, CSRF, headers (Phase C4)
- **Penetration testing** before any production launch with paying customer
- **OWASP Top 10 checklist** reviewed quarterly

---

## Bug history

- **None yet related to security in Phase A.** Phase B2 audit will examine current security posture; findings tracked in `Audit_Findings.md`.
- **Anticipated:** secrets in `appsettings.json` are likely (Phase 1 development convenience). Must be moved to Key Vault before production.
