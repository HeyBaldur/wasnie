# Security Policy

## Supported versions

| Version | Supported |
|---|---|
| Current `main` branch | Yes |
| All prior releases | No |

## Reporting a vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Report security issues by email to the project maintainer:

**Contact:** Rodolfo A. Calvo Jaubert  
**Email:** fillocj.rc@gmail.com  
**Subject line:** `[WASNIE SECURITY] <brief description>`

### What to include

- A clear description of the vulnerability
- Steps to reproduce, or a proof-of-concept (if safe to share)
- The component affected (API, UI, authentication, multi-tenancy boundary, etc.)
- Potential impact assessment
- Your contact details for follow-up

### Response timeline

| Stage | Target |
|---|---|
| Acknowledgement | Within 48 hours |
| Initial assessment | Within 5 business days |
| Resolution or mitigation plan | Depends on severity |

Critical issues (authentication bypass, cross-tenant data access, secret exposure) will be treated as highest priority.

## Security architecture notes

### Multi-tenancy

Every API request is scoped to a single tenant via the `tenant_id` JWT claim. EF Core global query filters enforce tenant isolation at the ORM layer — every query against a tenant-scoped entity automatically includes a `WHERE TenantId = @currentTenant` predicate. There is no opt-out path.

### Secrets management

- No secrets are stored in source control. See the Configuration section in [README.md](README.md).
- The JWT signing secret must be at least 32 characters and generated randomly per environment.
- Production credentials are injected at runtime via environment variables or a secrets manager (Azure Key Vault recommended).
- Refresh tokens are stored hashed (SHA-256) in the database. Plain-text tokens are never persisted.

### Authentication

- JWTs use HMAC-SHA256. Symmetric key is environment-specific.
- Refresh token rotation: each use issues a new refresh token and invalidates the previous one.
- Logout invalidates the stored refresh token server-side.

### Known limitations (current scope)

The following are explicitly deferred and represent known gaps for a production deployment:

- No rate limiting on auth endpoints (should be added before public exposure)
- No MFA
- No IP-based allowlisting
- No audit trail UI (backend events are logged via Serilog but not surfaced in the app)
- No automated secret rotation

These are tracked as future work, not unacknowledged risks.
