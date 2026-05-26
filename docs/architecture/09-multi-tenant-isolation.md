# 09 — Multi-Tenant Isolation

**Reading time:** ~6 min
**Applies to:** Backend, Database

---

## Why this matters

A single tenant boundary violation in Wasnie means **a customer can see another customer's commission data**. That is:

- Company A sees Company B's sales team salaries
- Company A sees Company B's compensation plan (competitive intelligence theft)
- Company A sees Company B's transaction volume (revenue intelligence)
- Wasnie loses both customers immediately + faces lawsuits

This is rule #1 of multi-tenant SaaS. Other rules are negotiable. This one is not.

---

## 9.1 The fundamental rule

### Rule 9.1.1 — Every query touching tenant data MUST filter by TenantId

ZERO exceptions. Every `SELECT`, `INSERT`, `UPDATE`, `DELETE` on any tenant-scoped table MUST include `WHERE TenantId = @currentTenantId`.

### Rule 9.1.2 — TenantId MUST come from auth context, NEVER from request

```csharp
// CORRECT
var tenantId = _currentUserService.TenantId;  // from JWT claims

// FORBIDDEN
var tenantId = request.TenantId;  // user-supplied → security hole
```

If the frontend sends `tenantId` in a query and the backend uses it, a malicious user can change one character and read another tenant's data.

### Rule 9.1.3 — Tenant context MUST be set BEFORE any data access

The middleware that authenticates users MUST set `ICurrentUserService.TenantId` from JWT claims, BEFORE any controller / use case runs.

If `TenantId` is null when data is accessed, the request MUST fail with 401, not 200 with empty data.

---

## 9.2 Implementation patterns

### Rule 9.2.1 — Global query filter in EF Core (defense in depth)

DbContext MUST configure a global query filter on every tenant-scoped entity:

```csharp
public class ApplicationDbContext : DbContext
{
    private readonly ICurrentUserService _currentUser;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payee>()
            .HasQueryFilter(p => p.TenantId == _currentUser.TenantId);

        modelBuilder.Entity<CompensationPlan>()
            .HasQueryFilter(p => p.TenantId == _currentUser.TenantId);

        modelBuilder.Entity<Quota>()
            .HasQueryFilter(q => q.TenantId == _currentUser.TenantId);

        // ... every tenant-scoped entity
    }
}
```

This ensures even forgotten WHERE clauses don't leak data.

### Rule 9.2.2 — Explicit WHERE still required (belt and suspenders)

Even with global query filter, application code MUST still write explicit `Where(p => p.TenantId == ...)` in queries. This is defense in depth: if the global filter is ever disabled (e.g., via `IgnoreQueryFilters()` for admin tools), the explicit clause still protects.

### Rule 9.2.3 — Writes MUST set TenantId from context, NOT from request

```csharp
// CORRECT
public async Task<Payee> CreatePayeeAsync(CreatePayeeRequest request)
{
    var payee = Payee.Create(
        tenantId: _currentUser.TenantId,  // from auth
        fullName: request.FullName,
        // ...
    );
    await _repository.AddAsync(payee);
    return payee;
}

// FORBIDDEN
public async Task<Payee> CreatePayeeAsync(CreatePayeeRequest request)
{
    var payee = Payee.Create(
        tenantId: request.TenantId,  // ← user can set this to anything
        // ...
    );
}
```

### Rule 9.2.4 — Cross-tenant references MUST be impossible

If `Payee.ManagerId` references another Payee, the application code MUST verify the referenced manager is in the same tenant. The Domain entity creation method MUST enforce this:

```csharp
public static Payee Create(
    Guid tenantId,
    /* ... */,
    Guid? managerId = null,
    IPayeeReadRepository repository = null)
{
    if (managerId.HasValue && repository != null)
    {
        var manager = repository.GetByIdAsync(managerId.Value).Result;
        if (manager == null || manager.TenantId != tenantId)
            throw new DomainException("Manager must be in the same tenant.");
    }
    // ...
}
```

OR (preferred): the use case validates manager belongs to tenant before passing to the domain.

### Rule 9.2.5 — Cache keys MUST include TenantId

Any cache (Redis, in-memory, file-based) used for tenant-scoped data MUST scope keys by tenant:

