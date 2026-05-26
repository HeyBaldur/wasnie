# 05 — Audit Trail

**Reading time:** ~6 min
**Applies to:** Backend, Database, Compliance

---

## Why this matters

Wasnie is financial software. When a customer's payee disputes a commission calculation 6 months from now:

- "Why was my October commission $1,200 instead of $1,500?"
- "Who changed my quota on November 5?"
- "Why is my plan assigned to me instead of someone else?"

We MUST be able to answer with **immutable, timestamped, attributable records**. Without these, we lose customer trust permanently and may face legal liability.

Audit trail is not optional. It is the **legal record** of every meaningful action in the system.

---

## 5.1 What MUST be audited

### Rule 5.1.1 — Every destructive operation

- DELETE of any entity
- Status changes (Payee: Active → Terminated, Plan: Draft → Active, etc.)
- Soft-delete operations
- Archive operations

### Rule 5.1.2 — Every monetary operation

- Commission calculations (every individual line)
- Quota assignments
- Quota updates (any field changed)
- Plan assignments
- Plan rule changes
- Payout approvals
- Disputes raised, accepted, rejected

### Rule 5.1.3 — Every authentication event

- Login (success and failure with reason)
- Logout
- Password change
- Token refresh
- Account lockout
- Role/permission changes
- 2FA enabled/disabled

### Rule 5.1.4 — Every authorization decision (when denied)

- Permission check failed
- Tier limit exceeded
- Subscription expired

(Successful permission checks NOT logged — would be too noisy.)

### Rule 5.1.5 — Every administrative action

- Tenant created, suspended, deleted
- Subscription tier changed
- User invited, role changed, removed
- Configuration changed

---

## 5.2 What an audit record MUST contain

### Rule 5.2.1 — Minimum fields

```csharp
public sealed class AuditLog
{
    public long Id { get; }                       // BIGINT IDENTITY for ordering
    public Guid TenantId { get; }                 // tenant scope
    public DateTime TimestampUtc { get; }         // when (UTC, never local)
    public string ActorUserId { get; }            // who (or "SYSTEM" for automated)
    public string ActorEmail { get; }             // human-readable identity
    public string Action { get; }                 // what (PAYEE_CREATED, PLAN_RULE_UPDATED, etc.)
    public string ResourceType { get; }           // entity type (Payee, Plan, etc.)
    public string ResourceId { get; }             // entity ID (string for flexibility)
    public string? ResourceDisplayName { get; }   // human-friendly identifier
    public string? BeforeJson { get; }            // entity state BEFORE change (null for create)
    public string? AfterJson { get; }             // entity state AFTER change (null for delete)
    public string? CorrelationId { get; }         // request ID for tracing
    public string? IpAddress { get; }             // origin IP (when applicable)
    public string? UserAgent { get; }             // browser/client (when applicable)
    public string? Metadata { get; }              // extra context (JSON, optional)
}
```

### Rule 5.2.2 — Timestamps in UTC

NEVER use local time. NEVER use server timezone. Always UTC. Display formatting (timezone conversion) is a UI concern.

### Rule 5.2.3 — Before/After as JSON snapshots

For updates, store full entity snapshot before and after the change. This allows:
- Diff inspection for disputes
- Replay of changes
- Compliance reporting

For very large entities, snapshot only relevant fields (document which).

---

## 5.3 What audit records MUST NEVER do

### Rule 5.3.1 — NEVER modified

Audit records are write-only once created. No UPDATE statement on the audit table is permitted.

Database-level enforcement:
- INSERT trigger that prevents UPDATE/DELETE on the audit table
- Or: row-level permissions denying UPDATE/DELETE to the app user

### Rule 5.3.2 — NEVER deleted

Even after data retention period passes, audit records are archived, not deleted. Hard-delete is FORBIDDEN.

Retention: minimum 7 years (financial compliance).

### Rule 5.3.3 — NEVER block the user operation

Audit log writes MUST NOT block the user's operation. Strategies:
- Write asynchronously (background queue) — preferred
- Write synchronously only if blocking is acceptable (very fast operations)
- If write fails, log the error but DO NOT fail the user operation (caller already got their result)

**Tradeoff:** if the audit write fails, we have a missing record. This is acceptable for some operations (logins) but NOT for money operations. Money operations MUST use a transactional outbox pattern (Phase 2+).

### Rule 5.3.4 — NEVER store secrets

Audit records MUST NOT contain:
- Passwords (hashed or plain)
- Tokens
- API keys
- Full credit card numbers
- Anything that, if leaked, would be a security incident

---

## 5.4 How audit records are accessed

### Rule 5.4.1 — Read-only view per tenant

Authenticated admins MUST be able to:
- View their tenant's audit log
- Filter by date range, user, action type, resource
- Export as CSV (Enterprise tier only)

