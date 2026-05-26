# 06 — Authorization Model

**Reading time:** ~8 min
**Applies to:** Backend, Frontend

---

## Why this matters

Without authorization, every authenticated user can do everything. That means:

- A junior sales rep can see executive compensation
- A rep can delete another rep's quota
- A user on Starter tier can create 1,000 payees, bypassing the price model
- A manager can modify the compensation plan they're supposed to be paid by

Authorization is the second pillar of security (auth is the first). Wasnie MUST enforce it at the API level, never trusting the frontend.

---

## 6.1 Subscription tiers

### Tier definitions

| Tier | Price (EUR/mo) | Payees | Plans | Integrations | API access |
|---|---|---|---|---|---|
| **Free** | 0 | 5 | 1 (demo) | None | No |
| **Starter** | 300 | 25 | 5 | CSV/Excel import only | No |
| **Growth** | 800 | 75 | 15 | HubSpot, CSV | Limited (read) |
| **Scale** | 1,800 | 150 | Unlimited | All native integrations | Full |
| **Enterprise** | 2,500+ | Unlimited | Unlimited | All + custom | Full + SLA |

(Detailed feature matrix in Product Master Specification.)

### Rule 6.1.1 — Tiers MUST be enforced at API level

Frontend hides features unavailable to the tier (graceful UX). But the **API enforces the limit**.

Example: Starter tier allows 25 payees. Adding the 26th MUST return:

```http
HTTP/1.1 403 Forbidden
Content-Type: application/json

{
  "error": "TierLimitExceeded",
  "message": "Your Starter plan is limited to 25 payees. Upgrade to Growth for up to 75.",
  "tier": "Starter",
  "currentCount": 25,
  "limit": 25,
  "upgradePath": "/account/subscription"
}
```

### Rule 6.1.2 — Tier limits checked BEFORE the action

Limit checks happen as part of the use case, BEFORE any write to the DB. NEVER write then rollback.

### Rule 6.1.3 — Tier features are explicit, not implicit

Each tier has a `TierFeatures` table or enum that lists exactly which features are enabled:

```csharp
public sealed class TierFeatures
{
    public TierName Tier { get; }
    public int MaxPayees { get; }
    public int MaxPlans { get; }
    public bool ApiAccess { get; }
    public bool HubspotIntegration { get; }
    public bool SalesforceIntegration { get; }
    public bool TwoFactorRequired { get; }
    public bool SsoEnabled { get; }
    public bool CustomReports { get; }
    // ...
}
```

This list is the contract. Adding a new feature → adding to this list + decision for each tier.

---

## 6.2 Roles (within a tenant)

### Role definitions

| Role | Purpose | Scope |
|---|---|---|
| **TenantAdmin** | Full access to tenant. Manages subscription, users, integrations. | Tenant-wide |
| **CompManager** | Designs plans, sets quotas, assigns payees. Approves payouts. | Tenant-wide |
| **Manager** | Views their team's performance. Cannot modify plans. Reviews disputes from team. | Limited to direct/indirect reports |
| **Rep** | Views their own earnings, quotas, and history. Raises disputes. | Self only |

### Rule 6.2.1 — Default roles

When a tenant is created, the user creating it gets `TenantAdmin`. They can invite others with specific roles.

### Rule 6.2.2 — Custom roles in Phase 8+

Phase 1-7 use the 4 fixed roles. Custom roles (Enterprise tier feature) come later.

---

## 6.3 Permissions

### Rule 6.3.1 — Permissions are atomic, named verbs

Format: `Resource.Action`

Examples:
- `Payees.Read`
- `Payees.Create`
- `Payees.Update`
- `Payees.Terminate`
- `Plans.Read`
- `Plans.Create`
- `Plans.Activate`
- `Quotas.Set`
- `Reports.ViewTeam`
- `Reports.ViewAll`
- `Subscription.Manage`

### Rule 6.3.2 — Roles MUST have explicit permission grants

```
TenantAdmin: ALL permissions
CompManager: Payees.*, Plans.*, Quotas.*, Reports.ViewAll, Imports.Execute
Manager: Payees.ReadTeam, Quotas.ReadTeam, Reports.ViewTeam, Disputes.Review
Rep: Payees.ReadSelf, Earnings.ReadSelf, Disputes.Raise
```

(Full matrix maintained in code, single source of truth.)

