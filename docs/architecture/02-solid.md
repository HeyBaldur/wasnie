# 02 — SOLID Principles

**Reading time:** ~12 min
**Applies to:** Backend and Frontend

---

## Why this matters

SOLID is not academic theory. Each principle prevents a specific class of failure that, in financial software, becomes catastrophic:

- Without **S**, a single class change ripples through commissions, payouts, audits simultaneously
- Without **O**, every new commission rule type requires modifying existing logic with risk of breaking working rules
- Without **L**, a `PercentageRule` pretending to be a `Rule` silently breaks the calculation engine
- Without **I**, tests must mock 20-method interfaces to verify one behavior
- Without **D**, the calculation engine cannot be tested without a database

For Wasnie, SOLID is non-negotiable.

---

## 2.1 Single Responsibility (SRP)

A class MUST have one, and only one, reason to change.

**Heuristic test:** Describe what the class does in one sentence WITHOUT using "and". If you need "and", split it.

### Rule 2.1.1 — Service classes MUST be focused

Backend services:
- One concern
- Max 300 lines
- Max 10 public methods
- Name describes the single responsibility

### Rule 2.1.2 — Controllers MUST be thin

Controllers in `Presentation`:
- Only: model binding, auth context, calling Application, returning HTTP response
- MUST NOT contain business logic
- MUST NOT query DbContext (even read-only)
- Max 200 lines per controller, max 20 lines per action method

### Rule 2.1.3 — Frontend components MUST be focused

- Max 300 lines unless justified in header comment
- MUST NOT make HTTP calls (Rule 1.6)
- MUST NOT contain calculations (Rule 1.8)
- MUST delegate state when crossing 5 signals

### Correct pattern

```csharp
// Single responsibility: parsing files. NOT validation. NOT persistence.
public sealed class FileParserService : IFileParserService
{
    public async Task<ParsedFile> ParseAsync(Stream stream, string fileName)
    {
        var format = DetectFormat(fileName);
        return format switch
        {
            FileFormat.Csv => await ParseCsvAsync(stream),
            FileFormat.Xlsx => await ParseXlsxAsync(stream),
            _ => throw new UnsupportedFormatException(fileName)
        };
    }
}
```

### NEVER

```csharp
public class PayeeImportManager
{
    public async Task ImportAsync(Stream file)
    {
        var rows = ParseFile(file);           // ← format changes
        ValidateRows(rows);                    // ← rules change
        var payees = MapToEntities(rows);      // ← domain changes
        await SaveToDatabaseAsync(payees);     // ← persistence changes
        await SendNotificationEmailsAsync();   // ← notifications change
    }
}
```

**Bug history (Phase A2):** Import was correctly split into 3 services from day one. Coverage > 85% achieved easily because each service had small surface area.

---

## 2.2 Open/Closed (OCP)

Software MUST be open for extension, closed for modification.

**Critical for Phase 2 (Calculation Engine):** new commission rule types MUST be added WITHOUT modifying:
- The calculation engine
- Existing rule types
- Existing tests for other rules
- Database schema (unless new column needed)

**Why non-negotiable:** customers have working plans in production. New rule for future customer MUST NOT risk existing customers.

### Rule 2.2.1 — Extensible behavior MUST use interfaces (Strategy pattern)

### Rule 2.2.2 — `switch(type)` is FORBIDDEN for business logic

If you write `switch (rule.Type)` or `if (rule is TieredRule)` in business logic, you violate OCP. Use polymorphism.

**Narrow exception:** infrastructure dispatching (file format detection) MAY use switch because the set is closed.

### Correct

```csharp
public interface ICommissionRule
{
    string RuleType { get; }
    bool AppliesTo(Transaction transaction, Payee payee);
    Money Calculate(Transaction transaction, CommissionContext context);
}

public sealed class FlatRateRule : ICommissionRule { /* ... */ }
public sealed class TieredRule : ICommissionRule { /* ... */ }

public sealed class CommissionCalculator
{
    public IReadOnlyList<CommissionResult> Calculate(
        Transaction tx,
        Payee payee,
        IReadOnlyList<ICommissionRule> rules)
    {
        // Engine treats all rules uniformly. New rule = new class. Zero engine changes.
        return rules
            .Where(r => r.AppliesTo(tx, payee))
            .Select(r => new CommissionResult(
                r.RuleType,
                r.Calculate(tx, BuildContext(tx, payee))))
            .ToList();
    }
}
```

### NEVER

```csharp
public class CommissionCalculator
{
    public Money Calculate(Transaction tx, Rule rule)
    {
        switch (rule.Type)  // ← OCP violation
        {
            case "FlatRate": return tx.Amount * rule.FlatRate;
            case "Tiered": return CalculateTiered(tx, rule);
            // Adding "SPIFF" requires modifying this method.
            // Every customer's working calculations risk regression.
            default: throw new NotSupportedException();
        }
    }
}
```

---

## 2.3 Liskov Substitution (LSP)

Subtypes MUST be substitutable for base types without altering correctness.

If `FlatRateRule`, `TieredRule`, `AcceleratorRule` all implement `ICommissionRule`, `CommissionCalculator` MUST use any interchangeably. If `TieredRule` throws `NotSupportedException` for some method, it is NOT a valid `ICommissionRule`.

### Rule 2.3.1 — Implementations MUST honor the contract

- Accept all valid inputs the interface allows
- Return all valid outputs the contract specifies
- NOT throw for inputs declared as valid
- NOT have stricter preconditions or weaker postconditions than the base

### Rule 2.3.2 — If implementing forces `NotImplementedException`, the interface is wrong

Split the interface (see ISP).