NEVER cross-tenant access (even for Wasnie staff). Support requests requiring this need a separate, logged, admin-tool flow.

### Rule 5.4.2 — Pagination required

Audit logs grow large. List endpoint MUST follow standard pagination (file 03, Rule 3.2.1).

### Rule 5.4.3 — Per-resource history

For any resource (Payee, Plan, etc.), there MUST be an endpoint to fetch its full audit history.

```
GET /api/payees/{id}/audit?page=1&pageSize=50
GET /api/plans/{id}/audit?page=1&pageSize=50
```

---

## 5.5 Implementation requirements

### Rule 5.5.1 — Audit service is a Domain concept

`IAuditService` interface MUST live in Application (used by use cases). Implementation in Infrastructure.

```csharp
// Application/Services/IAuditService.cs
public interface IAuditService
{
    Task LogAsync(
        string action,
        string resourceType,
        string resourceId,
        object? before = null,
        object? after = null,
        string? displayName = null,
        Dictionary<string, string>? metadata = null);
}
```

### Rule 5.5.2 — Audit is implicit, not explicit per call

Use cases SHOULD NOT have to call `_auditService.LogAsync(...)` in every method. Instead, decorate at the appropriate level:

**Option A — MediatR pipeline behavior (recommended):**

```csharp
public class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IAuditableCommand
{
    // Wraps every IAuditableCommand with audit logging.
}
```

Commands that need audit implement `IAuditableCommand`. The pipeline does the rest.

**Option B — Repository decorator:**

A `AuditingPayeeRepository` decorator wraps `IPayeeWriteRepository` and logs before/after on every Save.

### Rule 5.5.3 — Background queue for non-blocking writes

Audit writes go to an in-memory queue, consumed by a hosted service that batches writes to the DB. Failure of the queue logs errors but doesn't block users.

For money operations, use transactional outbox: audit record written in same DB transaction as the business change.

---

## 5.6 Specific audit requirements per resource

### Payees

- PAYEE_CREATED, PAYEE_UPDATED, PAYEE_TERMINATED, PAYEE_REACTIVATED, PAYEE_DELETED
- Manager changes especially important (org structure history)

### Plans

- PLAN_CREATED, PLAN_VERSION_CREATED, PLAN_ACTIVATED, PLAN_ARCHIVED
- PLAN_RULE_ADDED, PLAN_RULE_UPDATED, PLAN_RULE_REMOVED

### Quotas

- QUOTA_CREATED, QUOTA_UPDATED, QUOTA_DELETED
- Updates to amount or period are especially audit-critical

### Plan Assignments

- ASSIGNMENT_CREATED, ASSIGNMENT_UPDATED, ASSIGNMENT_REMOVED

### Imports

- IMPORT_STARTED, IMPORT_COMPLETED, IMPORT_FAILED
- Already implemented in Phase 1 as `ImportAudit` (precursor to general audit log)

### Transactions (Phase 2)

- TRANSACTION_IMPORTED, TRANSACTION_RECALCULATED, TRANSACTION_ADJUSTED
- COMMISSION_CALCULATED (every calculation, with full input + output)

### Payouts (Phase 2)

- PAYOUT_GENERATED, PAYOUT_APPROVED, PAYOUT_REJECTED, PAYOUT_PAID

### Disputes (Phase 2)

- DISPUTE_RAISED, DISPUTE_REVIEWED, DISPUTE_ACCEPTED, DISPUTE_REJECTED

---

## 5.7 Audit log database schema

### Rule 5.7.1 — Separate database OR separate schema

Audit log MUST live in:
- Separate database (preferred for production)
- OR separate schema in the same DB with strict permissions

This prevents accidental joins, ensures separate backup policies, and supports different retention.

### Rule 5.7.2 — Indexed for typical queries

Required indexes:
- `(TenantId, TimestampUtc DESC)` — tenant's recent activity
- `(TenantId, ResourceType, ResourceId, TimestampUtc DESC)` — resource history
- `(TenantId, ActorUserId, TimestampUtc DESC)` — user's actions

### Rule 5.7.3 — Partitioned by month at scale

When the audit log exceeds 10 million rows, partition by month for query performance and archival.

---

## Enforcement

- **DB triggers** prevent UPDATE/DELETE on audit table
- **CI tests** verify audit records are created for every CUD operation on critical entities (Phase C5)
- **Code review** checks that new use cases include audit logging (or implement `IAuditableCommand`)
- **Production monitoring** alerts on audit write failures (Phase C6)

---

## Bug history

- **Phase 1:** `ImportAudit` entity created as precursor to general audit log. Pattern to generalize in Phase C3.
- **Phase B2 audit (pending):** Identify which existing operations are NOT audited but should be.