### Rule 6.3.3 — Permission checks happen at the use case level

```csharp
public sealed class TerminatePayeeUseCase
{
    public async Task ExecuteAsync(
        Guid payeeId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        // Authorization FIRST
        _authorization.Require(user, "Payees.Terminate");

        // Then business logic
        var payee = await _repository.GetByIdAsync(payeeId);
        payee.Terminate(_clock.Today);
        await _repository.UpdateAsync(payee);
    }
}
```

### Rule 6.3.4 — Scoped permissions for Manager role

`Payees.ReadTeam` means "read payees that report to me, directly or indirectly". The authorization service MUST resolve the org hierarchy to enforce this.

```csharp
public interface IAuthorizationService
{
    void Require(ClaimsPrincipal user, string permission);
    Task<bool> CanAccessPayeeAsync(ClaimsPrincipal user, Guid payeeId);
    Task<IReadOnlyList<Guid>> GetAccessiblePayeeIdsAsync(ClaimsPrincipal user);
}
```

---

## 6.4 Implementation

### Rule 6.4.1 — Authorization is in Application layer

`IAuthorizationService` defined in Application. Implementation in Infrastructure (because it queries roles/permissions from DB).

### Rule 6.4.2 — Controllers MUST NOT have authorization logic

Use case methods enforce authorization. Controllers only:
- Pass `ClaimsPrincipal` to the use case
- Translate `UnauthorizedException` to HTTP 403

### Rule 6.4.3 — `[Authorize]` attribute is for authentication, not authorization

ASP.NET `[Authorize]` confirms the user is authenticated. Fine-grained permission check is done in the use case via `IAuthorizationService`.

This separation allows business logic to evolve without controller changes.

### Rule 6.4.4 — Authorization decisions MUST be logged when denied

(See file 05 — Audit trail, Rule 5.1.4)

---

## 6.5 Frontend authorization

### Rule 6.5.1 — UI MUST gracefully hide unavailable features

The user's permissions are in the JWT or fetched from `/auth/me`. The UI uses this to:
- Hide buttons / menu items the user can't use
- Show upgrade prompts for tier-restricted features

### Rule 6.5.2 — Frontend authorization is convenience, NOT security

Hiding a button does not prevent a clever user from calling the API directly. The backend MUST enforce regardless.

### Rule 6.5.3 — Upgrade flow for tier limits

When the user tries to use a tier-restricted feature, show:
- What the limit is
- Which tier removes the limit
- One-click upgrade path (Phase 4+ with Stripe)

---

## 6.6 Multi-tenant authorization

(See file 09 — Multi-tenant isolation for full details)

### Rule 6.6.1 — Cross-tenant access is FORBIDDEN

Even a TenantAdmin cannot access data outside their tenant. There is NO super-admin role that bypasses tenant boundaries in the application.

Wasnie staff support: separate admin tool, separate auth flow, separate logging, separate explicit grant. Not part of the application.

---

## 6.7 Auth-related endpoints

### Public (no auth required)

- `POST /auth/login`
- `POST /auth/register-tenant`
- `POST /auth/refresh`
- `POST /auth/forgot-password`
- `POST /auth/reset-password`
- `POST /auth/verify-email`

### Authenticated, no specific permission

- `GET /auth/me` — get current user info
- `POST /auth/logout`
- `POST /auth/change-password`
- `PUT /auth/profile` — update own profile

### TenantAdmin only

- `GET /tenant/users`
- `POST /tenant/users/invite`
- `DELETE /tenant/users/{id}`
- `PUT /tenant/users/{id}/role`
- `GET /tenant/subscription`
- `PUT /tenant/subscription` (upgrade/downgrade)
- `GET /tenant/audit`

---

## Enforcement

- **`IAuthorizationService`** is the single source of truth
- **Tests** MUST verify authorization for every protected endpoint (file 07)
- **Multi-tenant tests** MUST verify cross-tenant access is denied (file 09)
- **Phase B2 audit** identifies endpoints without proper authorization

---

## Bug history

- **Phase 1:** authorization not yet implemented. JWT auth exists, but tier limits and role checks do not. This is a known gap to be addressed in Phase C2.
- **Phase C2 will:**
  - Introduce `IAuthorizationService`
  - Implement tier limit enforcement
  - Implement role-based permission checks
  - Add UI graceful degradation
  - Migrate existing tenants to Growth tier (free during beta) for continuity
