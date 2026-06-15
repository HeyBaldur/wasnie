# ARCHITECTURE.md — Wasnie

**Status:** ACTIVE — Binding
**Version:** 1.1
**Created:** 2026-05-26
**Owner:** Rodolfo A. Calvo Jaubert
**Scope:** Wasnie (Sales Performance Management SaaS)

---

## 1. Document purpose

### Why this document exists

Wasnie is **financial software**. It calculates how much money real human beings get paid. A bug that miscalculates one cent in a single commission is not "a small bug" — it is a **breach of trust** that can:

- Trigger payroll disputes and employee lawsuits
- Violate financial regulations (SOX, GDPR financial provisions, local labor laws)
- Damage the customer's relationship with their sales team
- End the customer's relationship with Wasnie

Every line of code in Wasnie must be written with this reality in mind. This document codifies the non-negotiable engineering principles that protect that reality.

### What this document is

This is **the technical law** of the Wasnie project. Every architectural decision, every code change, every prompt to Claude Code, every pull request must respect it.

### What this document is NOT

- It is not a tutorial. It assumes the reader knows .NET, Angular, SQL, and modern web development.
- It is not a product specification. For "what features Wasnie has," see `Wasnie_Product_Master_Specification.md`.
- It is not a roadmap. For "what we are building right now," see `Wasnie_Master_Plan_Phase_1_Closure.md`.

### Document precedence

When in conflict, this is the order:

1. **ARCHITECTURE.md** (this document + the 14 section files) — non-negotiable technical law
2. **Wasnie_Product_Master_Specification.md** — what the product does
3. **DESIGN_SYSTEM.md** (in WasnieUi/) — visual and UI rules
4. **Wasnie_Master_Plan_Phase_1_Closure.md** — current operational plan
5. README, code comments, and other documentation

If you find yourself wanting to violate a rule because "it would be more pragmatic," **STOP**. Either the rule is wrong (and must be amended), or your pragmatism is hiding a future failure.

---

## 2. Document structure

This document is split into focused files for efficient consumption by Claude Code and human reviewers.

```
docs/
├── ARCHITECTURE.md                                ← THIS FILE (master + routing)
└── architecture/
    ├── 01-clean-architecture.md                   ← Layer rules (backend + frontend)
    ├── 02-solid.md                                ← SOLID principles
    ├── 03-performance-baselines.md                ← Numerical performance rules
    ├── 04-security.md                             ← Security requirements (OWASP-aware)
    ├── 05-audit-trail.md                          ← Immutable audit log requirements
    ├── 06-authorization.md                        ← Subscription tiers + RBAC
    ├── 07-testing-standards.md                    ← Coverage, mocking rules, test types
    ├── 08-breaking-change-protocol.md             ← How to change endpoints/DTOs safely
    ├── 09-multi-tenant-isolation.md               ← Tenant boundary enforcement
    ├── 10-visual-changes-protocol.md              ← Surgical UI prompts
    ├── 11-cicd-quality-gates.md                   ← Build pipeline standards
    ├── 12-observability.md                        ← Logging, metrics, tracing
    ├── 13-claude-code-autonomy.md                 ← What AI may/may not do
    └── 14-forbidden-patterns.md                   ← Consolidated FORBIDDEN list
```

Each file is independently readable but cross-references others when relevant.

---

## 3. Critical universal rules

These are the **absolutely non-negotiable rules** that apply to every change in Wasnie. They are the distilled essence of the 14 section files. Claude Code MUST respect these regardless of which sections it reads.

**The Critical Thirteen:**

1. **Multi-tenant isolation:** Every query touching tenant data MUST filter by tenant ID. No exceptions. See file 09.

2. **No money math without tests:** Any calculation that touches money MUST be in a pure function covered by unit tests. See files 02 (Rule 2.8) and 07.

3. **No client-side pagination:** All lists MUST be server-paginated. Max 100 items per page. See file 03.

4. **No DateTime.UtcNow in business logic:** Use `IClock`. See file 02 (Rule 3.5.3).

5. **No string-concatenated SQL:** Only parameterized queries / EF Core. See file 04.

6. **No `console.log` / `Console.WriteLine` in production code:** Use structured logging. See file 12.

7. **No git operations by Claude Code:** Ever. See file 13.

8. **No silent breaking changes:** Any endpoint/DTO signature change MUST update all consumers in the same PR. See file 08.

9. **No skipping authentication:** Every endpoint requires auth except `/auth/*` and `/health`. See file 04.

10. **Audit trail on destructive operations:** Every DELETE, status change, or money-related write MUST log who/when/what/before/after. See file 05.

11. **No `any` in TypeScript / `dynamic` in C# without justification:** Type safety is mandatory. See file 14.

12. **No new architectural layer without amendment:** The Clean Architecture layers are fixed. See file 01.

13. **Migrations must be applied and verified, never left pending.** When a work item creates or modifies an EF Core migration, Claude Code MUST apply it to the database (`dotnet ef database update --project ... --startup-project ...`) AND verify the schema change is present BEFORE reporting the work item complete. "Done" from the EF tooling is NOT sufficient proof — confirm the table or columns actually exist. If the update fails (API process holding a DLL lock, wrong connection string, or any other cause), Claude Code MUST report this explicitly and state exactly what the user must do to complete the apply — never leave a migration created-but-unapplied silently. A migration file that exists but was never applied produces "Invalid column name" errors that masquerade as code bugs and waste diagnosis time. See file 08, Rule 8.4.4.

