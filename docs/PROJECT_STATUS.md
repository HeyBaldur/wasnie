# Wasnie — Project Status

**Last updated:** 2026-05-27
**Updated by:** Rodolfo Calvo (post-audit session)
**Purpose:** Single source of truth for "where Wasnie is right now." Read this first when resuming work.

---

## What Wasnie is (in 2 sentences)

Wasnie is a Sales Performance Management (SPM) / Incentive Compensation Management (ICM) SaaS platform for mid-market companies (50–300 sales reps). It targets the gap between enterprise SPM tools (Xactly, SAP, Varicent — too complex and expensive) and spreadsheets (still used by ~50% of mid-market), with European + Latin American focus initially.

For full product context, see `docs/Wasnie_Product_Master_Specification.md` and `docs/Wasnie_Business_Brief.docx`.

---

## Founder + Stack

**Founder:** Rodolfo Calvo, based in Katowice, Poland. Solo founder during current development phase.

**Backend:** ASP.NET Core 8 + C#, MediatR, FluentValidation, AutoMapper, EF Core, MS SQL Server, JWT, Serilog. Clean Architecture (Domain, Application, Infrastructure, Api).

**Frontend:** Angular 20 standalone components with signals, Tailwind CSS, ngx-translate (EN/ES/PL). Testcontainers for integration tests. Jasmine/Karma for unit tests.

**Deployment:** Azure App Service (Free F1, West Europe). Azure DevOps CI/CD. Currently deploys directly from main branch on push.

**Repo location:** `C:\Users\fillo\Documents\Sales\Wasnie\` (Windows dev environment, Visual Studio 2022 + VS Code).

---

## Where we are in the Master Plan

```
✅ Phase 0 — Foundation (done before May 2026)
✅ Phase 1 — Plans, payees, quotas, assignments, basic import (done — Phase A closed 2026-05-26)

PHASE B — Architecture & Quality Standards
✅ B0 — Product docs (User Personas + Business Brief, done 2026-05-26)
✅ B1 — ARCHITECTURE.md + 14 section files (done 2026-05-26)
✅ B2 — Codebase audit (done 2026-05-27) — see docs/audit/Audit_Findings.md
⏭️ B3 — Prioritized backlog for Phase C (NEXT)

PHASE C — Critical Quality Gaps (3-4 weeks estimated)
⏭️ C1 — Server-side pagination (✅ DONE in Phase A via prompts 39-43)
⏭️ C2 — Claims & Authorization (RBAC + tier limits)
⏭️ C3 — Audit Trail standardized
⏭️ C4 — Security Hardening
⏭️ C5 — CI/CD Quality Gates
⏭️ C6 — Observability
⏭️ C7 — Clean Architecture + DIP fixes (NEW — proposed from B2 audit)

PHASE D — Phase 1 officially closed (~1 week)
⏭️ D1 — Backend coverage > 80%
⏭️ D2 — Frontend coverage > 60%
⏭️ D3 — E2E happy path tests (includes deferred A4)
⏭️ D4 — Master Spec v2.1 + docs update

PHASE 2+ — Transactions, Calculation Engine, Visibility, etc.
   (cannot start until Phase D signed off)
