# 07 — Testing Standards

**Reading time:** ~9 min
**Applies to:** Backend, Frontend, E2E

---

## Why this matters

Tests are not a "nice to have" in financial software. They are the **mechanism that proves correctness**. Without tests:

- A refactor that miscalculates one commission is undetected for months
- Edge cases (year boundaries, leap years, currency conversion) are never exercised
- Regressions slip into production silently
- Code becomes too risky to change → ossification → competitive death

Every line of money math in Wasnie MUST be covered by tests. Every endpoint MUST be tested for happy path, error cases, and authorization.

---

## 7.1 Coverage thresholds

### Rule 7.1.1 — Backend coverage MUST be > 80%

Measured on Application + Domain + Infrastructure (excluding migrations and DI config).

Calculation logic specifically MUST be > 95%.

### Rule 7.1.2 — Frontend coverage MUST be > 60%

Measured on services, helpers, and components with business logic.

Pure presentational components without logic do not require unit tests.

### Rule 7.1.3 — Coverage drop fails CI

Pull requests that reduce coverage by more than 1% MUST be reviewed manually. Reductions > 5% block merge unless explicitly justified.

### Rule 7.1.4 — Coverage % is necessary but not sufficient

90% coverage with weak assertions is worse than 70% with strong assertions. Code review MUST verify tests actually assert meaningful behavior.

---

## 7.2 Test types

### Backend

| Type | Location | Tools | Purpose |
|---|---|---|---|
| **Unit tests** | `WasnieApi.Tests/Unit/` | xUnit, FluentAssertions, NSubstitute | Test individual classes with mocked dependencies |
| **Integration tests** | `WasnieApi.Tests/Integration/` | xUnit, FluentAssertions, Testcontainers (MSSQL), WebApplicationFactory | Test endpoints with real DB |
| **Domain tests** | `WasnieApi.Tests/Domain/` | xUnit, FluentAssertions | Test pure domain logic, no mocks needed |
| **Architecture tests** (Phase C5) | `WasnieApi.Tests/Architecture/` | NetArchTest | Verify layer dependencies and rules |

### Frontend

| Type | Location | Tools | Purpose |
|---|---|---|---|
| **Unit tests** | `*.spec.ts` co-located | Jasmine, Karma | Test helpers, services, components |
| **E2E tests** (Phase 9) | `e2e/` | Playwright | Browser-based user journeys |

---

## 7.3 What MUST be mocked vs. real

### Rule 7.3.1 — Repositories: REAL in integration tests

Integration tests MUST use real database via Testcontainers. Mocking repositories in integration tests is FORBIDDEN — it defeats the purpose.

```csharp
// CORRECT: integration test with Testcontainers
public class PayeesEndpointsTests : IClassFixture<WasnieApiFactory>
{
    private readonly WasnieApiFactory _factory;
    private readonly HttpClient _client;

    public PayeesEndpointsTests(WasnieApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreatePayee_ReturnsCreated()
    {
        // Uses real SQL Server in Docker via Testcontainers
        var response = await _client.PostAsJsonAsync("/api/payees", new { /* ... */ });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

### Rule 7.3.2 — Repositories: MOCKED in unit tests

Unit tests of use cases MAY mock repositories (using NSubstitute) to focus on the use case logic.

### Rule 7.3.3 — External services: ALWAYS mocked

In ALL tests (unit and integration), mock:
- Email senders (SendGrid, etc.)
- Third-party APIs (Groq, Stripe, OAuth providers)
- File storage (S3, Azure Blob)
- SMS providers

Real calls in tests are forbidden — they're slow, flaky, and may cost money.

### Rule 7.3.4 — IClock and similar: ALWAYS injectable for tests

Per Rule 2.5.3, business logic uses `IClock` not `DateTime.UtcNow`. Tests inject a fake clock to control time.

### Rule 7.3.5 — HTTP in frontend: HttpTestingController

```typescript
beforeEach(() => {
  TestBed.configureTestingModule({
    imports: [HttpClientTestingModule],
    providers: [PayeesService]
  });
  service = TestBed.inject(PayeesService);
  httpMock = TestBed.inject(HttpTestingController);
});

afterEach(() => httpMock.verify());