### NEVER

```csharp
public sealed class StaticBonusRule : ICommissionRule
{
    public bool AppliesTo(Transaction tx, Payee p) => true;

    public Money Calculate(Transaction tx, CommissionContext ctx)
    {
        // ← LSP violation: ignores input the contract requires
        throw new NotSupportedException(
            "Static bonus doesn't use transactions. Call CalculateStatic() instead.");
    }

    public Money CalculateStatic() => /* ... */;
}
```

If `StaticBonusRule` doesn't fit `ICommissionRule`, it MUST NOT implement it.

---

## 2.4 Interface Segregation (ISP)

Clients MUST NOT depend on interfaces they do not use.

### Rule 2.4.1 — Interfaces MUST be focused

- Single, clear purpose
- Cohesive methods used together
- Max 8 methods (justify larger interfaces in XML doc)

### Rule 2.4.2 — Repository interfaces MUST be split by concern

FORBIDDEN:

```csharp
public interface IPayeeRepository
{
    Task<Payee> GetByIdAsync(Guid id);
    Task<List<Payee>> GetAllAsync();
    Task<List<Payee>> GetByManagerAsync(Guid managerId);
    Task<int> CountByTenantAsync(Guid tenantId);
    Task AddAsync(Payee payee);
    Task UpdateAsync(Payee payee);
    Task DeleteAsync(Guid id);
    Task<bool> ExistsByEmailAsync(string email);
    // ... 20 more methods
}
```

REQUIRED:

```csharp
public interface IPayeeReadRepository
{
    Task<Payee?> GetByIdAsync(Guid id);
    Task<bool> ExistsByEmailAsync(string email, Guid tenantId);
    Task<bool> ExistsByEmployeeCodeAsync(string code, Guid tenantId);
}

public interface IPayeeWriteRepository
{
    Task AddAsync(Payee payee);
    Task UpdateAsync(Payee payee);
}

public interface IPayeeQueryService  // for paginated lists
{
    Task<PagedResult<Payee>> QueryAsync(PayeesQuery query);
}
```

Each consumer injects only what it needs. Tests mock only what's relevant.

---

## 2.5 Dependency Inversion (DIP)

High-level modules MUST NOT depend on low-level modules. Both MUST depend on abstractions.

**Most important SOLID principle for Wasnie.** Commission engine (high-level) MUST NOT depend on EF Core or SQL Server (low-level). It depends on `IPayeeReadRepository` (abstraction owned by Domain or Application).

This makes the engine:
- Testable without a database
- Independent of SQL Server
- Resistant to ORM bugs polluting business logic

### Rule 2.5.1 — High-level code MUST depend on interfaces

Use cases receive dependencies as interfaces via constructor injection, never concrete classes.

### Rule 2.5.2 — Interfaces live in the layer that USES them, not implements them

```
Application/
└── Services/
    └── IEmailService.cs              ← defined here
Infrastructure/
└── Email/
    └── SendGridEmailService.cs       ← implementation here
```

Application doesn't know about SendGrid.

### Rule 2.5.3 — Static dependencies and singletons are FORBIDDEN in business logic

In Domain and Application, the following are FORBIDDEN:

- `DateTime.UtcNow` → use `IClock`
- `Guid.NewGuid()` → use `IGuidGenerator` (for tests)
- `Random` → use `IRandomGenerator`
- `Environment.GetEnvironmentVariable` → use config injection
- Static service locators → always constructor injection

These appear convenient but make tests time-dependent, non-deterministic, environment-dependent. For a system that calculates money, non-determinism is unacceptable.

**Infrastructure layer:** `DateTime.UtcNow` is allowed for logging timestamps and similar non-business-logic uses.

### Correct

```csharp
public sealed class TerminatePayeeUseCase
{
    private readonly IPayeeWriteRepository _repo;
    private readonly IClock _clock;

    public TerminatePayeeUseCase(IPayeeWriteRepository repo, IClock clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public async Task ExecuteAsync(Guid payeeId)
    {
        var payee = await _repo.GetByIdAsync(payeeId);
        var today = DateOnly.FromDateTime(_clock.UtcNow);
        payee.Terminate(today);
        await _repo.UpdateAsync(payee);
    }
}

// Tests:
var fakeClock = new FakeClock(new DateTime(2026, 5, 26));
var useCase = new TerminatePayeeUseCase(repo, fakeClock);
// Deterministic, reproducible.
```

### NEVER

```csharp
public sealed class TerminatePayeeUseCase
{
    public async Task ExecuteAsync(Guid payeeId)
    {
        var payee = await PayeeRepository.GetByIdAsync(payeeId);  // ← static
        var today = DateOnly.FromDateTime(DateTime.UtcNow);       // ← non-deterministic
        payee.Terminate(today);
        // Tests cannot control "today". Day-end edge cases untestable.
    }
}
```

---

## Enforcement

- **Project structure:** Section 01 prevents many SOLID violations physically.
- **Code review:** Every PR reviewed against this section. Violations block merge.
- **Static analysis:** SonarCloud rules (Phase C5) flag SRP violations (large classes), OCP violations (excessive switches), DIP violations (static deps).
- **Unit test difficulty as signal:** If a test requires 10+ mocks or static state control, the code violates SOLID. Refactor before testing.

---

## Bug history

- **Phase A2:** Backend Import services designed with SRP from day one. > 85% coverage trivial.
- **Phase A3:** Frontend auto-detect extracted to pure function (SRP) made 30+ focused tests with trivial setup.
- **Phase 2 (anticipated):** Calculation Engine will be the OCP/LSP test. `ICommissionRule` is the prototype for all future rule types.
