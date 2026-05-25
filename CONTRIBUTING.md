# Contributing

This is a private repository. Contributions are by invitation only.

---

## Development setup

Follow the [README](README.md) to get the API and UI running locally before making any changes.

---

## Branching

| Branch | Purpose |
|---|---|
| `main` | Stable, deployable code |
| `feature/<short-name>` | New functionality |
| `fix/<short-name>` | Bug fixes |
| `chore/<short-name>` | Tooling, config, dependency updates |

Branch from `main`. Open a pull request back to `main`. Direct commits to `main` are not allowed.

---

## Commit style

Format: `<type>: <imperative summary>` (72 characters max on the first line)

| Type | When |
|---|---|
| `feat` | New feature or endpoint |
| `fix` | Bug fix |
| `refactor` | Code change with no functional difference |
| `test` | Adding or updating tests |
| `chore` | Build tooling, dependencies, config |
| `docs` | Documentation only |

Examples:
```
feat: add clone-plan endpoint with draft-source guard
fix: resolve WsSelect preload mismatch for numeric enum values
test: add cross-tenant integration tests for plan endpoints
```

---

## Code standards

### Backend (.NET)

- Clean Architecture layer rules are enforced: Domain references nothing; Application references Domain only; Infrastructure implements Application interfaces.
- Domain logic stays in domain entities. No business rules in handlers or controllers.
- Every public handler must have a corresponding unit test.
- New endpoints require an integration test covering at minimum: happy path, validation failure, and cross-tenant rejection.
- `Result<T>` must be used as the return type for all Application handlers. Exceptions are for truly exceptional conditions (programming errors, not domain violations).
- `DomainException` must be caught in handlers and returned as `Result.Failure`. It must not reach the global exception middleware.

### Frontend (Angular)

- All components are standalone.
- State is managed via Angular Signals. No `BehaviorSubject` or `ReplaySubject` for local component state.
- No raw `<input type="date">` or `<select>` — use `<ws-date-picker>` and `<ws-select>`.
- No hex codes or raw rgba values in component SCSS — CSS variable tokens only.
- No inline styles in templates.
- Follow the full rules in [`WasnieUi/DESIGN_SYSTEM.md`](WasnieUi/DESIGN_SYSTEM.md). Violations block review.
- All interactive text (buttons, links, nav items, dropdowns) uses `font-weight: 600`.

---

## Pull request checklist

Before opening a PR:

- [ ] `dotnet test` passes with zero failures in both `Wasnie.UnitTests` and `Wasnie.IntegrationTests`
- [ ] `ng build --configuration production` succeeds with zero errors and zero warnings
- [ ] No hex codes or raw rgba values introduced in SCSS files
- [ ] No secrets, connection strings, or credentials added to any committed file
- [ ] `appsettings.Development.json` and `appsettings.Production.json` are not staged (`git status` confirms)
- [ ] New public API surface is covered by at least one integration test
- [ ] PR description explains the *why*, not just the *what*

---

## Secret hygiene

**Never commit secrets.** If you accidentally commit a secret:

1. Do not try to `git rm` and amend — assume the secret is compromised.
2. Rotate the secret immediately in all environments.
3. Notify the maintainer via the contact in [SECURITY.md](SECURITY.md).
4. Force-push a clean history only after the secret has been rotated.

The `.gitignore` excludes `appsettings.Development.json` and `appsettings.Production.json`. If `git status` shows either of these files as staged, remove them before committing.