```csharp
// CORRECT
var cacheKey = $"payees:{tenantId}:list:page:{page}";

// FORBIDDEN
var cacheKey = $"payees:list:page:{page}";  // ← cache leak across tenants
```

### Rule 9.2.6 — Temporary storage (imports, etc.) MUST be tenant-scoped

The Import Wizard temporarily stores a parsed file before validation. The `fileId` is opaque. The lookup MUST verify the requesting user is in the tenant that uploaded the file:

```csharp
// CORRECT
var parsedFile = await _cache.GetAsync<ParsedFile>($"import:{tenantId}:{fileId}");

// FORBIDDEN
var parsedFile = await _cache.GetAsync<ParsedFile>($"import:{fileId}");
// ← tenant A could use a fileId from tenant B
```

---

## 9.3 Testing rules

### Rule 9.3.1 — Every endpoint MUST have a cross-tenant test

For each endpoint, there MUST be a test that:
1. Creates data in tenant A (with valid auth as tenant A user)
2. Creates data in tenant B (with valid auth as tenant B user)
3. Authenticates as tenant A
4. Calls the endpoint
5. Verifies ONLY tenant A's data is returned (or, for write endpoints, only tenant A's data is affected)

### Rule 9.3.2 — Test the failure mode explicitly

Tenant B should NEVER see tenant A's data, even when:
- Tenant B tries to access a specific resource by ID that belongs to A
- Tenant B tries to update a resource that belongs to A
- Tenant B tries to delete a resource that belongs to A

In all cases, the response is 404 (not 403) — to avoid leaking information about whether the resource exists.

### Rule 9.3.3 — Cross-tenant by ID return 404, NOT 403

```csharp
public async Task<ActionResult<Payee>> GetPayee(Guid id)
{
    var payee = await _repository.GetByIdAsync(id);
    if (payee == null)
        return NotFound();  // ← does not exist OR not in our tenant
    return Ok(payee);
}
```

(Global query filter ensures `GetByIdAsync` returns null for other tenants' data.)

Using 403 would leak the existence of the resource. Use 404 instead.

### Rule 9.3.4 — Cache leak tests

For endpoints with caching, tests MUST verify that cached data does not leak across tenants. After populating tenant A's cache, tenant B's queries MUST hit the database or a separate cache key.

---

## 9.4 Admin tools and cross-tenant operations

### Rule 9.4.1 — There is NO super-admin role in the application

Wasnie staff who need to support a customer do NOT have a super-admin login that bypasses tenant boundaries. Cross-tenant access is FORBIDDEN at the application level.

### Rule 9.4.2 — Support operations use a separate admin tool

A separate, internal-only tool (Phase 8+) provides:
- Logged-in via Wasnie staff identity (separate from customer auth)
- Every action logged with explicit "support session" context
- Approval workflow for accessing customer data (customer notified)
- Read-only by default; writes require additional approval

This admin tool runs on a separate origin, with separate auth, separate authorization, and is NOT part of the main application.

### Rule 9.4.3 — Background jobs MUST set tenant context

Hosted services that process data per tenant MUST set `ICurrentUserService.TenantId` for each tenant they process. NEVER run a background job without a tenant context, even for "system" operations.

---

## 9.5 Data export

(See file 04 — Security, Rule 4.11.4 for GDPR)

### Rule 9.5.1 — Export endpoints MUST be tenant-scoped

A user requesting data export MUST only receive their own tenant's data.

### Rule 9.5.2 — Export files MUST be securely stored

Generated export files are tenant-scoped storage with auth-gated download URLs (signed, time-limited).

---

## Enforcement

- **Global query filter** in EF Core (defense in depth) — Phase C audit verifies all entities have it
- **Architecture tests** (Phase C5) verify entities have TenantId and query filter
- **Cross-tenant tests** required for every endpoint (Rule 9.3.1)
- **Code review** specifically checks tenant filter on new queries
- **Phase B2 audit** verifies all existing endpoints respect tenant isolation

---

## Bug history

- **None confirmed in Phase A.** Multi-tenant code present from day one. Tests verifying this need expansion.
- **Phase B2 audit (pending):** Will verify systematic coverage of cross-tenant test scenarios across all endpoints.
- **Phase A2 lesson:** ImportAudit table needs explicit cross-tenant tests (added during Phase A2 tests).
