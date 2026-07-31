# Wasnie

> Multi-tenant Sales Performance Management (SPM) platform for structured compensation plan definition, rule evaluation, and payout management.

Private repository. All rights reserved. See [LICENSE](LICENSE).

---

## Architecture overview

```
Wasnie/
  WasnieApi/          ASP.NET Core .NET 8 — Clean Architecture
  WasnieUi/           Angular 20 — standalone components, Signals, Tailwind CSS v4
```

### Backend layers

```
WasnieApi/src/
  Wasnie.Domain           Entities, value objects, domain events, enums
  Wasnie.Application      CQRS handlers (MediatR), validators (FluentValidation), DTOs
  Wasnie.Infrastructure   EF Core 8, ASP.NET Core Identity, JWT token service
  Wasnie.Api              Controllers, middleware, OpenAPI, DI composition root
```

Dependency rule: `Api → Application → Domain`. `Infrastructure → Application → Domain`. Domain references nothing.

### Frontend structure

```
WasnieUi/src/app/
  core/           Services, guards, interceptors (provided at root)
  shared/
    ui/           Design-system primitives (WsButton, WsSelect, WsCard, …)
    components/   App-wide layout components (Sidebar, Topbar)
  features/
    auth/         Login, tenant registration
    dashboard/    Summary shell
    plans/        Compensation plan + rule management
    payees/       Sales rep management
    transactions/ Deal / order list
    payouts/      Commission payout list
    admin/        Tenant settings
```

---

## Multi-tenancy

Every HTTP request carries a `tenant_id` claim in the JWT. `TenantContext` (Infrastructure) reads it from `HttpContext.User` and exposes it via `ITenantContext`. Every tenant-scoped EF entity applies a global `HasQueryFilter` in `ApplicationDbContext.OnModelCreating` — no query crosses a tenant boundary.

---

## Getting started

### Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 8.x |
| SQL Server | 2019+ (local instance or Docker) |
| Node.js | 20+ |
| Docker | Required for integration tests (Testcontainers pulls SQL Server 2022) |

### 1. Configure the API

Copy the development settings template and fill in your values:

```bash
cp WasnieApi/src/Wasnie.Api/appsettings.Development.template.json \
   WasnieApi/src/Wasnie.Api/appsettings.Development.json
```

Edit `appsettings.Development.json`:
- `ConnectionStrings.DefaultConnection` — point to your local SQL Server instance
- `JwtSettings.Secret` — generate a random string of at least 32 characters

#### The assistant's chat model (optional)

The AI assistant answers through Groq. **The API key never goes in a settings file** — the committed
`appsettings.json` declares only the shape of the section, with an empty `ApiKey`, and the value is
supplied by the secret channel at runtime. .NET merges the two: structure from JSON, value from the
secret store.

```bash
cd WasnieApi/src/Wasnie.Api
dotnet user-secrets set "Groq:ApiKey" "gsk_your_key_here"
```

In Azure (or any deployed environment) the same value arrives as an environment variable or a Key
Vault entry, using `__` where the JSON has `:`:

```
Groq__ApiKey = gsk_your_key_here
```

This is the same pattern in both places — structure in JSON, value outside it — not two different
setups. **The assistant works without a key**: it falls back to a stand-in reply, so nothing breaks if
you skip this step.

The non-secret options (`Model`, `BaseUrl`, `MaxHistoryMessages`, `TimeoutSeconds`) live in
`appsettings.json` with working defaults and can be overridden per environment like any other setting.

### 2. Create the database and apply migrations

```bash
cd WasnieApi
dotnet ef database update --project src/Wasnie.Infrastructure --startup-project src/Wasnie.Api
```

### 3. Start the API

```bash
cd WasnieApi/src/Wasnie.Api
dotnet run
```

OpenAPI UI: `https://localhost:7012/swagger`

### 4. Start the UI

```bash
cd WasnieUi
npm install
npm start
```

App: `http://localhost:4200`

---

## Running tests

### Unit tests

