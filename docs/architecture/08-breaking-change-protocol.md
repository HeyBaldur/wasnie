# 08 — Breaking Change Protocol

**Reading time:** ~4 min
**Applies to:** API contracts, DTOs, Service interfaces

---

## Why this matters

In Phase A (May 2026), prompt 39 changed pagination endpoint shape from `List<T>` to `PagedResult<T>` without updating all consumers. Result:
- Integration tests broke (3 of them) → only discovered after the fact
- Frontend filters silently stopped working → only discovered when user complained
- Hours wasted debugging instead of building

This is the **breaking change scar tissue rule**: when a change affects multiple parts of the system, ALL of them must be updated atomically, in the same change set.

---

## 8.1 What constitutes a breaking change

A change is "breaking" if it modifies:

- Endpoint URL or HTTP method
- Endpoint query parameters (added required, removed any, renamed any)
- Endpoint request body shape (renamed, removed, changed type of field)
- Endpoint response body shape (renamed, removed, changed type of field, or wrapped/unwrapped)
- DTO type used by multiple consumers
- Service interface signature in shared layers
- Domain entity structure (added required field, changed field type)
- Database schema (renamed column, changed type, dropped column, dropped table)

If your change matches ANY of the above, follow this protocol.

---

## 8.2 The protocol

### Rule 8.2.1 — Enumerate all consumers BEFORE coding

Before making the change, identify EVERY place that uses the contract being changed:

- All controllers that expose the endpoint
- All frontend services that call the endpoint
- All tests that verify the endpoint behavior
- All API client SDKs (Phase 6+)
- All integration partners (Phase 6+)
- All documentation (OpenAPI, README, internal docs)

The prompt that initiates the change MUST list these consumers in its acceptance criteria.

### Rule 8.2.2 — Update all consumers in the same change set

A pull request that changes a public contract MUST also update:
- All consumers (per Rule 8.2.1)
- All tests for those consumers
- Documentation
- Type definitions (TypeScript interfaces, etc.)

A PR that changes the contract but leaves consumers broken is FORBIDDEN.

### Rule 8.2.3 — CI MUST run ALL tests, not just affected

If only the changed module's tests run, regressions in consumer tests are missed. CI runs the **entire test suite** for every PR.

### Rule 8.2.4 — Tests MUST be visibly updated

In the PR description, list the test files updated for the breaking change:

```markdown
## Breaking changes

This PR changes `/api/payees` response from `List<Payee>` to `PagedResult<Payee>`.

### Consumers updated:
- [x] `WasnieApi/Controllers/PayeesController.cs`
- [x] `WasnieApi.Tests/Integration/Payees/PayeesEndpointsTests.cs`
- [x] `WasnieUi/src/app/features/payees/services/payees.service.ts`
- [x] `WasnieUi/src/app/features/payees/payees-list.component.ts`
- [x] OpenAPI schema in `WasnieApi/openapi.json`

### Consumers NOT updated (and why):
- [ ] External API clients (none exist yet — Phase 6)
```

---

## 8.3 Versioning (Phase 6+)

When Wasnie exposes a public API to customers/partners, breaking changes become more serious. At that point:

### Rule 8.3.1 — Public API uses versioned URLs

`/api/v1/payees`, `/api/v2/payees`. Old versions remain functional for a documented deprecation period (minimum 6 months).

### Rule 8.3.2 — Deprecation notice in response headers

Old endpoints return:
```
Deprecation: true
Sunset: Wed, 21 Oct 2026 23:59:59 GMT
Link: </api/v2/payees>; rel="successor-version"
```

Customers get explicit warnings, not silent breakage.

### Rule 8.3.3 — Major version bump for breaking changes

`/api/v1/` → `/api/v2/`. Old version supported until sunset date.

---

## 8.4 Database schema changes

### Rule 8.4.1 — Migrations MUST be backwards-compatible for one deploy

When deploying, the old code may still be running for a few minutes during rollout. The DB schema after migration MUST work for both old and new code.

Strategies:
- **Add column:** safe, old code ignores it.
- **Rename column:** NOT safe in one step. Do it in two:
  1. Deploy: add new column, keep old column, write to both
  2. Deploy: read from new column only, ignore old
  3. Deploy: drop old column
- **Drop column:** NOT safe in one step. Same as above.

### Rule 8.4.2 — Destructive migrations require explicit approval

Migrations that DROP columns, tables, or data require owner approval. Code review MUST flag them.

### Rule 8.4.3 — Data migrations MUST be reversible OR fully tested in staging

Any data migration (UPDATE/DELETE during deploy) MUST either:
- Have a documented rollback SQL script
- OR be exercised in a staging environment with production-like data first

---

## 8.5 Prompt protocol for breaking changes

When generating a prompt to Claude Code that introduces a breaking change:

### Rule 8.5.1 — Prompt MUST list affected consumers

The prompt header MUST explicitly list:

```markdown
## Breaking change — affected consumers

This change affects:
1. Backend: PayeesController, PayeesEndpointsTests, OpenAPI schema
2. Frontend: PayeesService, payees-list.component, payee-import.service
3. Database: no schema change

ALL of these MUST be updated in this single change. Failure to update any of them is a violation of ARCHITECTURE.md §8.
```

### Rule 8.5.2 — Acceptance criteria MUST verify ALL consumers work

"All existing tests pass" is the minimum. Specific tests:

```markdown
- [ ] PayeesEndpointsTests — all tests pass
- [ ] payees.service.spec.ts — all tests pass
- [ ] Manual: navigate to /payees, verify list renders
- [ ] Manual: verify filtering, sorting, pagination all work
```

### Rule 8.5.3 — Prompt MUST require running the FULL test suite

"Run `dotnet test` (entire solution)" and "Run `ng test --no-watch` (all tests)" before reporting completion.

---

## Enforcement

- **CI runs full test suite** on every PR (Phase C5)
- **PR template** includes breaking change checklist
- **Code review** verifies all consumers updated
- **Architecture tests** (NetArchTest) catch unauthorized contract changes (Phase C5)

---

## Bug history

- **Phase A — prompt 39:** Changed `List<T>` to `PagedResult<T>` in pagination endpoints. Tests not updated → 3 integration tests failed later. Frontend not updated → all filters stopped working silently.
- **Phase A — prompt 42:** Standardized filter query format from `filter[X]=Y` to `X=Y`. Backend changed but frontend still sent old format → silent breakage of all filtered list views.
- **Phase A — prompt 43:** Cleanup PR that finally aligned frontend and backend.

**Lesson:** these three prompts could have been one if Rule 8.2.2 had been in place from the start. The protocol now exists to prevent this.