```

For full plan details: `docs/Wasnie_Master_Plan_Phase_1_Closure.md`

---

## Audit findings summary (B2 results, 2026-05-27)

27 total findings across the codebase:

| Severity | Count | Examples |
|---|---|---|
| 🔴 Critical | 8 | MediatR in Domain (F-001), DateTime.UtcNow in entities (F-003), JWT lifetimes too long (F-005/006), import cache cross-tenant (F-007), email verification disabled (F-008) |
| 🟠 High | 7 | No RBAC/tier limits (F-009), no security headers (F-010), no rate limiting (F-011), no audit trail (F-014) |
| 🟡 Medium | 8 | Missing validators (F-016), no cross-tenant tests for some endpoints (F-017), IgnoreQueryFilters without guard (F-018) |
| 🟢 Low | 4 | Dev secret in git (F-024), legacy entity (F-026), Serilog cloud sink missing (F-027) |

Top 5 critical to fix first:
1. **F-007** — Import cache keys without TenantId (real cross-tenant leak vector)
2. **F-005 / F-006** — JWT lifetimes 4x longer than allowed (config change, 5 min fix)
3. **F-008** — Email verification disabled
4. **F-003 / F-004** — IClock and IGuidGenerator abstractions needed
5. **F-001 / F-002** — Clean Architecture layer violations

**Positive compliance areas** (no findings):
- All controllers are thin MediatR delegates
- Server-side pagination on every list endpoint
- EF Core global query filters on tenant-scoped entities
- No HttpClient in components (frontend HTTP architecture clean)
- No SQL injection (EF Core LINQ everywhere)
- Integration tests use real Testcontainers MSSQL
- No console.log in frontend
- CORS not wildcarded

For full audit: `docs/audit/Audit_Findings.md`

---

## Active work / current focus

**Right now we are:** Generating B3 — converting the 27 audit findings into a prioritized backlog with dependencies and effort estimates. After B3, we start fixing Critical findings (Phase C).

**Most recent significant work:**
- Created ARCHITECTURE.md + 14 section files (binding technical law for the project)
- Created User Personas + Business Brief (product docs for external use)
- Closed Phase A (Import feature with 144+ tests)
- Completed B2 audit (27 findings identified, codebase otherwise solid)

**Not yet started:**
- Phase C fixes (waiting for B3 prioritization)
- Phase 2 (Transactions module — cannot start until Phase D)
- Marketing / content strategy (planned for parallel work once Phase 1 fully closed)

---

## Important decisions made

1. **Document hierarchy:** ARCHITECTURE.md > Product Spec > DESIGN_SYSTEM > Master Plan. Conflicts resolved in this order.
2. **Strict architecture enforcement:** Claude (chat) acts as gatekeeper. Refuses prompts that violate ARCHITECTURE.md.
3. **All technical docs in English.** Chats in Spanish.
4. **Personal Trainer background NEVER mentioned** in Wasnie context (separate professional identity).
5. **Claude Code autonomy boundary:** auto-approve file/build/test, NEVER autonomous git operations.
6. **A4 (E2E tests) deferred to Phase 9** (Compliance & Enterprise readiness).
7. **Subscription tiers:** Free / Starter (€300) / Growth (€800) / Scale (€1,800) / Enterprise (€2,500+).
8. **Target markets in order:** Poland → Central & Eastern Europe → Iberian & LATAM markets.
9. **Mobile responsive deferred to Phase 8.** Desktop-only until then (1280px+).

---

## Key naming conventions

- **Backend projects:** `Wasnie.Domain`, `Wasnie.Application`, `Wasnie.Infrastructure`, `Wasnie.Api` (note: not "Presentation", but `.Api`)
- **Frontend:** `WasnieUi/`
- **Reusable Angular components:** `Ws` prefix (`WsButton`, `WsCard`, `WsDataTable`, `WsPageLayout`)
- **Languages supported:** English (primary), Spanish, Polish
- **Image folder convention:** `WasnieUi/public/` (Angular 17+ pattern), not `src/assets/`

---

## Working conventions

- **Prompts to Claude Code:** Must reference ARCHITECTURE.md sections explicitly. Long prompts include the Autonomy Footer (file 13).
- **Visual changes:** Surgical, not systemic. Numerical specs only ("15% lighter," not "subtle"). Inspect existing structure before specifying replacement.
- **Breaking changes:** All consumers updated in same PR. Run FULL test suite, not partial.
- **Multi-tenant testing:** Every endpoint MUST have a cross-tenant test.
- **No commits by Claude Code.** Ever. Code/files/deps OK, git operations user-only.

---

## Open questions / pending decisions

(Update this section as questions emerge that need answers before proceeding)

- **None currently open** as of 2026-05-27.

---

## Document index (where everything lives)

```
docs/
├── PROJECT_STATUS.md                              ← THIS FILE
├── SESSION_LOG.md                                 ← session history
├── ARCHITECTURE.md                                ← master technical law
├── architecture/                                  ← 14 architecture section files
├── audit/
│   └── Audit_Findings.md                          ← B2 audit results
├── Wasnie_Product_Master_Specification.md         ← product spec
├── Wasnie_Master_Plan_Phase_1_Closure.md          ← operational plan (v1.1)
├── Wasnie_User_Personas.md                        ← user personas + JTBD
├── Wasnie_Business_Brief.docx                     ← external presentation
└── Wasnie_Informe_Tecnico.docx                    ← original market analysis (Spanish, historical)

WasnieUi/
└── DESIGN_SYSTEM.md                               ← frontend visual rules
```

---

## How to resume work in a new chat

When starting a new conversation with Claude, send this as your first message:

```
Hola. Soy Rudolf, founder de Wasnie (SPM/ICM SaaS).
Para retomar contexto rápido, por favor lee primero:
1. docs/PROJECT_STATUS.md (este archivo)
2. docs/SESSION_LOG.md (últimas 3 entradas)
3. docs/ARCHITECTURE.md (master, no las secciones)

Después dime "listo" y arrancamos con [tu tarea].
```

Claude will read these, summarize where we are, and you can proceed without re-explaining context.

---

## Update protocol

**This file MUST be updated:**
- After every significant work session (>30 min of progress)
- After every Phase completion or new Phase start
- After any major architectural decision
- After any audit, review, or status check

**Format:** Update the relevant sections directly. Bump the "Last updated" date. Append a brief note to SESSION_LOG.md.