```bash
cd WasnieApi
dotnet test tests/Wasnie.UnitTests
```

### Integration tests

Docker must be running. Testcontainers starts a SQL Server 2022 container automatically.

```bash
cd WasnieApi
dotnet test tests/Wasnie.IntegrationTests
```

Test suite: 128 tests (101 unit + 27 integration), 0 skipped.

---

## Configuration reference

| File | Committed | Purpose |
|---|---|---|
| `appsettings.json` | Yes | Base configuration, safe placeholder values |
| `appsettings.Development.template.json` | Yes | Setup template for local development |
| `appsettings.Development.json` | **No** | Local dev secrets — never commit |
| `appsettings.Production.json` | **No** | Production secrets — set via environment variables or Key Vault |
| User Secrets (`dotnet user-secrets`) | **No** | Local dev secret values, stored outside the repository |

Production secrets are injected at deploy time via environment variables (`ConnectionStrings__DefaultConnection`, `JwtSettings__Secret`, `Groq__ApiKey`) or Azure Key Vault. No secrets live in source control.

**The rule for adding a new setting:** the *structure* goes in the committed `appsettings.json` — with
the secret field left empty — so the setting is discoverable and Azure knows what to fill. The *value*
goes in User Secrets locally and in the environment/Key Vault when deployed. A committed file must
never hold a real credential: deleting it later does not remove it from git history, so a leaked key
has to be rotated, not just erased.

---

## API surface

### Auth

| Method | Path | Description |
|---|---|---|
| POST | `/api/auth/register-tenant` | Provision new tenant + admin user |
| POST | `/api/auth/login` | Authenticate, receive JWT + refresh token |
| POST | `/api/auth/refresh` | Rotate tokens using a valid refresh token |
| POST | `/api/auth/logout` | Invalidate the current session |

JWT payload: `sub`, `email`, `tenant_id`, role claims.

### Compensation plans

| Method | Path | Description |
|---|---|---|
| GET | `/api/plans` | List plans for the current tenant |
| POST | `/api/plans` | Create a new draft plan |
| GET | `/api/plans/{id}` | Get a single plan |
| PUT | `/api/plans/{id}` | Update a draft plan |
| DELETE | `/api/plans/{id}` | Delete a draft plan |
| POST | `/api/plans/{id}/activate` | Activate a draft plan |
| POST | `/api/plans/{id}/archive` | Archive an active plan |
| POST | `/api/plans/{id}/clone` | Clone an active plan as a new draft version |
| POST | `/api/plans/{id}/rules` | Add a compensation rule to a plan |
| PUT | `/api/plans/{id}/rules/{ruleId}` | Update a rule |
| DELETE | `/api/plans/{id}/rules/{ruleId}` | Remove a rule |

---

## Tech stack

| Layer | Technology |
|---|---|
| Backend runtime | ASP.NET Core .NET 8 |
| ORM | Entity Framework Core 8 (Code-First, SQL Server) |
| CQRS | MediatR 12 |
| Validation | FluentValidation 11 |
| Logging | Serilog (console + rolling file) |
| Auth | ASP.NET Core Identity + JWT Bearer |
| API docs | Swashbuckle / OpenAPI 3 |
| Frontend framework | Angular 20, standalone components |
| Reactivity | Angular Signals |
| Styling | Tailwind CSS v4 (`@apply` in SCSS), custom design system |
| i18n | @ngx-translate/core v17 (en / es / pl) |
| Testing — unit | xUnit, FluentAssertions |
| Testing — integration | xUnit, Testcontainers (SQL Server 2022) |

---

## Design system

Component primitives live in `WasnieUi/src/app/shared/ui/`. Token definitions are in `WasnieUi/src/styles.scss`. Full documentation: [`WasnieUi/DESIGN_SYSTEM.md`](WasnieUi/DESIGN_SYSTEM.md).

Dev preview (development only): navigate to `http://localhost:4200/__design-system`.

---

## Security

See [SECURITY.md](SECURITY.md) for the vulnerability reporting policy.
