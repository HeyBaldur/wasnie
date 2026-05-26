# 11 — CI/CD Quality Gates

**Reading time:** ~6 min
**Applies to:** Build pipeline, deployment

---

## Why this matters

Without quality gates, every PR that "works on my machine" gets merged. Over months, this accumulates:

- Tests silently break and nobody notices
- Bundle size grows from 400KB to 2MB
- Security vulnerabilities accumulate in dependencies
- `console.log` proliferates in production
- Coverage erodes from 80% to 30%

CI/CD quality gates are **automated discipline**. They prevent slow degradation that no human reviewer catches consistently.

---

## 11.1 What MUST pass before merge

### Rule 11.1.1 — All tests pass

`dotnet test` (entire solution) passes. `ng test --no-watch --code-coverage` passes.

Failing tests block merge. NO "this test is flaky, ignore it" — flaky tests get fixed or removed.

### Rule 11.1.2 — Build succeeds with zero warnings

Both backend and frontend build with zero warnings. Warnings are bugs in disguise.

```yaml
# Backend
dotnet build --configuration Release --warnaserror

# Frontend
ng build --configuration production
# Plus check that no warning was printed to stderr
```

### Rule 11.1.3 — Linter passes

- Backend: SonarCloud (or equivalent) — no new code smells, no new bugs
- Frontend: ESLint — zero errors, zero warnings
- TypeScript: strict mode enabled, no `any` without justification comment

### Rule 11.1.4 — Coverage thresholds met

- Backend overall: > 80%
- Calculation logic specifically: > 95%
- Frontend overall: > 60%

Drops > 5% from main block merge unless manually approved.

### Rule 11.1.5 — No known vulnerabilities

- `npm audit` — no HIGH or CRITICAL
- `dotnet list package --vulnerable` — none
- Snyk / GitHub Dependabot — no open critical alerts

### Rule 11.1.6 — Bundle size budget respected

Frontend:
- Initial bundle: < 500 KB gzipped
- Lazy chunks: < 200 KB gzipped each

CI fails if exceeded.

### Rule 11.1.7 — No `console.log` in production code

Frontend lint rule blocks `console.log`. `console.error` allowed for genuine error logging (also gets sent to Sentry in production).

Backend: `Console.WriteLine` similarly blocked. Use `ILogger<T>` instead.

### Rule 11.1.8 — No TODO without ticket link

`TODO: fix this later` without an issue link is blocked. Format MUST be: `TODO(#123): description`. CI greps for bare TODOs.

### Rule 11.1.9 — No commented-out code

Commented-out code is dead weight. Either delete or document why kept (e.g., explanatory pseudocode in a comment is fine; commented code that "might be useful" is not).

### Rule 11.1.10 — Architecture tests pass

NetArchTest (or equivalent) verifies layer dependencies (file 01). New violations block merge.

---

## 11.2 What MUST pass before deploy

### Rule 11.2.1 — Migrations apply cleanly

In staging environment, run all pending migrations. Verify success.

### Rule 11.2.2 — Integration test suite passes against staging

Full integration test suite runs against staging environment (which uses production-like data and infrastructure).

### Rule 11.2.3 — Smoke tests pass after deploy

After production deploy, automated smoke tests verify:
- Health endpoint responds
- Login flow works (test account)
- Critical pages render
- No spike in errors

Failure of smoke tests triggers automatic rollback.

---

## 11.3 Pipeline configuration

### Rule 11.3.1 — Pipeline is in code (Infrastructure as Code)

`.github/workflows/*.yml` or `azure-pipelines.yml` in the repo. Reviewable. Versionable.

### Rule 11.3.2 — Secrets in pipeline are via vault

NEVER plaintext secrets in pipeline config. Use GitHub Actions secrets / Azure DevOps variables tied to Key Vault.

### Rule 11.3.3 — Pipeline runs on every PR

Not just on main. Every PR triggers the full pipeline.

### Rule 11.3.4 — Pipeline runs in parallel where possible

Backend tests + frontend tests + linters run concurrently to speed up feedback.

### Rule 11.3.5 — Pipeline fails fast

If lint fails, don't run tests. If tests fail, don't run deploy. Save compute.

---

## 11.4 Branching strategy

### Rule 11.4.1 — Main is always deployable

Anything merged to main MUST be deployable. No "wait until end of sprint" partial features.

### Rule 11.4.2 — Feature branches short-lived

Feature branches live max 1 week. Long-lived branches accumulate merge conflicts and drift.

### Rule 11.4.3 — Branch protection on main

- No direct push to main
- PRs require approval
- All CI checks pass
- Branch up to date with main before merge

### Rule 11.4.4 — Squash merge default

Squash merge keeps history clean. Commit message of the squash describes the change (PR title + description).

---

## 11.5 Deployment process

### Rule 11.5.1 — Deployments are automated

No manual `dotnet publish` to a server. CI/CD pipeline handles everything from merge to production.

### Rule 11.5.2 — Deployment is reversible

Every deploy MUST be revertable in < 5 minutes. Strategies:
- Blue-green deployment with traffic switch
- Or: previous version artifact available, redeploy script tested

### Rule 11.5.3 — Database migrations precede code deploy

The new code MUST work with the database schema BEFORE the new code is deployed. Migration runs in earlier stage.

For breaking schema changes, see file 08, Rule 8.4.1.

### Rule 11.5.4 — Environment parity

- Development: local
- Staging: closely mirrors production (same Azure region, similar resources)
- Production: live

Differences must be documented. No "but it works in staging" surprises.

---

## 11.6 Monitoring after deploy

### Rule 11.6.1 — Deploy spike detection

Monitor for 30 minutes after deploy:
- Error rate (alert if > 2× baseline)
- Response time P95 (alert if > 1.5× baseline)
- Failed auth (alert if anomalous)

### Rule 11.6.2 — Alert routing

Deploy-related alerts go to the engineer who triggered the deploy + on-call rotation.

### Rule 11.6.3 — Rollback decision criteria

Predefined criteria for rollback:
- Error rate > 5%
- Response time P95 > 3× baseline for 5 minutes
- Smoke test failure on critical path
- Customer-impacting bug reported within 1 hour of deploy

---

## 11.7 Phase rollout

Quality gates introduced progressively. Phase 1 closure (Phase C5 of Master Plan) introduces:

1. Backend test suite execution on PR
2. Frontend test suite execution on PR
3. Coverage report on PR
4. Linter checks
5. Bundle size check
6. Security scan (npm audit, dotnet vulnerable)
7. Build with warnings as errors
8. Branch protection rules

Phase C6 (Observability) adds:
- Application Insights / Sentry integration
- Smoke tests after deploy
- Performance baseline tracking

Phase 4+ adds:
- Visual regression testing
- E2E test suite in pipeline
- Automated rollback on alerts

---

## Enforcement

- **Pipeline config** is the enforcement mechanism — if it's not in the pipeline, it doesn't happen
- **Code review** checks pipeline changes carefully (they affect everything)
- **Quarterly review** of pipeline rules: are they still effective? Are there gaps?

---

## Bug history

- **Phase 1:** No CI/CD pipeline exists yet beyond basic Azure DevOps deploy. Phase C5 will introduce all of this.
- **Phase A lesson:** "no regressions" is verified by running the FULL test suite, not partial. This MUST be the pipeline default once it exists.