---

## 4. Routing table — which files to read for each task type

When working on Wasnie, read this file first (`ARCHITECTURE.md`) plus the specific section files relevant to your task. Reading all 14 files for every task wastes context and slows the AI.

### Backend tasks

| Task type | Required reading (in addition to this master) |
|---|---|
| New API endpoint | 01, 03, 04, 06, 07, 08, 09 |
| New domain entity | 01, 02, 05, 07, 09 |
| New use case / command / query | 01, 02, 03, 07 |
| Database migration | 01, 03, 08, 09 |
| Calculation logic (commissions, payouts) | 02, 03, 05, 07 |
| Authentication / authorization change | 04, 06, 13 |
| Audit logging | 05, 09, 12 |
| Performance optimization | 03, 12 |
| Refactor of existing code | 01, 02, 07, 08 |

### Frontend tasks

| Task type | Required reading |
|---|---|
| New component (no business logic) | 01, 10, plus `DESIGN_SYSTEM.md` |
| New feature with services | 01, 02, 07, 10, plus `DESIGN_SYSTEM.md` |
| New form with validation | 01, 02, 07, plus `DESIGN_SYSTEM.md` |
| New page / wizard | 01, 10, plus `DESIGN_SYSTEM.md` |
| Calculation displayed in UI | 02 (Rule 2.8), 03, 07 |
| Visual fix / style change | 10, plus `DESIGN_SYSTEM.md` |
| New service (HTTP, state) | 01, 02, 07 |
| Authentication / authorization UI | 04, 06 |

### Cross-cutting tasks

| Task type | Required reading |
|---|---|
| Tests (any type) | 02, 07, 09 |
| CI/CD pipeline change | 11, 13 |
| Adding a new dependency | 01, 04, 11 |
| Logging change | 05, 12 |
| Adding a new role or permission | 04, 06 |
| Migrating to a new subscription tier | 06 |

### When in doubt

Read 01 (Clean Architecture), 02 (SOLID), 07 (Testing), and 14 (Forbidden patterns). These four cover 80% of any task.

---

## 5. Claude Code prompt protocol

Every prompt sent to Claude Code that does non-trivial work MUST include this preamble:

```markdown
## Architecture compliance

Per `docs/ARCHITECTURE.md`, this task must respect:
- Critical Twelve (always)
- Sections: [list specific section files from the routing table above]

Read those files before proceeding. Do not violate any rule in them.

For any rule that conflicts with what the task seems to require, STOP and report the conflict instead of silently violating.
```

Plus the **standard autonomy footer** (see file 13).

This is not optional. Prompts that omit this preamble MUST be rejected or rewritten.

---

## 6. Amendment process

This document is binding but not frozen. Reality changes. Lessons emerge. Technology evolves.

To amend a rule:

1. **Document the reason.** What bug, performance issue, or insight motivates the change?
2. **Propose the new rule.** Written in the same MUST/NEVER/FORBIDDEN style.
3. **Discuss the trade-off.** What protections are being relaxed? What new ones are being added?
4. **Update the version.** Bump the version number at the top of this file AND the affected section file. Add an entry to the changelog at the bottom.
5. **Communicate the change.** If others are working on Wasnie, they must know.

Amendments require explicit owner approval. Silent violation is not amendment.

---

## 7. Authority

When this document conflicts with:

- **A developer's preference** — the document wins.
- **A product feature request** — the document wins. Either the product spec is wrong, or this document needs amendment first.
- **A "more pragmatic" approach** — the document wins.
- **Claude Code's autonomous decisions** — the document wins. Claude Code MUST refuse to violate rules even if instructed to.
- **Claude (in chat) generating prompts** — Claude MUST refuse to generate prompts that violate this document, regardless of how the user phrases the request.

The only thing that overrides this document is an **amendment to this document**.

---

## Document changelog

| Version | Date | Change |
|---|---|---|
| 1.1 | 2026-06-15 | Added Critical Rule 13: migrations must be applied and verified before reporting WI complete. Root cause: migrations created-but-unapplied caused "Invalid column name 'IsQualified'" and "Invalid object name 'PasswordResetTokens'" errors twice in the same session, wasting diagnosis time. Added Rule 8.4.4 in file 08. Updated routing table: "Database migration" now requires reading file 08. |
| 1.0 | 2026-05-26 | Initial creation. Codifies lessons from Phase A. |

---

## Status of section files

| File | Status | Last reviewed |
|---|---|---|
| 01-clean-architecture.md | ACTIVE | 2026-05-26 |
| 02-solid.md | ACTIVE | 2026-05-26 |
| 03-performance-baselines.md | ACTIVE | 2026-05-26 |
| 04-security.md | ACTIVE | 2026-05-26 |
| 05-audit-trail.md | ACTIVE | 2026-05-26 |
| 06-authorization.md | ACTIVE | 2026-05-26 |
| 07-testing-standards.md | ACTIVE | 2026-05-26 |
| 08-breaking-change-protocol.md | ACTIVE | 2026-05-26 |
| 09-multi-tenant-isolation.md | ACTIVE | 2026-05-26 |
| 10-visual-changes-protocol.md | ACTIVE | 2026-05-26 |
| 11-cicd-quality-gates.md | ACTIVE | 2026-05-26 |
| 12-observability.md | ACTIVE | 2026-05-26 |
| 13-claude-code-autonomy.md | ACTIVE | 2026-05-26 |
| 14-forbidden-patterns.md | ACTIVE | 2026-05-26 |
