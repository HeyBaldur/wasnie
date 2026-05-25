# WasnieApi — Test Suite

## Overview

| Project | Count | Runner |
|---|---|---|
| `Wasnie.UnitTests` | 85 tests | Pure in-process (no Docker) |
| `Wasnie.IntegrationTests` | 21 tests | Requires Docker |

Run all tests:

```
dotnet test
```

Run only unit tests (no Docker required):

```
dotnet test tests/Wasnie.UnitTests/Wasnie.UnitTests.csproj
```

## Docker prerequisite

Integration tests use **Testcontainers** to spin up a real SQL Server 2022 container. Docker Desktop (or equivalent) must be running before executing the integration test suite.

The container lifecycle is managed by `TestDatabaseFixture`:
- One container starts per test run (`ICollectionFixture`)
- EF Core migrations are applied once on start
- `ResetAsync()` deletes test data (`DELETE FROM CompensationPlans`) between each test

Without Docker, integration tests fail at container startup with a connection error — unit tests are unaffected.

## Why real SQL Server instead of InMemory

EF Core's InMemory provider does not faithfully apply `HasQueryFilter` expressions that close over a scoped service (`ITenantContext`) when the query uses `Include()` + `FirstOrDefaultAsync(predicate)`. The filter is applied correctly on `ToListAsync()` paths but silently skipped on single-entity lookups with eager loading.

On SQL Server, the filter translates to a parameterized `WHERE TenantId = @p0` clause that is always evaluated at the database level. This makes the cross-tenant security tests (`GetPlan_CrossTenant_Returns404`, `ActivatePlan_CrossTenant_Returns422`) meaningful.

## Known production bugs (documented in tests)

| ID | Severity | Description |
|---|---|---|
| BUG-001 | Critical | `Money.Of()` accepts negative amounts — no guard |
| BUG-002 | Critical | `Money.ToString()` uses `F2` half-up rounding, not banker's rounding (spec 5b.5) |
| BUG-003 | High | `Plan.Archive()` allows Draft → Archived transition (spec 5b.1: only Active → Archived) |
| BUG-004 | Medium | `Plan.CloneAsNewVersion()` has no source-status restriction; Draft can be cloned |
