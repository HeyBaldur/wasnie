# 01 — Clean Architecture

**Reading time:** ~10 min
**Applies to:** Backend (.NET 8) and Frontend (Angular 20)

---

## Why this matters

Wasnie processes money. Business logic that determines how commissions are calculated MUST be:

- **Testable in isolation** — without database, without HTTP, without third-party APIs
- **Independent of frameworks** — so that ASP.NET Core upgrades or Angular major bumps never force rewriting commission rules
- **Independent of UI** — so we can build desktop, mobile, or partner API on top of the same core
- **Independent of database** — so we can switch storage or add caching without changing commission rules

When business logic is entangled with frameworks, databases, or UIs, three things happen:

1. **Tests are slow and unreliable**
2. **Refactoring is impossible**
3. **Bugs hide in the wrong place** — a commission miscalculation might be an ORM mapping issue, taking days to find

For financial software, none is acceptable.

---

## Backend layers (.NET 8)

The Wasnie backend MUST be organized in exactly four layers:

```
┌─────────────────────────────────────────────────────────┐
│  Presentation (WasnieApi.Presentation)                  │
│  Controllers, request/response DTOs, model binding,     │
│  authentication middleware, OpenAPI/Swagger             │
└─────────────────────┬───────────────────────────────────┘
                      │ depends on
                      ▼
┌─────────────────────────────────────────────────────────┐
│  Application (WasnieApi.Application)                    │
│  Use cases (commands, queries), application services,   │
│  validation, orchestration, DTO ↔ Domain mapping        │
└─────────────────────┬───────────────────────────────────┘
                      │ depends on
                      ▼
┌─────────────────────────────────────────────────────────┐
│  Domain (WasnieApi.Domain)                              │
│  Entities, value objects, domain services, domain       │
│  events, business rules, domain interfaces (ports)      │
└─────────────────────────────────────────────────────────┘
                      ▲
                      │ depends on
                      │
┌─────────────────────┴───────────────────────────────────┐
│  Infrastructure (WasnieApi.Infrastructure)              │
│  EF Core DbContext, repository implementations, file    │
│  storage, third-party API clients, identity, caching    │
└─────────────────────────────────────────────────────────┘
```

**Critical:** Infrastructure depends on Domain (not reverse). Domain defines interfaces (ports); Infrastructure implements them (adapters).

---

## Backend rules

### Rule 1.1 — Domain has NO external dependencies

Domain MUST reference nothing except the .NET base class library. No EF Core. No ASP.NET Core. No FluentValidation. No MediatR. No AutoMapper.

```xml
<!-- WasnieApi.Domain.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <!-- No PackageReference allowed. -->
  <!-- No ProjectReference allowed. -->
</Project>
```

### Rule 1.2 — Application depends ONLY on Domain

Application may reference Domain plus approved cross-cutting libraries (MediatR, FluentValidation, AutoMapper). Application MUST NOT reference Infrastructure or Presentation.

### Rule 1.3 — Infrastructure depends on Domain (and Application for interfaces)

Infrastructure implements Domain interfaces. May reference Application if Application defines interfaces it must implement.

### Rule 1.4 — Presentation depends on Application only

Controllers MUST NOT:
- Instantiate domain entities directly
- Query the DbContext directly
- Call Infrastructure services directly

```xml
<!-- WasnieApi.Presentation.csproj -->
<ItemGroup>
  <ProjectReference Include="..\WasnieApi.Application\WasnieApi.Application.csproj" />
  <!-- NEVER: ProjectReference to Domain or Infrastructure -->
</ItemGroup>
```

**Exception:** `Program.cs` (composition root) MAY reference everything to wire up DI.

### Rule 1.5 — NEVER add framework attributes to Domain entities

Domain entities MUST NOT have:
- EF Core attributes (`[Key]`, `[Column]`, `[ForeignKey]`, `[Table]`)
- ASP.NET attributes
- JSON serialization attributes
- AutoMapper attributes

EF Core config MUST be Fluent API in `Infrastructure/Persistence/Configurations/`.

#### Correct

```csharp
// Domain/Entities/Payee.cs
namespace WasnieApi.Domain.Entities;

public sealed class Payee
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string FullName { get; private set; }
    public string EmployeeCode { get; private set; }
    public string Email { get; private set; }
    public DateOnly HireDate { get; private set; }
    public PayeeStatus Status { get; private set; }
    public Guid? ManagerId { get; private set; }

    private Payee() { }  // EF Core

    public static Payee Create(
        Guid tenantId, string fullName, string employeeCode,
        string email, DateOnly hireDate, Guid? managerId = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("Full name is required.");
        if (hireDate > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new DomainException("Hire date cannot be in the future.");

        return new Payee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = fullName.Trim(),
            EmployeeCode = employeeCode.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            HireDate = hireDate,
            Status = PayeeStatus.Active,
            ManagerId = managerId
        };
    }
}
```

```csharp
// Infrastructure/Persistence/Configurations/PayeeConfiguration.cs
public class PayeeConfiguration : IEntityTypeConfiguration<Payee>
{
    public void Configure(EntityTypeBuilder<Payee> builder)
    {
        builder.ToTable("Payees");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.FullName).IsRequired().HasMaxLength(200);
        builder.HasIndex(p => new { p.TenantId, p.EmployeeCode }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.Email }).IsUnique();
    }
}
```

