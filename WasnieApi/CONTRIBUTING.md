# Contributing to Wasnie API

## Local development setup

Each developer uses their own machine-local secrets via `dotnet user-secrets`. Secrets are **never** committed to git.

### 1. Set the JWT signing secret

```bash
cd src/Wasnie.Api
dotnet user-secrets init   # only needed the first time
dotnet user-secrets set "JwtSettings:Secret" "your-local-dev-secret-min-32-chars"
```

### 2. Set the database connection string

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOURSERVER;Database=WasnieDb;Trusted_Connection=True;TrustServerCertificate=True;"
```

### 3. Apply database migrations

```bash
dotnet ef database update --project src/Wasnie.Infrastructure --startup-project src/Wasnie.Api
```

### 4. Run the API

```bash
dotnet run --project src/Wasnie.Api
```

---

## Template

`src/Wasnie.Api/appsettings.Development.template.json` documents the expected structure.
Copy it to `appsettings.Development.json` (gitignored) and fill in your local values,
or use `dotnet user-secrets` as shown above (preferred).

---

## Tests

Integration tests use Testcontainers (Docker required) and run against a real SQL Server container.
No manual database setup is needed for tests.

```bash
dotnet test
```
