# 13 — Claude Code Autonomy Boundary

**Reading time:** ~3 min
**Applies to:** Every prompt sent to Claude Code

---

## Why this matters

Claude Code is a productive AI agent that operates autonomously on tasks. Autonomy enables velocity. But unrestricted autonomy is dangerous:

- A single `git push --force` can destroy hours of work
- A wrong destructive migration can lose production data
- An incorrect API call can cost money or violate terms of service

Wasnie's policy: **Claude Code may modify working copy, but never committed state or external systems.**

---

## 13.1 What Claude Code MAY do autonomously

Without asking permission, Claude Code may:

- Create, modify, or delete files within the project
- Add or remove dependencies (`npm install`, `dotnet add package`)
- Run build commands
- Run test suites
- Run linters and formatters
- Read documentation, code, and configuration
- Search for files and patterns
- Refactor code following the rules in this document
- Generate scaffolding (new components, services, tests)

This is the standard productive mode for Claude Code.

---

## 13.2 What Claude Code MUST NEVER do autonomously

Claude Code MUST NEVER execute, without explicit user confirmation:

### Git operations (ANY)

- `git add`
- `git commit`
- `git push`
- `git pull`
- `git merge`
- `git rebase`
- `git checkout` to a different branch
- `git reset`
- `git stash` (which can hide changes)
- Any custom git command

### Production / external systems

- Deployments (Azure DevOps releases, anything deploying to production)
- Database operations against non-test databases
- Operations against the live application or any staging environment that costs money
- External API calls that have side effects (sending email, creating Stripe charges, etc.)

### Destructive operations

- `rm -rf` on directories outside the project working copy
- Operations that modify system PATH, environment variables permanently, or configuration outside the project
- Mass file deletion (delete > 10 files in one operation without confirming)

---

## 13.3 Standard autonomy footer

Every long-running prompt (estimated > 10 minutes) MUST include the autonomy footer at the end:

```markdown
---

## Autonomy convention

Auto-approve all file modifications, file creations, file deletions within the project, dependency installations (npm, dotnet add package), build commands, test runs, lint commands, and code generation. Proceed without asking for confirmation.

**STRICTLY FORBIDDEN without explicit user confirmation:**
- `git add`
- `git commit`
- `git push`
- `git pull`
- `git merge`
- `git rebase`
- `git checkout` to a different branch
- `git reset`
- `git stash`
- ANY git command that modifies the repository state
- Database operations against non-test databases
- External API calls that have side effects

If a git operation is needed, STOP and report what needs to be committed. Wait for the user to handle the git operation manually.

If an operation is unclear, STOP and ask.
```

---

## 13.4 If Claude Code encounters a need for forbidden operations

When Claude Code's task seemingly requires a forbidden operation:

### Rule 13.4.1 — STOP and report

Do not attempt to work around the restriction. Stop the task and report:
- What operation would be needed
- Why
- What the user needs to do

Example:

```
I cannot proceed with this refactor because it requires merging an existing branch.
Per ARCHITECTURE.md §13, git operations are user-only.

Please run:
  git merge --no-ff feature/payee-import
  git push

Then re-run this prompt. I will pick up from where I left off.
```

### Rule 13.4.2 — Suggest alternatives that don't require forbidden ops

If a task seems to require forbidden ops, the AI MAY suggest an alternative approach. E.g., "Instead of merging branches, I can apply the changes manually to your current branch — would you like that?"

### Rule 13.4.3 — Document boundary friction

If certain forbidden ops are needed routinely, that signals our workflow is wrong. The user should consider whether to:
- Adjust the workflow (do these operations themselves more proactively)
- Or amend this rule (formally allow specific git ops with logging)

Amendments via the process in `ARCHITECTURE.md`.

---

## 13.5 Why git is specifically excluded

Git is the safety net for everything else. If Claude Code makes a mistake:
- Files modified: `git checkout .` rolls back
- Dependencies added: `package.json` reverted via git
- Code broken: revert via git

If Claude Code itself uses git autonomously, it can destroy its own rollback option. By restricting git to the user, we keep the user as the authoritative checkpoint manager.

Additionally:
- Commit messages benefit from human context (the "why")
- Commit atomicity is a human decision (what logically goes together)
- Branching strategy is workflow, not implementation

---

## 13.6 Claude (in chat) responsibilities

Claude (in this chat, separate from Claude Code) MUST:

### Rule 13.6.1 — Include the autonomy footer in every long prompt

When generating prompts for Claude Code, include the footer (Rule 13.3) by default for any prompt that would run > 10 minutes.

### Rule 13.6.2 — Refuse to generate prompts that ask Claude Code to violate this section

If the user asks for a prompt that would make Claude Code commit, push, or perform other forbidden ops, Claude MUST refuse:

> "I cannot generate this prompt because it would have Claude Code violate ARCHITECTURE.md §13 (Claude Code Autonomy Boundary). Specifically: [reason]. Would you like me to generate a prompt that pauses for you to handle the git operation manually?"

### Rule 13.6.3 — Educate the user when they ask for forbidden ops

If the user is unaware of the rule, briefly explain. The rule exists for their safety.

---

## Enforcement

- **Prompt review:** Claude (chat) checks every prompt for forbidden operations before generating
- **Footer presence:** every non-trivial prompt has the footer
- **Code review:** if Claude Code somehow performs a git op, that's a bug in our prompt template, not a workflow choice

---

## Bug history

- **Phase A (May 2026):** User left Claude Code unattended for an hour expecting work, returned to find Claude Code waiting for confirmation. Issue: default "safe mode" of Claude Code asks before installing packages.
- **Resolution:** introduced autonomy footer convention (Rule 13.3). Codified here.
- **No incident** of Claude Code performing forbidden git operations has occurred (because the footer prevents it). Continuing this rule prevents the first incident.