#### NEVER

```csharp
// Domain/Entities/Payee.cs — WRONG
[Table("Payees")]                            // ← EF attribute in Domain
public sealed class Payee
{
    [Key] public Guid Id { get; set; }       // ← EF attribute
    [Required, MaxLength(200)]               // ← Data annotation
    public string FullName { get; set; }
    [JsonPropertyName("employee_code")]      // ← JSON in Domain
    public string EmployeeCode { get; set; }
    public PayeeStatus Status { get; set; }  // ← Public setter breaks encapsulation
}
```

---

## Frontend layers (Angular 20)

```
┌─────────────────────────────────────────────────────────┐
│  Components (features/*/components, shared/components)  │
│  Templates, presentation, event handlers, user input.   │
│  NO business logic. NO HTTP calls.                      │
└─────────────────────┬───────────────────────────────────┘
                      │ depends on
                      ▼
┌─────────────────────────────────────────────────────────┐
│  Services / Facades (features/*/services)               │
│  Application logic, state management, HTTP orchestrat., │
│  view-model construction.                               │
└─────────────────────┬───────────────────────────────────┘
                      │ depends on
                      ▼
┌─────────────────────────────────────────────────────────┐
│  Domain logic (features/*/helpers, *.model.ts)          │
│  Pure functions, business rules, calculations,          │
│  type definitions. NO framework dependencies.           │
└─────────────────────────────────────────────────────────┘
                      ▲
                      │ depends on
┌─────────────────────┴───────────────────────────────────┐
│  Infrastructure (core/http, core/auth, core/storage)    │
│  HttpClient wrappers, auth interceptors, localStorage,  │
│  routing utilities, third-party SDK adapters.           │
└─────────────────────────────────────────────────────────┘
```

### Rule 1.6 — Components MUST NOT make HTTP calls directly

Components inject services. Services own HTTP. Components never inject `HttpClient`.

#### Correct

```typescript
// features/payees/services/payees.service.ts
@Injectable({ providedIn: 'root' })
export class PayeesService {
  private http = inject(HttpClient);
  getPayees(query: PayeesQuery): Observable<PagedResult<Payee>> {
    return this.http.get<PagedResult<Payee>>('/api/payees', {
      params: buildHttpParams(query)
    });
  }
}

// features/payees/payees-list.component.ts
@Component({ /* ... */ })
export class PayeesListComponent {
  private payeesService = inject(PayeesService);
  payees = signal<Payee[]>([]);
}
```

#### NEVER

```typescript
@Component({ /* ... */ })
export class PayeesListComponent {
  private http = inject(HttpClient);  // ← Component injecting HttpClient
  ngOnInit() {
    this.http.get('/api/payees').subscribe(/* ... */);  // ← HTTP from component
  }
}
```

### Rule 1.7 — Business logic MUST be in pure functions

If logic:
- Does not depend on Angular
- Can be expressed as input → output
- Has business meaning

...it MUST be in `*.helper.ts` or `*.utils.ts` and unit-tested independently.

**Bug history (Phase A3):** `composeFullName` and `autoDetectColumns` were initially embedded in `MappingStepComponent`. Extracting them reduced test setup from 50 lines to 2 lines per test and made coverage > 95% trivial.

### Rule 1.8 — Components MUST NOT contain financial calculations

Any money calculation MUST be in a pure function in `helpers/` or `calculations/`, covered by unit tests of every meaningful path. ZERO exceptions.

Inline financial logic in templates is FORBIDDEN:
```typescript
<!-- FORBIDDEN -->
<div>{{ amount * 1.21 }}</div>

<!-- CORRECT -->
<div>{{ withTax(amount, taxRate) }}</div>
```

### Rule 1.9 — Service files MUST have one responsibility

- `PayeesService` — HTTP for /api/payees. NOT validation. NOT state.
- `PayeesStateService` — signal state. NOT HTTP.
- `PayeesFacadeService` — combines them. Single injection point.

If > 200 lines or > 8 public methods, MUST be split.

---

## Cross-cutting rules

### Rule 1.10 — NEVER bypass layers for "performance"

Skipping layers (controller → DbContext, e.g.) is FORBIDDEN. Performance is addressed via profiling, caching, query optimization — never by breaking architecture.

### Rule 1.11 — NEVER add a new layer without amendment

The four layers are fixed. Adding "Service", "Manager", "Helper" layer requires documented amendment. Architectural drift is forbidden.

### Rule 1.12 — Composition root is the ONLY exception

`Program.cs` (backend) and `app.config.ts` (frontend) are composition roots. They MAY reference all layers for DI wiring.

---

## Enforcement

- **Project references:** `.csproj` files prevent illegal dependencies at build time
- **Linter (frontend):** ESLint `no-restricted-imports` blocks `HttpClient` outside `core/` and `features/*/services/` (Phase C5)
- **Code review:** Every PR reviewed for layer violations
- **Phase B2 audit:** Existing codebase audited; findings logged

---

## Bug history

- **Phase A3 (May 2026):** Frontend logic embedded in components made tests painful. Extracted to pure functions per Rule 1.7. Pattern enforced.
- **Phase B2 (pending):** First full audit of existing codebase will produce findings.