it('fetches payees', () => {
  service.getPayees().subscribe();
  const req = httpMock.expectOne('/api/payees');
  expect(req.request.method).toBe('GET');
  req.flush({ items: [], totalCount: 0 });
});
```

NEVER use real network calls in unit tests.

---

## 7.4 What MUST be tested

### Rule 7.4.1 — Every endpoint MUST have integration tests for:

- Happy path (typical successful request)
- Validation failure (400 returned)
- Authentication required (401 when missing)
- Authorization required (403 when permission denied)
- Tenant isolation (no cross-tenant data access — see file 09)
- Not found (404 when resource doesn't exist)
- Concurrency conflict (when applicable)

### Rule 7.4.2 — Every paginated endpoint MUST have tests for:

- Default pagination returns first page
- Specific page returns correct subset
- Sort by each whitelisted field, both asc and desc
- Search filters correctly
- Each filter parameter filters correctly
- Combination (filter + sort + search + pagination together)
- Invalid sort field falls back to default (no 500)
- Cross-tenant isolation under all of the above

### Rule 7.4.3 — Every calculation MUST have tests for:

- Typical inputs (happy path)
- Boundary values (zero, max, min)
- Edge cases (leap year, month boundary, year boundary, timezone changes)
- Error cases (division by zero, overflow, currency mismatch)
- Idempotency (same input → same output, every time)

### Rule 7.4.4 — Money math MUST be exhaustively tested

(File 02, Rule 1.8 — extracted to pure functions; file 03, Rule 3.1.1 — performance baselines.)

Every commission calculation MUST have tests that:
- Cover every rule type (flat, tiered, accelerated, capped, etc.)
- Cover all rate tier boundaries (just below, at, just above)
- Test combination of multiple rules on a single transaction
- Verify rounding behavior is documented and consistent
- Verify currency consistency (no implicit conversion)

### Rule 7.4.5 — Every domain entity MUST have tests for:

- Each public factory method (Create, etc.)
- Each public state-changing method
- Each business rule (invariant) enforcement
- Each invalid state attempt is rejected with a clear DomainException

---

## 7.5 Test quality rules

### Rule 7.5.1 — One concept per test

Each test verifies one behavior. Multiple `Assert` is fine if they verify the same concept; tests that verify "X and Y and Z" should be split.

### Rule 7.5.2 — Tests MUST be independent

A test MUST NOT depend on another test running first. Tests run in any order, in parallel.

### Rule 7.5.3 — Tests MUST be deterministic

Same input → same result, every run. Flaky tests (sometimes pass, sometimes fail) are FORBIDDEN. They get fixed or removed.

### Rule 7.5.4 — Tests MUST be fast

| Test type | Target |
|---|---|
| Unit test | < 50ms |
| Integration test | < 500ms |
| E2E test | < 5 sec per scenario |
| Full unit test suite | < 30 sec |
| Full integration test suite | < 5 min |

Slow tests get optimized or split.

### Rule 7.5.5 — Test names MUST describe behavior

Format: `Method_Scenario_ExpectedResult` or descriptive sentence.

Good: `CreatePayee_DuplicateEmail_ReturnsConflict`
Good: `ListPayees_FilterByStatusActive_ReturnsOnlyActiveOnes`
Bad: `Test1`, `CreatePayeeWorks`, `BugFix123`

### Rule 7.5.6 — Builders for test data

Test data MUST be created via builder classes for readability:

```csharp
var payee = new PayeeBuilder()
    .WithFullName("Ana María Rodríguez")
    .WithEmployeeCode("SDN-001")
    .WithStatus(PayeeStatus.Active)
    .BuildAndInsert(_dbContext, _tenantId);
```

Avoid inline entity construction with 10+ parameters.

### Rule 7.5.7 — Fixtures are deterministic

Use seeded random (`new Random(42)`) for fake data that needs to be predictable. Real-world data files (the NorthBridge sample) are checked in or generated by a script with a fixed seed.

---

## 7.6 Multi-tenant testing

(See file 09 for full details)

### Rule 7.6.1 — Every cross-cutting test MUST include cross-tenant scenarios

For any list endpoint, there MUST be a test that:
1. Creates data in tenant A
2. Creates data in tenant B
3. Authenticates as tenant A user
4. Queries the endpoint
5. Verifies only tenant A's data is returned

This is non-negotiable for multi-tenant SaaS.

---

## 7.7 Breaking change testing

(See file 08 for full details)

### Rule 7.7.1 — When an endpoint signature changes, ALL consumer tests MUST be updated in the same PR

Phase A lesson: prompt 39 changed pagination response shape; tests failed later because they weren't updated. New rule: acceptance criteria of any prompt that changes a public contract MUST explicitly list "update all tests that consume this contract".

---

## 7.8 What NOT to test

### Rule 7.8.1 — Don't test the framework

Don't write tests that verify "EF Core saves my entity" or "Angular renders my template". Trust the framework.

### Rule 7.8.2 — Don't test trivial getters/setters

Properties without logic don't need tests. Focus tests on behavior.

### Rule 7.8.3 — Don't test private methods directly

Test the public behavior they support. If a private method needs testing in isolation, extract it to a class with public methods (it has SRP violation).

### Rule 7.8.4 — Don't write tests that mirror the implementation

A test that says "method X calls method Y" is brittle. Test what the code DOES (input → output, side effect on DB), not how it does it.

---

## Enforcement

- **CI** runs all tests on every PR. Failures block merge.
- **Coverage report** generated on every PR. Drops > 5% block merge.
- **Architecture tests** (Phase C5) verify layer dependencies via NetArchTest.
- **Quarterly review** of test suite to remove obsolete tests.

---

## Bug history

- **Phase A2 (May 2026):** Backend Import tests achieved > 85% coverage. Confirmed value of SRP for testability.
- **Phase A3 (May 2026):** Frontend tests achieved 95% on tested helpers. Extracting pure functions made testing trivial.
- **Phase A2 (May 2026):** Test failures revealed real bugs in pagination filter implementation. Confirmed value of writing tests to catch real issues, not just satisfy coverage.
- **Lesson:** "no regressions" as an acceptance criterion is meaningless without running ALL tests. CI must run the full test suite for every PR.
