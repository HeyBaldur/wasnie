# Prompt — Update Continuity Docs After Session

**Use case:** After any significant work session (>30 min), run this prompt to update `PROJECT_STATUS.md` and append a new entry to `SESSION_LOG.md`. This keeps context portable across chats.

**When to run this:**
- Before closing a chat session for the day
- After completing a Phase milestone
- After any architectural decision is made
- After Claude Code completes a significant prompt
- Whenever PROJECT_STATUS.md no longer reflects current reality

**When NOT to run this:**
- For trivial tweaks (typo fixes, single-file CSS adjustments)
- Mid-task (let the task finish first, then update)

---

## The prompt

Copy this template and fill in the bracketed values before pasting to Claude Code:

```
## Task — Update Continuity Documentation

Per docs/ARCHITECTURE.md section 13, this is a documentation-only task.

### What just happened in this session

[FILL IN: brief description of what was done this session — 2-5 sentences. Include any prompts executed, files created/modified, decisions made, phase progress.]

### Key decisions made

[FILL IN: bullet list of decisions worth preserving — naming, architectural choices, deferred work, etc. Empty list is fine if no decisions.]

### Files produced/modified this session

[FILL IN: list paths of new or modified files, briefly noting what each contains]

### Current focus / next steps

[FILL IN: what we plan to work on next — within current phase or transitioning to next phase]

---

## Required updates

### 1. Update docs/PROJECT_STATUS.md

Make the following edits to PROJECT_STATUS.md:

- Bump the "Last updated" date to today's date (2026-MM-DD format)
- Update "Updated by" to "Rodolfo Calvo (post-[session topic] session)"
- Update the "Where we are in the Master Plan" section if a phase status changed (✅ done / ⏭️ next / etc.)
- Update the "Active work / current focus" section with current state
- Update the "Most recent significant work" bullets in that section
- If applicable, update the audit findings summary (when new findings are addressed or new ones discovered)
- If applicable, update "Important decisions made" with any new decisions from this session
- If applicable, update "Open questions / pending decisions"
- If applicable, update the document index with any new docs

### 2. Append entry to docs/SESSION_LOG.md

Add a new entry at the TOP of the "Sessions (newest first)" section, before all existing entries.

Use this format:

```markdown
## YYYY-MM-DD — [Brief session title from "What just happened" above]

**Duration:** ~X hours [estimate based on session content]
**Phase:** [phase identifier — A, B0, B1, B2, B3, C2, etc.]

### What we did
- [bullet list summarizing the "What just happened" content]

### Key decisions
- [from "Key decisions made" above]

### Files produced this session
- [from "Files produced/modified" above]

### What's next
- [from "Current focus / next steps" above]

### Notes / lessons learned (only if applicable)
- [optional — only add if there are insights worth preserving]
```

Add the new entry, followed by the `---` separator, ABOVE the most recent existing entry.

### 3. Hard constraints

- This is a documentation-only task. Do NOT modify any code file.
- Do NOT modify any architecture doc, product spec, or other content doc.
- Do NOT create new files (only modify PROJECT_STATUS.md and SESSION_LOG.md).
- Do NOT run tests, builds, or migrations.
- Do NOT perform any git operations.
- Format MUST match the existing structure of both files. If existing structure is unclear, READ both files first.

### 4. Verification

Before completing:

1. Read both files first to understand current structure
2. Make edits preserving existing formatting (line breaks, header levels, etc.)
3. Confirm git status shows only PROJECT_STATUS.md and SESSION_LOG.md as modified
4. Confirm both files are well-formed markdown (no broken links, no orphan headers)

### 5. Report on completion

Report:

1. Sections of PROJECT_STATUS.md that were updated (list)
2. New SESSION_LOG.md entry summary
3. git diff --stat output showing only 2 files changed
4. Any ambiguities encountered

---

## Autonomy convention

Auto-approve all file read and file edit operations on PROJECT_STATUS.md and SESSION_LOG.md only.

**STRICTLY FORBIDDEN:**
- Modifying any file other than PROJECT_STATUS.md and SESSION_LOG.md
- Creating new files
- Any git operation
- Running tests, builds, or migrations

If unclear, STOP and ask.
```

---

## Usage examples

### Example 1: After completing a phase milestone

```
## Task — Update Continuity Documentation

### What just happened in this session

Completed B3 — prioritized backlog of 27 audit findings. Created docs/audit/Audit_Backlog.md with effort estimates, dependencies, and recommended fix order. Confirmed Phase C will start with F-007 (cache cross-tenant), followed by JWT lifetimes and email verification fixes.

### Key decisions made

- Phase C will be split into 6 subphases (C1 done, C2-C6 + new C7 for Clean Arch fixes)
- F-007 fix is first priority (single SaaS-grade exploit risk identified)
- F-003/F-004 (IClock + IGuidGenerator) will be combined into one infrastructure prompt

### Files produced/modified this session

- docs/audit/Audit_Backlog.md (new)

### Current focus / next steps

- Start Phase C with prompt for F-007 (cache cross-tenant fix)

[then paste the rest of the template]
```

### Example 2: After Claude Code completed a major refactor

```
## Task — Update Continuity Documentation

### What just happened in this session

Claude Code executed Phase C2 — introduced IAuthorizationService, IClaimsService, and TierLimitChecker. Refactored 4 use case handlers to call IAuthorizationService.Require(). Added integration tests for permission checks (12 new tests, all passing).

### Key decisions made

- Permission strings format: "Resource.Action" (e.g., "Payees.Create", "Plans.Activate")
- Tier limits enforced before write, not after
- TenantAdmin gets ALL permissions implicitly; no need to list

### Files produced/modified this session

- src/Wasnie.Application/Authorization/* (new module, 6 files)
- src/Wasnie.Infrastructure/Authorization/* (new module, 3 files)
- tests/Wasnie.IntegrationTests/Authorization/* (new, 4 files)
- src/Wasnie.Application/Compensation/Handlers/* (modified, 4 files)

### Current focus / next steps

- Phase C3 — Audit Trail standardized

[continue with template...]
```

---

## Notes on this strategy

**Why this works:**

1. Context regenerates automatically from the docs at the start of every chat
2. Decisions are preserved literally, not as my "memory" (which can drift)
3. Session log gives traceability for "why did we do X 3 weeks ago"
4. PROJECT_STATUS stays compact and current — fits in any context window
5. Anyone (you, Claude, Claude Code, future hire) can onboard in 5 minutes

**What this does NOT solve:**

- Long-term context drift in chat conversations (still need to feed docs back occasionally)
- Subtle preferences not yet documented (we'll add them as they emerge)
- Code-level details (those live in code + tests, not docs)

**When to revise PROJECT_STATUS.md structure itself:**

If after 4-6 weeks the doc feels stale or missing sections, schedule a 30-min session to refactor it. The structure proposed here is intentional but not sacred.
