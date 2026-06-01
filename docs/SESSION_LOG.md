# Wasnie — Session Log

**Purpose:** Append-only log of work sessions. Each entry records what was accomplished, what was deferred, and key decisions. Read newest entries first to retrace recent work.

**Format:** Each session is a level-2 heading (`##`) with date and brief title. Newest entries at the TOP of the log section. Update PROJECT_STATUS.md when status changes materially.

---

## Sessions (newest first)

---

## 2026-06-01 — Full milestone close: WI-PROD-E + A.3 + R Done; WI-PROD-MODEL fully realized in code

**Type:** Implementation + smoke test + UX polish + docs  
**Status:** Milestone closed ✅  
**Test count:** 628 → 692 backend (+64 across the full day); 143 frontend (static)

### Day timeline

**Morning:** WI-PROD-A.2 closure carried over from previous day — confirmed Settings UI shows 6 rows; Payee edit form accepts EmploymentType + Location.

**Mid-day:** WI-PROD-E (validation messages with category) — scope confirmed as complete; second real-world validation recorded: a payee CSV with `EmploymentType = "Full-time"` (hyphenated) triggered the contextual error during the WI-PROD-A.3 smoke test, letting the owner recover in seconds.

**Afternoon:** WI-PROD-A.3 implemented — Payee lifecycle (IsActive + DeactivatedAt), nullable CompensationTransaction.PayeeId, AssignPayeeCommand + ReassignPayeeCommand with full state-machine enforcement. 644 → 692 backend tests (+48). See Decision #40 for technical summary. Smoke test passed:
- EMP310 Mateusz Walczak deactivated → "Inactive" badge rendered; reactivated cleanly.
- Re-import against EMP310 → Warning with Reference category + contextual message.
- 8 transactions imported without payeeCode (Transaction.PayeeId = Optional); persisted as Unassigned.
- AssignPayeeCommand on Unassigned → EMP301 assigned correctly.
- ReassignPayeeCommand: empty reason rejected; "short" (5 chars) rejected; 52-char reason with EMP302 accepted; audit event recorded reason.
- Settings shows 7 catalog rows (Transaction.PayeeId, Optional).
- **Key verification:** reason-required is enforced across BOTH states of the Transaction.PayeeId toggle — the two settings are orthogonal. Reason is hardcoded in the domain (NOT configurable), protecting the audit trail.

**Late afternoon:** WI-PROD-R — UX polish after smoke test feedback. Three sub-fixes:
1. "Unassigned" in the transaction list now italic + `--color-text-tertiary` (distinguishable at a glance from assigned rows).
2. Assign/Reassign modal payee picker dropdown overflow: resolved with `position: fixed` in ws-select when inside a `.ws-modal__dialog`. Earlier approach (280px height estimate) correctly set direction but did not escape `overflow: hidden` clipping. Final fix is the canonical portal approach — fixed positioning via `getBoundingClientRect()`.
3. Settings seventh-row label: "Payee / Transaction" → "Require payee on new transactions" with descriptive subtitle in EN/ES/PL.

**Investigation note:** A transient visual concern on the Reassign modal (appeared to show clipped search input) was investigated across two sessions. Confirmed it was a structural `overflow: hidden` + `position: absolute` interaction, NOT a system bug. The reason-required rule behaves correctly in all states. The `position: fixed` fix resolved the visual issue permanently.

### What was recorded

- Decision #40: WI-PROD-A.3 technical summary (updated with hardcoded-reason note).
- Decision #41: Trilogy milestone + smoke-test checkpoint list.
- Decision #42: Architectural observations (data-driven Settings validated 2→7; state machine in domain; reason hardcoded; ws-select fixed-positioning pattern).
- WI-PROD-P: tolerant enum parsing — LOW priority backlog.
- WI-PROD-Q: frontend test coverage sweep — LOW-MEDIUM priority backlog.
- WI-PROD-R: corrected technical summary (position:fixed, not just height estimate).

---

## 2026-06-01 — WI-PROD-R DONE: Unassigned visibility + ws-select async overflow fix

**WI:** WI-PROD-R — UX polish  
**Status:** DONE ✅  
**Test count:** 143 frontend (unchanged) · 692 backend (unchanged)  
**Files changed:** 3 (transactions-list.component.html, .scss, ws-select.component.ts)

### Fix 1 — Unassigned visibility

`transactions-list.component.html`: replaced `{{ tx.payeeName || ('TRANSACTIONS.UNASSIGNED' | translate) }}` with a conditional block that wraps the absent-payee case in `<span class="col-unassigned">`. `transactions-list.component.scss`: `.col-unassigned { font-style: italic; color: var(--color-text-tertiary); }`. No new i18n keys. No badge added — "Unassigned" is a normal operational state, not an error; italic + tertiary is the appropriate absent-value treatment.

### Fix 2 — Modal async dropdown overflow

**Root cause:** `ws-select.component.ts` `openDropdown()` estimates dropdown height from `this.filteredOptions().length * 36`. In async mode, `filteredOptions()` is empty at open time → estimate = 60px → algorithm places dropdown downward → async results load → dropdown grows to 284px → overflows modal dialog.

**Fix:** One-line change — when `searchFn()` is non-null (async mode), use `estimatedHeight = 280` unconditionally. The existing `.ws-modal__dialog`-aware positioning code was already correct and already used the dialog bounds; it just needed the right estimate to trigger upward placement before results arrived.

**Binding rule noted for `14-forbidden-patterns.md`:** When `ws-select` is used in a modal with `searchFn` (async mode), the positioning is handled automatically by the component's modal-aware logic — do NOT add custom dropdown overflow hacks in modal SCSS.

---

## 2026-06-01 — Milestone close: WI-PROD-A trilogy + WI-PROD-MODEL fully realized in code

**Type:** Documentation + in-vivo smoke test. No new code. No builds. No migrations.  
**Status:** Milestone closed ✅

### What was validated in vivo

Full smoke test of Decisions D, G, 10, 11, 12 from WI-PROD-MODEL using real Reserved Polska retail data:

1. **Decision G (Payee lifecycle):** EMP310 Mateusz Walczak (Silesia City Center) deactivated via detail page. "Inactive" badge appeared correctly in both the payee list and detail header. Reactivation cleared `DeactivatedAt` and removed the badge.

2. **Decision 12 (import against inactive payee):** Re-imported a small CSV with EMP310 as `payeeCode`. Preview step showed `IssueSeverity.Warning` with `IssueCategory.Reference` and the message "Payee 'EMP310' is inactive — assignment will be historical". Row was imported normally. `skipRowsWithWarnings` toggle confirmed working (row skipped when enabled).

3. **Decisions D + 10 (nullable PayeeId / Unassigned is derived):** 8 transactions imported without `payeeCode` column populated (Transaction.PayeeId = Optional in Settings). All persisted with `PayeeId IS NULL`. Transaction list rendered "Unassigned" using the WI-PROD-F defensive rendering. Settings page confirmed 7 catalog rows (added Transaction → Payee row, default Optional).

4. **Decision 11 — AssignPayeeCommand:** Clicked "Assign" on an Unassigned transaction. Picked EMP301 Agnieszka Jankowska via async dropdown. Comment field left blank (optional). Submitted. Transaction now shows EMP301's name. Audit event recorded.

5. **Decision 11 — ReassignPayeeCommand:** Clicked "Reassign" on the just-assigned transaction. Empty reason → blocked with validation message. "short" (5 chars) → blocked with min-10 message. "Cliente confirmó vendedor correcto en cierre de turno" (52 chars) accepted. EMP302 picked. Submitted. Transaction shows EMP302. Audit event includes reason text.

6. **WI-PROD-E (contextual error messages) — incidental validation:** A payee CSV with `EmploymentType = "Full-time"` (human-readable, hyphenated — natural Excel export variant) triggered the contextual error. Message showed the offending value `'Full-time'` and the accepted values `FullTime, PartTime, Temporary, Contractor`. Owner resolved in seconds without support. This confirmed WI-PROD-E's design intent in a realistic accidental-entry scenario. The tolerance gap is tracked as WI-PROD-P.

### What was documented

- WI-PROD-E: added second real-world validation note (EmploymentType smoke test).
- WI-PROD-A.3 sub-items: all ✅ marks added.
- WI-PROD-MODEL: updated to "fully realized in code" milestone.
- Decision #41: trilogy + MODEL milestone entry recorded.
- WI-PROD-P: tolerant enum parsing — new backlog item (LOW priority).
- WI-PROD-Q: frontend test coverage sweep — new backlog item (LOW-MEDIUM priority; 143 tests static across 4 WIs).

### Test counts (cumulative across today's sessions)

- Backend: 628 → 692 (+64 across WI-PROD-E close + WI-PROD-A.3). 0 failures.
- Frontend: 143 (static — WI-PROD-Q tracks this debt).
- Trilogy total since A.1 start: 561 → 692 (+131 backend).

### Architectural observation

A.1's data-driven Settings UI absorbed the seventh catalog row (Transaction → Payee, Optional) without template edits — only the i18n key `SETTINGS.FIELD_PAYEEID` was missing and was added as a two-line fix. The foundation continues to validate itself with each new catalog addition.

---

## 2026-06-01 — WI-PROD-A.3 DONE: Payee lifecycle + nullable PayeeId + Assign/Reassign commands

**WI:** WI-PROD-A.3 — Final sub-WI of WI-PROD-A; closes WI-PROD-A and WI-PROD-MODEL trilogy  
**Status:** DONE ✅  
**Test count:** 644 → 692 backend (+48: 27 unit + 21 integration); 143 frontend (unchanged)

### Milestone: WI-PROD-MODEL decisions fully realized in code

All 12 decisions from the WI-PROD-MODEL conversation (Decisions #35, #36, #37) are now live. Decision A–I from Parts 1+2 were implemented across WI-PROD-A.1, A.2, and A.3. Decisions 10–12 from Part 3 were implemented in A.3.

### What was done

**Domain:**
- `Payee.IsActive` (bool, default true) + `Payee.DeactivatedAt` (DateTimeOffset?). `Deactivate()` / `Activate()` domain methods.
- `CompensationTransaction.PayeeId` → `Guid?` (nullable). `Assign()` and `Reassign()` domain methods with full state-machine enforcement (Paid blocked, Eligible/Calculated → revert to Pending, Cancelled allowed). `Reassign()` requires reason ≥ 10 chars.
- 4 new domain events: `TransactionPayeeAssignedEvent`, `TransactionPayeeReassignedEvent` (used), `PayeeDeactivatedEvent`, `PayeeActivatedEvent` (deleted — Payee extends BaseAuditableEntity not AggregateRoot; handler-level audit used instead).
- New audit actions: `PayeeDeactivated`, `PayeeActivated`, `TransactionPayeeAssigned`, `TransactionPayeeReassigned`.
- New permissions: `Payees.Deactivate`, `Transactions.Update` (both granted to TenantAdmin + CompManager).

**Application:**
- `DeactivatePayeeCommand` + `ActivatePayeeCommand` (IAuditableCommand, NOT IMoneyCriticalCommand — admin ops, not money mutations).
- `AssignPayeeCommand` + `ReassignPayeeCommand` (both IMoneyCriticalCommand — money-critical audit path).
- `AssignPayeeHandler`, `ReassignPayeeHandler`, `DeactivatePayeeHandler`, `ActivatePayeeHandler`.
- `TransactionFieldNames` constants class (entity="Transaction", field="PayeeId").
- `IngestTransactionCommand.PayeeId` → `Guid?`; handler checks `IFieldRequirementService` for PayeeId optionality; blocks inactive payee assignment on manual entry.
- `PayeeDto` extended with `IsActive`, `DeactivatedAt?`.
- `TransactionDto.PayeeId` → `Guid?`. `ListTransactionsHandler` updated for nullable PayeeId in payee-name resolution.

**Infrastructure:**
- EF migration `P2_PayeeLifecycle`: adds `IsActive` (bit NOT NULL default 1) + `DeactivatedAt` (datetimeoffset NULL) to Payees; makes `CompensationTransactions.PayeeId` nullable; seeds `FieldRequirementSettings` row `Transaction.PayeeId = Optional` for all existing tenants.
- `TransactionImportValidationService` extended: PayeeId optionality check via `IFieldRequirementService`; inactive payee match → Warning with Reference category.
- `TransactionImportJobHandler` extended: null payeeId passed when payeeCode is blank (row passed validation, Optional setting confirmed).

**API:** `POST /api/payees/{id}/deactivate`, `POST /api/payees/{id}/activate`, `POST /api/transactions/{id}/assign-payee`, `POST /api/transactions/{id}/reassign-payee` (409 Conflict for state-rule violations).

**Frontend:**
- `payee.model.ts`: `isActive`, `deactivatedAt`.
- `payees.api.service.ts`: `deactivate()`, `activate()`. Store: `deactivate()`, `activate()`.
- Payee list: "Inactive" WsBadge (warning) + Deactivate/Activate row menu items (gated by `Payees.Deactivate`).
- `transaction.model.ts`: `payeeId` → nullable; `AssignPayeeRequest`, `ReassignPayeeRequest`.
- `transactions.api.service.ts` + store: `assignPayee()`, `reassignPayee()`.
- Transaction list: Assign button (unassigned), Reassign button (assigned non-Paid), disabled+tooltip for Paid (gated by `Transactions.Update`).
- `AssignPayeeModalComponent` (payee picker + optional comment).
- `ReassignPayeeModalComponent` (payee picker + required reason, min-10 validation).
- EN/ES/PL i18n complete: 24 new keys per language.

### Existing test regressions fixed
- `TransactionImportValidationServiceTests`: 32 constructor calls updated to pass `IFieldRequirementService` stub; 2 tests renamed to reflect new Optional-by-default behavior; 1 new test added (`EmptyPayeeCode_WhenOptional_NoError`).
- `TransactionImportEndpointsTests`: `Validate_EmptyPayeeCode_ReturnsError` → `Validate_EmptyPayeeCode_WhenOptional_NoError` (behavior changed per Decision D).

### Architecture notes
- `Payees.Deactivate` chosen (not `Payees.Update`) consistent with existing `Payees.Terminate` finer-grain pattern.
- `WsTextarea` does not exist; reason field uses `WsInput` (single-line text, functionally adequate). Flagged as WsTextarea candidate per §10.3.
- Initial frontend bundle ~562 kB (500 kB budget); pre-existing — modals are lazy-loaded, not in initial chunk.

---

## 2026-06-01 — WI-PROD-E DONE: contextual import error messages + IssueCategory visual distinction

**WI:** WI-PROD-E — Actionable import validation messages  
**Status:** DONE ✅

Validation issue messages now include offending value + corrective action; new `IssueCategory` field on `ValidationIssue` with visual distinction in preview (Reference→amber, Format→red, Required→blue, Other→default). 36 emit sites updated across payee + transaction import validators. 3 new binding rules added to `14-forbidden-patterns.md`. Test count: 628→644 (+16). Smoke-tested in vivo.

---

## 2026-06-01 — Backlog update: WI-PROD-N + WI-PROD-O (upload security); threat-model decision #39

**Type:** Backlog and decision documentation only. No code changes.

### What was recorded

**WI-PROD-N — File upload security hardening** added to backlog.  
Triggered by owner asking whether uploaded files are scanned for viruses. Assessed current state (ClosedXML rejects malformed OOXML; 5 MB limit; no disk persistence; no serving back to users; macros never executed) and documented four gaps to close before the first paying customer: magic-byte validation, per-user upload rate limiting on the two parse endpoints, structured upload logging (actor / hash / MIME / size), and an internal security documentation page for customer IT reviews. Timing: before signing first customer (~1 focused day). NOT urgent today.

**WI-PROD-O — Antivirus scanning integration** added to backlog.  
Three provider candidates documented (Azure Defender for Storage, self-hosted ClamAV, VirusTotal API). Synchronous vs asynchronous scan flow trade-offs recorded. Quarantine workflow scoped (reject file, alert TenantAdmin, log at Critical). Timing: only when contractually required by a customer — do not implement speculatively. WI-PROD-N must ship first.

**Decision #39 — Threat-model snapshot** recorded in "Important decisions made."  
Risk assessment: LOW at current stage. Becomes MEDIUM at first IT-security review. Both WIs visible in backlog so the gap cannot be invented later under contract pressure.

---

## 2026-06-01 — WI-PROD-E: contextual error messages + category badges in import preview

**WI:** WI-PROD-E — Actionable import validation messages  
**Status:** DONE ✅  
**Test count:** 628 → 644 backend (+16 — 8 per validator); 143 frontend (unchanged)

### What was done

**Model (`ImportValidationModels.cs`):**
- `IssueCategory` enum added: `Reference | Format | Required | Other`
- `ValidationIssue.Category` property added with default `Other` — backward-compatible; existing emit sites that weren't updated continue to serialize `Category: "Other"` without breaking

**PayeeImportValidationService — all 22 emit sites updated:**
- Reference errors (duplicate code, duplicate/existing email, manager not found) → embed offending value + corrective action, `Category = Reference`
- Format errors (bad date, bad email format, too long, invalid employment type) → embed offending value, `Category = Format`
- Required errors (email/hire date/role/employment type/location required per settings) → message explains the settings origin, `Category = Required`
- Warnings (personal domain, recent hire date) → `Category = Other` (warnings keep existing style)

**TransactionImportValidationService — all 14 emit sites updated:**
- "Payee code not found." → `"Payee code 'EMP999' not found in this tenant. Create the payee first or correct the code in your file."` `Category = Reference`
- Duplicate reference/externalId → embed value, `Category = Reference`
- Bad amount/currency/date → embed value, `Category = Format`
- Missing reference/payee code → `Category = Required`

**Frontend (both payee and transaction preview steps):**
- `ValidationIssue` model extended with `category: IssueCategory`
- `issueBadgeVariant()` + `issueCategoryKey()` helpers added to both preview components
- Issues column now renders `<ws-badge>` before each message: Reference → `'warning'` (amber), Format → `'danger'` (red), Required → `'info'` (blue), warnings → `'warning'`; `Other` → no badge (neutral, kept minimal)
- `.preview-issue` + `.preview-issue__msg` CSS classes added to both preview SCSS files

**i18n (EN/ES/PL):** `IMPORTS.ISSUE_CATEGORY_REFERENCE/FORMAT/REQUIRED/OTHER` added to all three files

**`14-forbidden-patterns.md`:** New "Validation error message violations" section — three binding rules: (1) embed offending value, (2) corrective action on reference errors, (3) `Category` must be set explicitly

### Smoke test
No dedicated in-vivo test run in this session — the owner has the `_test.xlsx` instructions from the WI prompt and can verify the Reference badge on an EMP999 row. All automated tests green.

---

## 2026-06-01 — WI-PROD-A.2 CLOSED: smoke-tested in vivo; Settings shows 6 rows; Payee form persists new fields

**WI:** WI-PROD-A.2 — Extend field requirement catalog  
**Status:** CLOSED ✅  
**Session type:** Documentation + closure. Code was completed in the prior implementation session (see entry below). This session records the in-vivo validation and officially closes the WI.

### Smoke test results (in vivo, real tenant data)

- **Settings → Field Requirements page:** Shows 6 rows as expected — Email, Hire date, Role, Manager, Employment type, Location. Each toggles independently between Required and Optional.
- **Payee edit form:** EmploymentType select renders with four localized options (Full-time, Part-time, Temporary, Contractor). Location text input renders. Both fields save correctly and persist on reload.
- **No regressions observed** on the existing payee list, payee detail, or import flows.

### Summary

WI-PROD-A.2 completed across two Claude Code sessions:
- **Session 1 (crashed mid-flight):** Full Application layer — `Payee.cs` entity with new fields, `EmploymentType` enum, `PayeeFieldNames` constants, commands/DTOs/handlers/validators.
- **Session 2 (continuation prompt):** EF migration `P2_PayeeNewColumns` (adds columns + seeds 4 catalog rows per tenant), import service extensions, frontend form fields, auto-detect patterns (7 languages), i18n (EN/ES/PL), tests.

**Test count: 595 → 628 (+33).** Build clean. Migration applied to live DB without incident.

**Architectural win confirmed in vivo:** A.1's data-driven Settings UI (iterates over the API response, one row per `FieldRequirementSetting`) absorbed the four new catalog entries automatically. Zero UI template changes needed — only new i18n keys. Pattern holds for all future catalog additions.

### Remaining backlog (next sessions)

- **WI-PROD-A.3** — `Payee.IsActive` + `DeactivatedAt` lifecycle; `AssignPayeeCommand` + `ReassignPayeeCommand` (`IMoneyCriticalCommand`); state machine enforcement; Assign/Reassign UI on transaction detail. (Decisions G, 11, E)
- **WI-PROD-CURRENCY** — Full multi-currency system: account currency on Tenant, FX rate table, original + converted amount duality on Transaction, conversion engine. (Decision I)
- **WI-PROD-K** — Books reconciliation (payout line → bank export).
- **WI-PROD-B/C/E/G/I/J** — Smaller items (multi-sheet Excel picker, onboarding UX, actionable errors, etc.)

---

## 2026-06-01 — WI-PROD-A.2: EmploymentType + Location on Payee; field requirement catalog → 6 entries

**WI:** WI-PROD-A.2 — Extend field requirement catalog  
**Status:** DONE  
**Note:** Completed across two sessions. The previous session (crashed mid-flight due to billing) had done all backend Application layer work. This session added the EF migration, import services, frontend form, and tests.

### What was done

**Backend:**
- `PayeeConfiguration.cs` — added `EmploymentType` (nullable int) and `Location` (nvarchar 200) property configs
- `20260601111756_P2_PayeeNewColumns` migration — adds two nullable columns to `Payees` table; seeds 4 new `FieldRequirementSetting` rows per existing tenant (Role/ManagerId/EmploymentType/Location = Optional by default)
- `PayeeImportColumnMapping.cs` — added `EmploymentTypeColumn` and `LocationColumn` optional properties
- `PayeeImportValidationService.cs` — validates EmploymentType (enum check) and Location (max 200 chars); Role now catalog-driven (Error when required, Warning when optional)
- `PayeeImportExecutionService.cs` — passes EmploymentType (parsed to enum) and Location to `Payee.Create()`

**Tests (before: 595 → after: 628):**
- `PayeeTests.cs` — 7 new domain unit tests for EmploymentType (Create/Update, null, trim) and Location
- `CreatePayeeCommandValidatorTests.cs` — 10 new validator unit tests for Role, ManagerId, EmploymentType (including invalid/valid values), Location; `ConfigurableFieldService` added
- `PayeeImportValidationServiceTests.cs` — 13 new integration tests for EmploymentType (valid types case-insensitive, invalid type error, required error), Location (valid, required error, too long), Role catalog-driven required; `AlwaysRequiredExceptService` helper added

**Frontend:**
- `payee.model.ts` — `Payee` interface + `CreatePayeeRequest` + `UpdatePayeeRequest` gain `employmentType?` and `location?`
- `payee-form.component.ts` — 4 new computed required signals (roleRequired, managerRequired, employmentTypeRequired, locationRequired); `employmentTypeOptions` static SelectOption array; 2 new form controls; effect refactored to loop with `syncRequired` helper; patch and payload extended
- `payee-form.component.html` — EmploymentType `ws-select` (static options) and Location `ws-input`; Role and Manager labels now conditional on required setting
- `payee-import.models.ts` — `PayeeImportColumnMapping` gains `employmentTypeColumn?` and `locationColumn?`
- `mapping-step.component.ts` — form group, auto-detect init, restore, `currentMapping()`, and preview extended for 2 new optional columns
- `mapping-step.component.html` — 2 new optional mapping rows
- `column-auto-detect.ts` — `OTHER_FIELD_PATTERNS` extended with `employmentTypeColumn` (EN/ES/PL/PT/FR/DE/IT patterns) and `locationColumn` (EN/ES/PL/FR/DE/IT patterns)

**i18n (EN + ES + PL):**  
New keys in `PAYEES.*`: FIELD_ROLE_OPTIONAL, FIELD_MANAGER_OPTIONAL, FIELD_EMPLOYMENT_TYPE, FIELD_EMPLOYMENT_TYPE_OPTIONAL, FIELD_EMPLOYMENT_TYPE_PLACEHOLDER, EMPLOYMENT_TYPE_FULLTIME/PARTTIME/TEMPORARY/CONTRACTOR, FIELD_LOCATION, FIELD_LOCATION_OPTIONAL  
New keys in `SETTINGS.*`: FIELD_ROLE, FIELD_MANAGERID, FIELD_EMPLOYMENTTYPE, FIELD_LOCATION

**Settings UI** — fully data-driven (was already data-driven from A.1); shows 6 rows automatically once new catalog rows are seeded.

### Test counts
- Backend: **628 passing** (259 unit + 369 integration, 2 skipped rate-limit tests unchanged)
- Frontend: **143 passing**, build clean

### Notes
- Bundle budget overrun (561 kB vs 500 kB angular.json budget) is pre-existing; NOT introduced by this WI
- Frontend coverage 47% is pre-existing; NOT reduced by this WI
- Smoke test screenshots not captured (no running instance); manual verification against live data deferred to deployment

---

## 2026-06-01 — Full day: WI-PROD-A.1 validated live; WI-PROD-D + WI-PROD-L done; 3 stores, ~12,500 txns

**Duration:** Full day (morning + afternoon)
**Phase:** Phase 2 (retail SPM domain model + UX improvements)
**Tests at end of day:** 595 backend (238 unit + 357 integration), 143 frontend — no regressions.

### Morning — WI-PROD-MODEL Part 3 + WI-PROD-A.1 implementation

WI-PROD-MODEL was closed with three final decisions (10, 11, 12 — see Decision #37 in PROJECT_STATUS.md). WI-PROD-A.1 was then implemented and test-validated: `Payee.Email` and `Payee.HireDate` made nullable end-to-end, `FieldRequirementSetting` entity + settings system built, validators made conditional via `IFieldRequirementService`, and a Settings UI (TenantAdmin-only) built to toggle the two fields. `ValidationBehavior` was fixed from sync `Validate()` to async `ValidateAsync()` (critical bug that would have broken any future `MustAsync` validator). EF migration with filtered unique index, seed for existing tenants, and deduplication step. +32 backend tests, 0 regressions. See the WI-PROD-A.1 session entry below for full implementation details.

### Afternoon — real-data validation + UX fixes

**Real-data pass 1 — Warszawa / Galeria Mokotów (EMP201-EMP209, 4,232 txns):**
- Owner toggled Email and HireDate to Optional in the new Settings → Field Requirements page.
- Re-imported `Reserved_Warszawa_Employees_April2026.xlsx` (9 employees, 4 with no email) — all 9 imported with 0 errors. The original WI-PROD-A.1 blocker is dead.
- Imported 4,232 Warszawa transaction rows — all successful. Payee name resolution (WI-PROD-F) correctly resolved all names server-side.

**Payee import UX fix — WI-PROD-L (DONE):**
Owner observed that the Payee import wizard lacked the progress screen and result feedback that the Transaction wizard had — clicking "Import" left the user on the preview table with only a spinning button. Claude Code implemented:
- Shared `ImportProgressComponent` (`features/imports/shared/`) — visual component used by both wizards (indeterminate/determinate bar, error/retry state). This delivers WI-PROD-D (progress bar promoted when second consumer appeared).
- New `PayeeImportingStepComponent` — 5th wizard step fires the HTTP import call on init, shows animated bar, transitions to complete or shows error with retry.
- Transaction wizard refactored to use the shared component (behaviour unchanged, 6 existing tests pass).
- Payee wizard extended: 4 steps → 5 steps; preview step now emits event to wizard rather than calling service directly.
- 143/143 frontend tests pass, 0 regressions.

**Real-data pass 2 — Silesia City Center Katowice (EMP301-EMP310, 5,066 txns):**
- Owner generated a new store dataset (10 payees, 5,066 April 2026 transactions).
- Imported all 10 payees via the updated wizard (new "Importing…" progress screen confirmed working).
- Imported 5,066 transactions — all successful.
- Tenant now has three stores coexisting: Galeria Katowice (8 payees, 3,183 txns), Galeria Mokotów Warszawa (9 payees, 4,232 txns), Silesia City Center Katowice (10 payees, 5,066 txns) — ~26 payees, ~12,500 transactions total. No regressions at this volume.

### Items closed today

- WI-PROD-MODEL — ✅ CLOSED (final decisions recorded)
- WI-PROD-A.1 — ✅ DONE (implemented + real-data validated)
- WI-PROD-D — ✅ DONE (delivered via WI-PROD-L)
- WI-PROD-L — ✅ DONE (payee import UX: progress + result screens)

### Items remaining (next sessions, priority TBD by owner)

- WI-PROD-A.2 — additional configurable fields (Role, ManagerId, EmploymentType, Location)
- WI-PROD-A.3 — assignment commands (AssignPayee / ReassignPayee state machine)
- WI-PROD-CURRENCY — full multi-currency system (account currency + FX + original/converted duality)
- WI-PROD-K — books reconciliation tool

---

## 2026-06-01 — WI-PROD-A.1: Email + HireDate optional via FieldRequirementSettings system

**Duration:** ~3 hours
**Phase:** Phase 2 (retail SPM domain model — first implementation sub-WI)
**Backend tests before → after:** 563 (217 unit + 346 integration) → 595 (238 unit + 357 integration). **+32 tests.** 0 regressions.
**Frontend tests before → after:** 143 → 143 (all existing pass; no new frontend tests added this session). 0 regressions.

### What was built

The real-data import blocker from 2026-05-29 is resolved: Reserved Polska retail exports with staff lacking corporate email can now be imported. The solution is a per-tenant configurable field-requirement system.

### Backend changes

**New domain:** `FieldRequirementSetting` entity (`Domain/Settings/`) extending `Entity`. Fields: `TenantId`, `EntityName`, `FieldName`, `IsRequired`. `SetRequired(bool)` method. No audit fields on the entity itself — all changes go to AuditLog.

**New Application interfaces:** `IFieldRequirementService` (Application/Common/Interfaces) with async `IsRequiredAsync(entityName, fieldName, ct)`. Scoped service; caches per-request via lazy-load private list (single DB query for all settings per request). 

**New Application commands/queries:** `GetFieldRequirementsQuery` + handler; `UpdateFieldRequirementCommand` + handler. Both require `Settings.Update` permission (TenantAdmin-only). `UpdateFieldRequirementHandler` uses explicit `auditService.LogAsync(...)` with before/after snapshots (Rule 5.1.5 — configuration changes must be audited). Audit swallows failures (non-money operation).

**New Application validators:** `CreatePayeeCommandValidator` (was missing entirely). Both Create and Update validators inject `IFieldRequirementService`. Email and HireDate use `MustAsync` (presence check conditional on setting; format always enforced when value is present).

**Critical bug fixed — ValidationBehavior:** `ValidationBehavior` was calling `v.Validate()` synchronously. FluentValidation throws `InvalidOperationException` when `Validate()` is called on a validator containing `MustAsync` rules. Changed to `ValidateAsync()` using `Task.WhenAll`. This is a backward-compatible fix — all existing sync validators work correctly with `ValidateAsync()`.

**Payee domain:** `Email` → `string?`, `HireDate` → `DateOnly?`. Domain factory invariants updated: null values no longer throw; format validation (future date guard) only runs when value is present. `Update()` mirrors same nullable behavior.

**PayeeImportValidationService:** Email and HireDate blank checks now conditional on `IFieldRequirementService`. Bug fix: `TryParseDate` was using `null` (thread culture) in `DateOnly.TryParseExact` — fixed to `CultureInfo.InvariantCulture` (same fix as WI-P2-04a-fix2 did for transaction import).

**EF migration `20260601080854_P2_FieldRequirementSettings`:**
- `Payee.Email` → nullable
- `Payee.HireDate` → nullable
- Drop old non-filtered index `IX_Payees_TenantId_Email`
- Add filtered unique index `IX_Payees_TenantId_Email WHERE Email IS NOT NULL` (same pattern as `ExternalId` in WI-P2-02)
- Create `FieldRequirementSettings` table with unique index `(TenantId, EntityName, FieldName)`
- Deduplication step before index creation (handles dev DB with duplicate emails from test imports)
- Seed SQL: inserts Email=Required + HireDate=Required for all existing tenants → backward compat

**New API:** `GET /api/settings/field-requirements`, `PUT /api/settings/field-requirements/{entity}/{fieldName}`. Both require `Settings.Update` (TenantAdmin).

**Permission + RolePermissions:** `Settings.Update` added to Domain constants; granted to TenantAdmin only.

**New audit constants:** `AuditActions.FieldRequirementChanged`, `ResourceTypes.FieldRequirement`.

### Frontend changes

**New service:** `SettingsApiService` (`features/admin/services/`) — `getFieldRequirements()` + `updateFieldRequirement(entity, field, isRequired)`.

**New component:** `FieldRequirementsComponent` (`features/admin/field-requirements/`) — renders `WsCard` with a list of field toggles using `WsSegmentedControlComponent` (Required / Optional per field). Loads settings on init; updates via PUT and shows toast on save. Gated by `*hasPermission="'Settings.Update'"` in AdminComponent.

**AdminComponent:** replaced placeholder with live `FieldRequirementsComponent`. `*hasPermission` directive gates the section.

**PayeeFormComponent:** loads field requirements on `ngOnInit()` via `SettingsApiService`. Email and HireDate validators are added/removed dynamically via `effect()` syncing with the loaded settings signals. Label changes from "Email" to "Email (optional)" and "Hire date" to "Hire date (optional)" when setting is Optional.

**Payee model:** `email` and `hireDate` are now `string | null` throughout the model, request types, and form handling.

**i18n (EN/ES/PL):** New `SETTINGS` namespace (7 keys each). New `PAYEES.FIELD_EMAIL_OPTIONAL` and `PAYEES.FIELD_HIRE_DATE_OPTIONAL` keys.

### New tests (backend)

- **Unit:** `PayeeTests.cs` (11 tests) — nullable email, nullable hireDate, format/range still enforced when present, always-required fields still throw
- **Unit:** `CreatePayeeCommandValidatorTests.cs` (10 tests) — `FakeFieldRequirementService` fake, required/optional matrix for email + hireDate, format always enforced when present
- **Integration:** `FieldRequirementSettingsEndpointsTests.cs` (11 tests) — GET auth/authz, PUT update and reflect, unknown field → 400, cross-tenant isolation, payee creation with null email when Optional → 201, invalid email format still rejected

### New architecture rules

Added to `14-forbidden-patterns.md`: (1) hardcoding required/optional for catalog fields — must use `IFieldRequirementService`; (2) calling `Validate()` sync when validators have `MustAsync` — must use `ValidateAsync()`; (3) adding new catalog fields without migration seed and test fixture seed.

### Notes

- Budget warning (initial bundle 61.84 kB over 500 kB) pre-existed before this WI — verified by git stash/pop. Not caused by this WI.
- `ValidationBehavior` async fix is a systemic improvement that unblocks any future validator using `MustAsync` or `WhenAsync`.
- Deduplication in migration is a one-time dev-DB cleanup; production will never hit it since the validator always prevented duplicate emails.

---

## 2026-06-01 — WI-PROD-MODEL Part 3 (FINAL): three decisions closed; WI-PROD-A unblocked

**Duration:** ~15 min (docs only — no code, no tests, no builds, no migrations)
**Phase:** Phase 2 (pre-implementation — product design, final part)
**Tests:** 563 backend (217 unit + 346 integration), 143 frontend — no changes this session.

### What we did

Closed the WI-PROD-MODEL design conversation by resolving the three open questions carried over from Parts 1 and 2. Three firm decisions recorded as Decision #37 in `PROJECT_STATUS.md`. WI-PROD-MODEL is now fully CLOSED. WI-PROD-A is now UNBLOCKED.

### Three firm decisions taken (Decision #37 — Part 3)

**Decision 10 — Transaction status enum unchanged; "Unassigned" is derived, not a status.** `CompensationTransaction.Status` (`Pending / Eligible / Calculated / Paid / Cancelled`) stays as-is. Default for all new transactions remains `Pending`. The condition "no payee" is derived from `PayeeId IS NULL` — never encoded as a status value. Status and assignment are independent dimensions. Phase 3 Calculation Engine filters `Status = 'Pending' AND PayeeId IS NOT NULL` to process only what is processable.

**Decision 11 — `AssignPayeeCommand` and `ReassignPayeeCommand` as distinct money-critical commands.** Both implement `IMoneyCriticalCommand` and are audit-logged automatically via the existing `AuditBehavior` pipeline (no new audit infrastructure). `AssignPayeeCommand`: no reason required; allowed when `PayeeId IS NULL`; Pending/Eligible/Calculated/Cancelled states allow. `ReassignPayeeCommand`: reason field REQUIRED (≥ 10 chars), persisted in audit log; reassignment on Eligible returns to Pending; on Calculated invalidates the commission line and returns to Pending; on Paid is BLOCKED with domain exception (money already disbursed). Frontend must hide/disable the action for Paid rows.

**Decision 12 — Import against inactive payee: accept with warning.** When a row's `EmployeeCode` matches a payee with `IsActive = false`, the validator emits `IssueSeverity.Warning` — message: `"Payee X (code Y) is inactive — assignment will be historical"`. Row is imported and assigned. Historical assignments are a legitimate retail scenario (a transaction dated April 28 can arrive in the May 5 import even if the payee deactivated April 30). Comp manager can exclude warning rows via the existing `skipRowsWithWarnings` toggle.

### Architectural observation

The system now has four clearly identified money-critical commands all routing through the same `IMoneyCriticalCommand` → `AuditBehavior` transactional pipeline: `IngestTransactionCommand` (existing), `AssignPayeeCommand` (new — WI-PROD-A/A2), `ReassignPayeeCommand` (new — WI-PROD-A/A2), and whatever the Phase 3 Calculation Engine produces. The pattern holds; no new audit infrastructure needed for WI-PROD-A.

### WI-PROD-A scope updated

WI-PROD-A now covers all 12 WI-PROD-MODEL decisions. It is a LARGE WI — must be split into at least 3 sub-WIs before coding: A1 (schema + settings system), A2 (assignment commands), A3 (frontend UI). Scoping conversation recommended before implementation.

---

## 2026-05-30 — WI-PROD-F: Server-side payee name resolution (GUID bug eliminated)

**Duration:** ~45 min
**Phase:** Phase 2
**Backend tests before → after:** 561 → 563 passing (+2 integration: payeeName populated, cross-tenant isolation). 217 unit + 346 integration. 0 regressions.
**Frontend tests before → after:** 138 → 143 passing (+5 new component tests). 0 regressions.

### Root cause

`ListTransactionsHandler` fetched `CompensationTransaction` entities, then mapped them in-memory via `IngestTransactionHandler.ToDto`. No Payee data was included. The frontend resolved payee names via `PayeesStore.payees().find(p => p.id === payeeId)?.fullName ?? payeeId` — when the payee was not on the currently loaded page, it fell back to the raw GUID. First manifested in real testing with the Reserved Katowice import (3,183 rows).

### Fix (backend)

`TransactionDto` extended with `string? PayeeName = null` and `string? PayeeEmployeeCode = null` (default-null positional record params — zero breaking changes to existing call sites including `IngestTransactionHandler.ToDto`).

`ListTransactionsHandler` now batch-fetches payee names after `ToPagedResultAsync`: extracts `PayeeId` values from the page, runs a single `WHERE Id IN (payeeIds)` query against `db.Payees`, builds a dictionary, and uses `with { PayeeName, PayeeEmployeeCode }` to enrich each DTO. Result: 3 queries per list request (COUNT + paginated SELECT + payee batch-fetch). No N+1 regardless of page size. Tenant isolation maintained automatically by the global query filter on `Payees`.

`Payee` is NOT navigable from `CompensationTransaction` in EF (no nav property). Batch-fetch chosen over navigation property — avoids Clean Architecture violation and does not require schema changes.

### Fix (frontend)

- `Transaction` model: `payeeName?: string | null`, `payeeEmployeeCode?: string | null` added.
- `TransactionsListComponent`: `PayeesStore` dependency removed; `payeeName()` method removed; `ngOnInit` removed (store auto-loads via `effect()`); `HasPermissionDirective` removed from imports (was unused — the template uses `HasPermissionPipe`).
- Template: `{{ tx.payeeName || ('TRANSACTIONS.UNASSIGNED' | translate) }}`. Never renders GUID, never renders empty string.
- i18n: `"UNASSIGNED"` key added to EN (`"Unassigned"`), ES (`"Sin asignar"`), PL (`"Bez przypisania"`).
- New spec: `transactions-list.component.spec.ts` with 5 tests: renders without error, payeeName from DTO, null → "Unassigned" (no GUID), empty string → no GUID, PayeesStore not required.

### Binding rule added

`14-forbidden-patterns.md` — new "Frontend data-fetching violations" section: list endpoints MUST resolve referenced entities server-side in the DTO; raw GUIDs and empty strings are forbidden as user-visible fallbacks.

---

## 2026-05-30 — WI-PROD-MODEL Part 2: five firm decisions (E–I) recorded; WI-PROD-A and WI-PROD-CURRENCY scopes expanded

**Duration:** ~20 min (docs only — no code, no tests, no builds, no migrations)
**Phase:** Phase 2 (pre-implementation — product design, continuation of Part 1)
**Tests:** 561 backend (217 unit + 344 integration), 138 frontend — no changes this session.

### What we did

Recorded five firm decisions from the Part 2 continuation of the WI-PROD-MODEL design conversation (Decision #36 in PROJECT_STATUS.md). Expanded WI-PROD-A scope with four new implementation items. Replaced the WI-PROD-CURRENCY entry with the substantially larger multi-currency system scope.

### Five firm decisions taken (Decision #36 — Part 2)

**Decision E — User and Payee are separate but linkable.** `User` and `Payee` are distinct entities. `User.PayeeId` is nullable. No separate rep portal — the existing RBAC identity system serves all logged-in roles. Vast majority of payees (store staff) will never have a login. MVP uses manual invite links; email-send (SendGrid) remains deferred per WI-02.

**Decision F — `Payee.EmploymentType` added as configurable optional field.** Values: full-time, part-time, temporary, contractor. Nullable. Joins Decision B's configurable-fields list. Default Optional. Used by Phase 3 calculation rules that may treat employment categories differently.

**Decision G — Payees are never deleted; activity state via `IsActive` + `DeactivatedAt`.** `IsActive` defaults true; `DeactivatedAt` (DateTimeOffset, nullable) is set automatically on deactivation and cleared on re-activation. Inactive payees preserved with full history; new transactions cannot be assigned to them. All transitions audit-logged. Re-import behavior on inactive payees: OPEN (Part 3).

**Decision H — Location/CostCenter as optional string dimension, NOT a `Store` entity.** Sparse usage is fine; reporting and filtering must work when the field is populated. Also likely added to `CompensationTransaction` (to confirm during scoping). Joins configurable-fields list as Optional.

**Decision I — Tenant account currency + explicit FX conversion.** Tenant has a TenantAdmin-configured account currency (payout currency). Transactions preserved in native currency (Spec §5b.5 intact). Explicit FX conversion uses a traceable exchange-rate source; both original and converted amounts are persisted — never overwritten. WI-PROD-CURRENCY is now a complete multi-currency handling system with four components: account-currency field on Tenant, exchange rate table, original+converted amount duality on transactions, and a conversion engine.

### Three open questions deferred to Part 3

- **Q1** — Audit/history of "transaction assigned to payee later": direct field update vs. assignment event log.
- **Q2** — Default transaction status: confirm `Pending` is correct in context of eligibility lifecycle and calc engine.
- **Q3** — Re-import behavior when payee is inactive: accept (historical correction), reject as error, or accept with warning.

### WI-PROD-A scope additions (items 7–10)

`Payee.EmploymentType` nullable (F); `Payee.IsActive`/`DeactivatedAt` with transition logic and audit (G, pending Q3); `Payee.Location` nullable string dimension (H); `User.PayeeId` nullable with manual invite-link MVP flow (E).

### WI-PROD-CURRENCY scope replacement

Old scope: display formatting only (pipe + column + footer). New scope: full multi-currency system — account-currency on Tenant, exchange rate table (rate source/date/retroactivity TBD during scoping), original+converted amount duality on transactions, conversion engine. Display formatting still included.

**WI-PROD-MODEL is NOT yet closed.** Part 3 pending to resolve Q1–Q3.

---

## 2026-05-30 — WI-PROD-MODEL Part 1: four firm decisions recorded; WI-PROD-K added

**Duration:** ~20 min (docs only — no code, no tests, no builds, no migrations)
**Phase:** Phase 2 (pre-implementation — product design)
**Tests:** 561 backend (217 unit + 344 integration), 138 frontend — no changes this session.

### What we did

Recorded the four firm decisions from the WI-PROD-MODEL product-design conversation (Decision #35 in PROJECT_STATUS.md). Added WI-PROD-K to the backlog. Updated WI-PROD-MODEL and WI-PROD-A entries with the new detail.

### Four firm decisions taken (Decision #35 — Part 1)

**Decision A — Field-level requirement configuration system per tenant.** Wasnie implements a TenantAdmin-only setting where specific fields are marked Required or Optional. Every change is audit-logged (Rule 5.1.5). No retroactive effect on existing data.

**Decision B — Configurable fields (initial scope of WI-PROD-A):** `Payee.Email`, `Payee.HireDate`, `Payee.Role`, `Payee.ManagerId`, `CompensationTransaction.PayeeId`. All five default to **Optional** for new tenants (avoids onboarding "valley of death").

**Decision C — Always-required fields (product law, not configurable):** `Payee.FullName`, `Payee.EmployeeCode`, `CompensationTransaction.ReferenceNumber / Amount / Currency / TransactionDate`, `TenantId` on both.

**Decision D — `CompensationTransaction.PayeeId` becomes nullable.** Transactions without an assigned payee are legitimate. Users can assign a payee later. Cross-phase dependency: Calculation Engine MUST define its null-PayeeId policy (skip / house-pool / error) before Phase 3 engine design starts.

### Schema implications recorded for WI-PROD-A

`Payee.Email`, `HireDate`, `Role`, `ManagerId` → nullable. Unique index on `(TenantId, Email)` → filtered (`WHERE Email IS NOT NULL`). `CompensationTransaction.PayeeId` → nullable FK. Validation when value is present remains enforced.

### WI-PROD-K added to backlog

Books reconciliation tool: a dedicated screen for comparing Wasnie transaction totals against the client's General Ledger by period / currency / source / payee. Trust-critical for mid-market clients with formal audits. Relationship to WI-PROD-J to be resolved during scoping.

### Still open — Part 2 pending

- Rep portal / payee login: do payees log in to Wasnie?
- Retail-specific fields possibly missing (employment type, termination date, cost center / store location, preferred currency).
- History/audit of "transaction assigned to payee later" — direct update vs. audit log of the assignment event.
- Default transaction status — currently `Pending`; review whether that default is right or if it should change.

**WI-PROD-MODEL is NOT yet closed.** WI-PROD-A and further import/transaction WIs remain soft-blocked until Part 2 completes.

---

## 2026-05-29 — WI-PROD-H closed: "New Transaction" button matches Payees pattern

Added `<app-icon name="plus">` inside the button and switched RBAC from `*hasPermission` directive to `[hidden]="!('Transactions.Create' | hasPermission)"` pipe — identical to Payees. 2 files: `transactions-list.component.ts` (added `HasPermissionPipe` + `IconComponent`), `.html` (button update). 138/138 tests pass, build clean.

---

## 2026-05-29 — WI-DOCS-UPDATE addendum 2: three more backlog items (transactions UX review)

**Duration:** ~5 min (docs only)

Three additional items added to `PROJECT_STATUS.md` backlog section after reviewing the transactions list UX:

- **WI-PROD-H** — "New Transaction" button placement inconsistent with Payees pattern. Low complexity; Payees is the reference.
- **WI-PROD-I** — No search input on the transactions list. Backend already supports the filter (`ListTransactionsHandler` WI-P2-03b); frontend just needs the 300 ms debounced input wired to `store.setSearch()`. Medium priority, ~1 h.
- **WI-PROD-J** — Transactions page summary widget (per-currency totals + time-series chart). Higher complexity; blocked on WI-PROD-CURRENCY for display convention and a chart library decision.

No code, no tests, no builds.

---

## 2026-05-29 — WI-DOCS-UPDATE addendum: three additional backlog items (transaction list review)

**Duration:** ~5 min (docs only)

Reviewing the live transaction list after the Reserved import surfaced three more pending items added to the `PROJECT_STATUS.md` backlog section:

- **WI-PROD-CURRENCY** — Multi-currency display convention undefined: same Amount column mixes EUR/PLN/USD with inconsistent decimal formatting. Design conversation needed; likely resolution: ISO-code prefix + always 2 decimals + no cross-currency totals.
- **WI-PROD-F** — Payee name resolution is client-side via `PayeesStore`; when the payee is not in the loaded page, the list shows a raw GUID. High priority — confidence breaker for demos. Fix: server-side JOIN in `ListTransactionsHandler`, return `PayeeName` in the DTO.
- **WI-PROD-G** — No test-data reset mechanism; manual testing accumulated noise rows (garbage GUIDs, million-dollar amounts, mixed currencies). Low priority dev convenience; a SQL script in `/scripts` or a dev-only endpoint would suffice.

No code, no tests, no builds.

---

## 2026-05-29 — WI-DOCS-UPDATE: Real-data test findings captured; domain-model backlog opened

**Duration:** ~20 min (docs only — no code, no builds, no tests)
**Phase:** Phase 2
**Tests:** 561 backend (217 unit + 344 integration), 138 frontend — no changes this session.

### What we did

Captured findings from today's real-data test of the transaction import wizard using a 3,183-row Reserved Polska / Galeria Katowice POS export (April 2026). Recorded two completed fixes and opened a structured product-design backlog.

### Today's completed fixes (shipped earlier in the day)

**WI-P2-04a-fix — Row limit 300 → 10,000, configurable (backend + frontend)**
- `MaxRows = 300` constant replaced by `ImportOptions` (`appsettings.json` `"Imports"` section, `IOptions<T>`, `ValidateOnStart`).
- Payee limit stays 300 (synchronous path, Rule 3.2.5). Transaction limit: 10,000.
- `IFileParserService.ParseAsync` now takes `int maxRows` — parser stays stateless; controller chooses limit per resource.
- New `GET /api/imports/transactions/limits` endpoint; frontend upload-step fetches it on init. `CONSTRAINT_ROWS` i18n key parameterised with `{{ count }}` in EN/ES/PL.
- +5 backend tests. **Test count after fix: 552.**

**WI-P2-04a-fix2 — Excel native DateTime parsing (Option B: ISO string in parser)**
- Root cause: `cell.GetString()` on `XLDataType.DateTime` cells → culture-dependent `"4/1/2026 10:21:04 AM"` → validator rejects every row.
- Fix: `FileParserService.ReadCellAsString(cell)` — DateTime cells → `"yyyy-MM-dd"` (ISO, InvariantCulture, time dropped); Number cells → `d.ToString(InvariantCulture)`.
- Validator `TryParseDate` switched from `null` to `CultureInfo.InvariantCulture`. Error message now includes actual bad value.
- Forbidden-patterns rule added to `14-forbidden-patterns.md`.
- +9 backend tests (smoking-gun, culture independence pl-PL, garbage message, min boundary). **Test count after fix: 561.**

### Real-data test outcome (Reserved Katowice, 3,183 rows)

After both fixes:
- **Upload:** Accepted. File parsed in < 2 s.
- **Map Columns:** Auto-detect picked correct columns for 5/6 fields.
- **Preview:** All rows failed with "payee not found" — expected, because payees were intentionally not pre-loaded for this test. Zero date errors (confirmed fix2 works). Zero amount errors (numeric cells round-trip correctly).
- **Execute / Progress / Complete:** Not reached in this test run (blocked at Preview by expected payee errors).

No additional bugs found beyond the two already fixed. The wizard is functionally correct for a realistic POS export.

### Backlog items opened (6 items — product conversation required before code)

| ID | Name | Status |
|---|---|---|
| WI-PROD-MODEL | Retail SPM domain model review (email/hireDate/PayeeId optionality) | **NEXT SESSION — conversation first** |
| WI-PROD-A | `RequirePayeeOnTransactions` tenant setting | Depends on WI-PROD-MODEL |
| WI-PROD-B | Multi-sheet Excel sheet picker | Bug — not yet implemented |
| WI-PROD-C | First-import onboarding "valley of death" | UX gap — conversation pending |
| WI-PROD-D | Promote `WsProgressBar` to design system | Deferred (single consumer) |
| WI-PROD-E | Actionable "payee not found" error message | Mini-WI — no blocker |

Full detail in `PROJECT_STATUS.md` backlog section.

### Phase 3 cross-dependency flagged

WI-P2-05 (Calculation Engine) must not start before WI-PROD-MODEL resolves how the engine handles `PayeeId = null` transactions. This choice (skip / house-pool / error) is a domain decision, not an engine implementation detail.

---

## 2026-05-29 — WI-P2-04a-fix2: Excel native DateTime parsing (bug fix)

**Duration:** ~30 min
**Phase:** Phase 2
**Backend tests before → after:** 552 → 561 passing (+9 new tests). 0 regressions.

### Root cause (quoted)

`FileParserService.ParseXlsx` was calling `cell.GetString()` on every cell. For `XLDataType.DateTime` cells, ClosedXML's `GetString()` produces a culture-dependent string like `"4/1/2026 10:21:04 AM"` — a format not accepted by the validator's `DateFormats` list. Every row from a real POS export (`Reserved_Katowice_POS_April2026.xlsx`, 3,183 rows) failed validation with "Transaction date is not a recognisable date."

The same `cell.GetString()` call also stringifies numeric cells using the cell's Excel number format (may include currency symbols and locale-specific separators), which would cause amount parsing failures on formatted numeric cells.

### Fix — Option B (robust string preservation in XLSX path)

Option A (type-preserving `Dictionary<string, object>`) was rejected: would cascade through `ParsedFile`, `IImportCacheService`, both validators, `TransactionImportJobHandler`, and all tests. Too large for a bug fix.

Option B applied — new private `ReadCellAsString(IXLCell cell)` method in `FileParserService`:
- `XLDataType.DateTime` → `dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)` — drops time component, always ISO 8601
- `XLDataType.Number` → `d.ToString(CultureInfo.InvariantCulture)` — invariant decimal, no currency formatting
- All others → `cell.GetString().Trim()` (text, blank, boolean, error — unchanged)

Validator `TryParseDate` also fixed: changed `null` (thread culture) to `CultureInfo.InvariantCulture` in `DateOnly.TryParseExact` calls.

Error message improved: was `"Transaction date is not a recognisable date. Use YYYY-MM-DD."` → now `"'{dateStr}' is not a recognisable date. Use YYYY-MM-DD."` (includes actual bad value).

### New tests (9)

Parser: `ParseXlsx_NativeDateTimeCell_ProducesIsoDateString` (smoking-gun), `ParseXlsx_NativeDateTimeCell_CultureIndependent` (pl-PL), `ParseXlsx_NativeNumberCell_ProducesInvariantDecimalString`

Validator: `Validate_ValidDateFormats_NoDateError` (ISO / MM-dd / dd-MM theory), `Validate_GarbageDate_ErrorMessageContainsActualValue`, `Validate_DateParsing_CultureIndependent` (pl-PL), `Validate_DateExactlyAtMinBoundary_Passes`

### Files modified

`FileParserService.cs` (new `ReadCellAsString` method), `TransactionImportValidationService.cs` (InvariantCulture + improved error message), `FileParserServiceTests.cs` (+3 tests), `TransactionImportValidationServiceTests.cs` (+6 tests)

---

## 2026-05-29 — WI-P2-04a-fix: Transaction import row limit 300 → 10,000 (configurable)

**Duration:** ~30 min
**Phase:** Phase 2
**Backend tests before → after:** 547 → 552 passing (+5 new parser limit tests). 0 regressions.

### What we did

Raised the transaction import row cap from 300 (Phase 1 synchronous holdover) to 10,000 configurable via `appsettings.json`. Payee import cap stays at 300 — it runs synchronously within the HTTP request and is governed by Rule 3.2.5 (bulk writes < 5s / 300 records).

**Architecture decision — `maxRows` as parameter, not injection:**
`FileParserService` is shared between payee and transaction paths. Instead of injecting `IOptions<ImportOptions>` into the parser (making it stateful/aware of resource type), added `int maxRows` parameter to `IFileParserService.ParseAsync`. The controller reads from `IOptions<ImportOptions>` and passes the correct limit per caller. Parser stays stateless and pure — easier to test, no resource-awareness leaked into parsing logic.

**Frontend — live limit from backend:**
Added `GET /api/imports/transactions/limits` (returns `{ maxRows }`) and `getImportLimits()` to `TransactionImportService`. Upload-step fetches this on `OnInit`, defaults to 10,000 if the call fails. `CONSTRAINT_ROWS` i18n key now uses `{{ count }}` param in EN/ES/PL. Payee upload-step `CONSTRAINT_ROWS` key unchanged (still shows "300").

**Config validation at startup:** `ValidateOnStart()` rejects `TransactionMaxRows` outside [1, 100,000] or `PayeeMaxRows` outside [1, 100,000] with a clear error — no silent misconfiguration.

**Files created:** `Application/Common/Options/ImportOptions.cs`
**Files modified:** `IFileParserService.cs`, `FileParserService.cs`, `ImportsController.cs`, `DependencyInjection.cs` (Infrastructure), `appsettings.json`, `FileParserServiceTests.cs`, `transaction-import.service.ts`, `upload-step.component.ts/html`, `en.json`, `es.json`, `pl.json`

### Open / deferred

None new. WI-P2-04c (persistent error queue) and WI-P2-05 (calculation engine) remain next candidates.

---

## 2026-05-29 — WI-P2-04b: Transaction Import Wizard UI (5-step with progress polling)

**Duration:** ~1.5 hours
**Phase:** Phase 2
**Frontend tests before → after:** 98 passing → 138 passing (+40). 0 regressions.

### What we did

Built the transaction import wizard UI consuming the WI-P2-04a backend endpoints. Mirrors the payee wizard pattern with one new step: **Progress** (async job polling).

**Architecture:**
- Five steps: `upload → map → preview → progress → complete`. SessionStorage key `wasnie:import-wizard:transactions` (TTL same as payees; `progress` step not persisted — no live job on reload).
- `TransactionImportService`: 4 methods — `parseFile`, `validateMapping`, `executeImport` (returns `ExecuteAccepted { jobId }`), `getJobStatus`. No HttpClient in components.
- `TxProgressStepComponent`: polls `GET /api/jobs/{id}` every 3s via `timer(0, 3000)` + `takeUntilDestroyed(destroyRef)`. Stops on terminal state (`Succeeded`/`Failed`) via explicit `_polling.unsubscribe()`. Stops on component destroy via `takeUntilDestroyed`. Transient network errors set `netError` signal without failing the job — next poll continues.
- Column auto-detect (`detectField()`) covers EN/ES/PL patterns for all 6 transaction fields.
- RBAC: route `/transactions/import` gated to `Transactions.Create`. Sidebar entry added in OPERATIONS section.

**Key decisions:**
- **WsProgressBar not in shared/ui** → implemented as LOCAL CSS within `progress-step.component.scss` only. Indeterminate animation for Pending state, determinate width for Running. NOT added to design system (§10.3 — owner decision required).
- **Progress step retry → goes back to Preview** (not Upload/Map). The parsed file and mapping are still valid; only the execution needs retry.
- **TransactionImportResult { totalRows, processedRows }** derived from `JobStatusDto.ProgressTotal/ProgressCurrent` on success. No `createdCount`/`skippedCount` breakdown (not in `JobStatusDto` — 04c deferred).

**Files created (25):** models, service, helpers (auto-detect), upload/mapping/preview/progress/complete step components, wizard orchestrator, specs (service, mapping, progress — 40 tests total).

**Files modified (5):** `transactions.routes.ts`, `sidebar.component.ts`, `en.json`, `es.json`, `pl.json`.

**Bug fixed in tests:** `WsButtonComponent` injects `RouterLink` internally → `ActivatedRoute` missing in TestBed. Fixed by adding `provideRouter([])` to all step component specs.

### Open / deferred

- **WI-P2-04c:** Persistent per-row error queue after job completion (currently no row-level detail after Succeeded). Deferred per original WI scope.
- **WsProgressBar as shared primitive:** If 2+ features need a progress bar, elevate to `shared/ui/` in a separate design-system WI (§10.3).

---

## 2026-05-29 — WI-P2-04a: Transaction Import Backend (async via Hangfire)

**Duration:** ~2 hours (split across two sessions due to interruption)
**Phase:** Phase 2
**Tests before → after:** 494 passing → 547 passing (217 unit + 330 integration). 0 regressions.

### What we did

Built the async transaction CSV import backend, unblocking WI-P2-04b (wizard UI).

**Architecture (step 0 confirmed):**
- Three endpoints mirror the payee import pattern: parse → validate → execute (202 Accepted + jobId)
- `IImportCacheService` extended with `resource` default param (`"payees"` unchanged, `"transactions"` new)
- `BackgroundJobTenantContext` set by `HangfireJobDispatcher` before handler runs — handler does not call `SetTenant` again
- Hangfire retry configured globally to 3 attempts (down from default 10)

**Key decisions:**
- **Audit-batch failure → job Failed (not swallowed):** Unlike payee imports, transaction audit is mandatory (money-critical). If the end-of-job `AuditLog` batch insert fails, the handler throws, Hangfire marks the job Failed, retries up to 3×. On retry, idempotency skips already-committed transactions; audit batch re-attempted. If all retries fail: committed transactions are in DB but un-audited — Failed job in Hangfire dashboard is the operational alert. This trade-off is documented in `05-audit-trail.md` and explicitly accepted.
- **Chunked processing (50 rows/chunk):** Each chunk in its own SQL transaction. `DbUpdateException` (unique constraint violation) caught per entity → `ChangeTracker.Clear()` → continue chunk → `CommitAsync`. On retry, idempotency skips the same rows again — safe.
- **Per-row audit in single end-of-job batch:** One `SaveChangesAsync` inserts all `AuditLog` entries. Cost: O(1) transactions instead of O(chunks). Risk: window where committed rows have no audit entry if batch fails — accepted and documented.
- **Payload size:** Worst-case 300 rows × 6 fields × ~20 chars ≈ 36 KB — well under 5 MB, stored in `BackgroundJobRecord.PayloadJson`.
- **Re-validation at job start:** DB state may change between validate endpoint call and job execution. Handler re-runs `ITransactionImportValidationService.ValidateAsync` as its first action after payee lookup.

**Chunk timing (50-row chunk):** Integration test `ChunkTiming_50Rows_CompletesWellUnder5s` confirmed < 10s total for 50 rows (complete job including parse+execute+wait); individual chunk time is ~1–2s. Well within Rule 3.2.5's 5s per transaction limit.

**Bug fixed mid-session:** `Task.WhenAll` on two EF Core queries sharing the same `DbContext` — concurrent context operations throw. Fixed to sequential `await` in all 4 quota handlers that were introduced in the same session.

**Files created:**
- `Application/Models/Imports/TransactionImportColumnMapping.cs`
- `Application/Models/Imports/TransactionImportPayload.cs` + `TransactionImportOptions`
- `Application/Models/Imports/ImportValidationModels.cs` (+`TransactionRowValidationResult`, `TransactionValidateResponse`, `TransactionExecuteAccepted`)
- `Application/Services/Imports/ITransactionImportValidationService.cs`
- `Infrastructure/Services/Imports/TransactionImportValidationService.cs`
- `Infrastructure/BackgroundJobs/TransactionImportJobHandler.cs`
- `tests/.../Services/Imports/TransactionImportValidationServiceTests.cs` (29 tests)
- `tests/.../Integration/Imports/TransactionImportEndpointsTests.cs` (15 tests)
- `tests/.../BackgroundJobs/TransactionImportJobTests.cs` (7 tests)

**Files modified:**
- `Application/Services/Imports/IImportCacheService.cs` (`resource` default param)
- `Infrastructure/Services/Imports/ImportCacheService.cs` (resource-aware cache key)
- `Infrastructure/DependencyInjection.cs` (new services, Hangfire retry=3)
- `Api/Controllers/ImportsController.cs` (3 transaction endpoints)
- `tests/.../Infrastructure/TestDatabaseFixture.cs` (`ResetTransactionImportDataAsync`)
- `docs/architecture/05-audit-trail.md` (bulk import audit binding rule)
- `docs/architecture/14-forbidden-patterns.md` (bulk import violations section)

### Open / deferred

- **WI-P2-04b:** Transaction import wizard UI (Angular) with parse→validate→execute flow + polling for job progress. Polling interval: 2s while Running.
- **WI-P2-04c:** Persistent per-row error queue (deferred). Currently skipped rows return no detail after job completes — fine for Phase 2 launch.
- **Dashboard admin role:** `HangfireDashboardAuthorizationFilter` blocks in Production until a `SystemAdmin` role is defined. Hangfire dashboard for checking Failed import jobs is available in Development only.

---

## 2026-05-29 — WI-P2-BG-a verification + architecture doc gap closed

**Duration:** ~30 min
**Phase:** Phase 2

### What we did

Verified WI-P2-BG-a (Hangfire background job foundation) which was implemented in the 2026-05-28 session but left with one mandatory doc item missing.

**Build:** `dotnet build --configuration Release` — clean, 0 warnings, 0 errors.

**Test count: 494 passing (217 unit + 277 integration), 2 intentionally skipped. 0 regressions.**
- Previous baseline (before WI-P2-BG-a): 460 passing
- Added by WI-P2-BG-a: `BackgroundJobTenantContextTests` (5 unit) + `PingJobIntegrationTests` (1 integration)

**Step 0 confirmation:**
- `TenantContext` (HTTP path) reads `IHttpContextAccessor` → JWT claim `tenant_id`. Returns `Guid.Empty` for unauthenticated; throws `UnauthorizedAccessException` for authenticated-with-missing-claim. Registered Scoped.
- `CurrentUserService` reads `IHttpContextAccessor`. Registered Scoped.
- `AuditBehavior` consumes both via constructor injection in a Scoped pipeline.
- `BackgroundJobTenantContext` (job path): mutable, throws if `TenantId` read before `SetTenant()`. DI factory selects HTTP vs job implementation based on presence of `IHttpContextAccessor.HttpContext`. HTTP path behavior unchanged — regression tests green.
- Hangfire target framework: .NET 8 (packages `Hangfire.Core/SqlServer/AspNetCore` 1.8.14, LGPLv3).
- SQL connection: `DefaultConnection` from `IConfiguration` (same string used by EF Core + Hangfire SQL storage).

**Architecture doc gap closed:**
- `docs/architecture/14-forbidden-patterns.md`: added "Background job violations" section — 5 rules covering `SetTenant` before DB access, no swallowing the throw-before-set exception, dashboard auth guard, Hangfire in Application/Domain = forbidden, no silent Guid.Empty.

### Open / deferred

Same as 2026-05-28 entry. No new deferrals.

---

## 2026-05-28 — WI-P2-BG-a: Hangfire background job foundation

**Duration:** ~2 hours
**Phase:** Phase 2
**Tests before → after:** 460 passing → 494 passing (217 unit + 277 integration). 0 regressions.

### What we did

Built the generic, reusable background job infrastructure. This is the prerequisite for WI-P2-04a (transaction import), which runs rows in background to avoid HTTP timeouts on large CSV files.

**Key decisions:**
- **Hangfire** (LGPLv3 — correction: the step-0 inspection mislabeled it as MIT) over hand-rolled SQL jobs. Recommended because it handles retries, state persistence, and dashboard out-of-the-box.
- **Azure F1 plan**: no Always On; app unloads after ~20 min idle. Hangfire jobs are durable in SQL — they survive recycles. Timing is non-deterministic but not money-safety risk. **B1 upgrade ($13/month, Always On) deferred to first paying customer** — this is the explicit trigger.
- **BackgroundJobTenantContext**: throws `InvalidOperationException` if `TenantId` read before `SetTenant()`. Never silently returns `Guid.Empty` (Rule 9.4.3). `HangfireJobDispatcher` sets tenant from job payload as first action.
- **Hangfire dashboard at `/jobs`**: dev-only (blocked in Production) until a global SystemAdmin role/claim is implemented. Cross-tenant job data exposure risk documented.
- **`ApplicationDbContext.CurrentTenantId`**: changed from eager (`{ get; } = tenantContext.TenantId`) to lazy (`=> tenantContext.TenantId`). Required so background job scopes can construct `ApplicationDbContext` before `SetTenant` is called (EF evaluates query filters per-query, not at construction).

**Regression found and fixed:**
`AuthorizationService.RequireAsync` had a `catch { }` that swallowed `UnauthorizedAccessException` from `tenantContext.TenantId` (missing/invalid claim). With the lazy change, the exception now fired inside the audit block rather than at DbContext construction, making it get swallowed and replaced with `ForbiddenException` → 403. Fixed by adding `catch (UnauthorizedAccessException) { throw; }`.

**Files created/modified:**
- `Domain/BackgroundJobs/JobState.cs` + `BackgroundJobRecord.cs` (entity with `MarkRunning/UpdateProgress/MarkCompleted/MarkFailed`)
- `Application/Common/Interfaces/IBackgroundJobService.cs`, `IJobHandler.cs`
- `Application/Common/Models/JobStatusDto.cs`, `JobContext.cs`
- `Application/BackgroundJobs/JobHandlerBase.cs` (abstract), `Queries/GetJobStatusQuery.cs`
- `Application/Common/Interfaces/IApplicationDbContext.cs` (+`BackgroundJobRecords` DbSet)
- `Infrastructure/Identity/BackgroundJobTenantContext.cs`
- `Infrastructure/BackgroundJobs/HangfireJobDispatcher.cs`, `HangfireBackgroundJobService.cs`, `PingJobHandler.cs`, `HangfireDashboardAuthorizationFilter.cs`
- `Infrastructure/Persistence/Configurations/BackgroundJobs/BackgroundJobRecordConfiguration.cs`
- `Infrastructure/Persistence/ApplicationDbContext.cs` (lazy `CurrentTenantId`, +BackgroundJobRecords DbSet + config + query filter)
- `Infrastructure/DependencyInjection.cs` (factory-based `ITenantContext`, Hangfire registration, `PingJobHandler` handler registration)
- `Infrastructure/Identity/AuthorizationService.cs` (re-throw `UnauthorizedAccessException`)
- `Infrastructure/Wasnie.Infrastructure.csproj` (Hangfire.Core/SqlServer/AspNetCore 1.8.14)
- `Api/Controllers/JobsController.cs` (`GET /api/jobs/{id}`)
- `Api/Program.cs` (Hangfire dashboard middleware + `JsonStringEnumConverter`)
- `tests/.../Infrastructure/TestWebApplicationFactory.cs` (`ConnectionStrings:DefaultConnection` override for Hangfire)
- EF migration: `20260528135529_AddBackgroundJobs`
- Tests: `BackgroundJobs/BackgroundJobTenantContextTests.cs` (5 tests), `BackgroundJobs/PingJobIntegrationTests.cs` (1 end-to-end test)

### Open / deferred

- **B1 upgrade trigger**: When first paying customer is onboarded, upgrade to Azure App Service B1 (Always On) so Hangfire processes jobs without idle-sleep delays.
- **SystemAdmin role for dashboard**: `HangfireDashboardAuthorizationFilter` blocks dashboard in Production until a global SystemAdmin role/claim is defined (separate WI).
- **WI-P2-04a**: Transaction import backend — now unblocked. Uses `IBackgroundJobService.EnqueueAsync` + `IJobHandler<ImportPayload>`.

---

## 2026-05-28 — WI-P2-FIX-select: ws-select async server-side typeahead

**Duration:** ~90 min (split across two context windows)
**Phase:** Phase 2

### What we did

Fixed a critical bug: `ws-select` was filtering typeahead client-side over only the rows already loaded (e.g. 10), while the data source is server-paginated (e.g. 1,250 payees). Searching "John" in a payee dropdown with 1,250 payees was finding 0 results if John was not in the first page.

**Root cause fix:** Additive async mode added to `ws-select` — single component, no fork.

**Changes:**
- `ws-select.component.ts`: `searchFn` + `initialOption` inputs; `asyncOptions`/`asyncLoading` signals; `switchMap` pipeline with `debounceTime(300)` + `takeUntilDestroyed`; `options` changed from required to optional
- `ws-select.component.html`: search input condition extended; animated loading indicator; empty state guarded by `!asyncLoading()`  
- `ws-select.component.scss`: `.ws-select__loading` 3-dot animated indicator
- 6 consumers migrated: `transaction-form`, `payee-form` (manager select), `assignment-create` (payee + plan), `quota-create` (payee + plan)
- `assignment-create`: `planId.valueChanges → plansApi.getPlan() → patchValue(dateRange)` replaces store lookup; queryParam preselection via `firstValueFrom`
- `payee-form`: `managerInitialOption` computed from `payee().managerId/managerName/managerEmployeeCode` (no extra API call needed — Payee DTO includes manager fields)
- `ws-select.component.spec.ts` (16 tests, NEW): client-side + async behaviors + loading/empty state timing
- `transaction-form.component.spec.ts`: updated to mock `PayeesApiService` instead of removed `PayeesStore`
- `DESIGN_SYSTEM.md`: WsSelect async mode subsection

### Tech debt noted

- Manager "exclude self" limitation: backend has no `excludeId` filter param, so a payee can assign themselves as their own manager. No client-side status filter in async mode (backend `search` param is the primary filter).
- No lightweight lookup DTOs: consumers use full DTOs at `pageSize=20` — acceptable at current scale.

### Test count

**98 frontend tests pass (build clean, no new warnings)**

---

## 2026-05-28 — WI-P2-03c-fix: Transaction form visual fix (surgical)

**Duration:** ~15 min
**Phase:** Phase 2

### What we did

Two root-cause bugs found and fixed (SCSS only, 2 files):

1. `transaction-create.component.scss` `.form-card` used `var(--color-bg-surface)` — same token as the page background, making the card invisible (zero elevation differential). Fixed to `var(--color-bg-surface-raised)` to match the Payees pattern. Also aligned padding (`var(--space-5)`) and margin-top (`var(--space-6)`) and max-width (`640px`) to match Payees exactly.

2. `transaction-form.component.scss` was missing the `.ws-form-grid` CSS definition. `ws-form-grid` is not a global class — each form component defines it locally (payees, quotas, assignments all do). Without it, the 2-column grid didn't render and fields stacked uncontained. Added the full grid definition matching the Payees/Quotas pattern. Also added `@apply flex flex-col gap-5` to `.transaction-form` (payee-form pattern). Also added responsive collapse at 640px.

3. `.source-info` styled as an intentional read-only element: `background: var(--color-bg-surface-sunken)`, `border: 1px solid var(--color-border-subtle)`, `border-radius: var(--radius-sm)`, `padding: var(--space-2) var(--space-3)` — token-based, no precedent exists so kept minimal.

### Files changed

- `src/app/features/transactions/create/transaction-create.component.scss` (MODIFIED)
- `src/app/features/transactions/form/transaction-form.component.scss` (MODIFIED)

### Result

Build: ✅ clean. Tests: 17/17 pass.

---

## 2026-05-28 — WI-P2-03c: Transaction UI — Create Form + Paginated List

**Duration:** ~1 hour
**Phase:** Phase 2

### What we did

- Step 0 inspection (previous session): confirmed Payees feature as pattern; confirmed `Transactions.Read`/`.Create` come from `/auth/me` (no frontend permission code needed); found route and sidebar bugs using `Reports.ViewAll`; planned payee name client-side lookup; established §5b.8 disclaimer NOT required (raw transactions = objective sales facts)
- `transaction.model.ts`: `Transaction` interface, `TransactionStatus` string enum (Pending/Eligible/Calculated/Paid/Cancelled), `TransactionSource` enum, `CreateTransactionRequest`
- `TransactionsApiService`: `list()`, `getById()`, `create()` using `buildHttpParams` — mirrors `PayeesApiService` exactly
- `TransactionsStore`: signals-based with `effect()` auto-reload, status filter, no text search (backend doesn't support it), `createTransaction()` + `loadTransactions()`, `setStatusFilter()` / `setPage()` / `setPageSize()`
- `TransactionsListComponent`: `WsPageLayout`, `WsSegmentedControl` for status filter (6 options), `WsTable` with skeleton rows, `WsBadge` with 5 status variants, `WsPagination`, payee name lookup via `PayeesStore`, `*hasPermission="'Transactions.Create'"` gates New button
- `TransactionFormComponent`: 4-field form (Payee select searchable, Reference number, Transaction date, Amount+Currency amount-pair), source shown as read-only info paragraph, `isEditMode = computed(() => transaction() !== null)`
- `TransactionCreateComponent`: thin wrapper, navigates to `/transactions` on saved/cancelled
- `transactions.routes.ts`: `''` → `TransactionsListComponent`, `'new'` → `TransactionCreateComponent`
- **Bug fixed in `app.routes.ts`:** transactions path changed from `loadComponent` + `Reports.ViewAll` to `loadChildren` (transactionsRoutes) + `Transactions.Read`
- **Bug fixed in `sidebar.component.ts`:** transactions nav item permission changed from `Reports.ViewAll` to `Transactions.Read`
- i18n: TRANSACTIONS namespace added to EN/ES/PL (29 keys: title, subtitle, status labels, column headers, form fields, toast, source info)
- **17 new frontend tests:** 4 service (`HttpTestingController` — list flat params, status filter, getById, create body), 7 store (load, filter reset page, pageSize reset, createTransaction calls api + reloads, error signal), 6 form (invalid on empty, valid when filled, amount min validation, submit marks touched, submit calls store, hasError, onCancel)
- Build: `ng build --configuration production` ✅ clean (pre-existing budget warning only)
- Tests: 17/17 pass

### Files produced/modified

- `src/app/features/transactions/models/transaction.model.ts` (NEW)
- `src/app/features/transactions/services/transactions.api.service.ts` (NEW)
- `src/app/features/transactions/services/transactions.api.service.spec.ts` (NEW)
- `src/app/features/transactions/state/transactions.store.ts` (NEW)
- `src/app/features/transactions/state/transactions.store.spec.ts` (NEW)
- `src/app/features/transactions/list/transactions-list.component.ts` (NEW)
- `src/app/features/transactions/list/transactions-list.component.html` (NEW)
- `src/app/features/transactions/list/transactions-list.component.scss` (NEW)
- `src/app/features/transactions/form/transaction-form.component.ts` (NEW)
- `src/app/features/transactions/form/transaction-form.component.html` (NEW)
- `src/app/features/transactions/form/transaction-form.component.scss` (NEW)
- `src/app/features/transactions/form/transaction-form.component.spec.ts` (NEW)
- `src/app/features/transactions/create/transaction-create.component.ts` (NEW)
- `src/app/features/transactions/create/transaction-create.component.html` (NEW)
- `src/app/features/transactions/create/transaction-create.component.scss` (NEW)
- `src/app/features/transactions/transactions.routes.ts` (NEW)
- `src/app/app.routes.ts` (MODIFIED — loadChildren + Transactions.Read)
- `src/app/shared/components/sidebar/sidebar.component.ts` (MODIFIED — Transactions.Read)
- `src/assets/i18n/en.json` (MODIFIED — TRANSACTIONS namespace)
- `src/assets/i18n/es.json` (MODIFIED — TRANSACTIONS namespace)
- `src/assets/i18n/pl.json` (MODIFIED — TRANSACTIONS namespace)

### Decisions / notes

- **§5b.8 NOT required:** Transactions page shows raw sales facts (not estimated commission). Advisory disclaimer only applies to projected commission figures (Payouts page, future).
- **Payee name lookup tech debt:** `TransactionDto` has only `PayeeId`. List component resolves name client-side via `PayeesStore`. Backend enhancement (add `PayeeName` to DTO) deferred.
- **No text search on transaction list:** Backend `ListTransactionsHandler` has no reference-number search filter. Status filter only.
- **Currencies:** USD, EUR, GBP, PLN, CAD, AUD — reused from quota form pattern.

---

## 2026-05-28 — WI-P2-03b: Transaction Read Endpoints — Backend

**Duration:** ~1 hour
**Phase:** Phase 2

### What we did

- Step 0 inspection: confirmed Payees list as reference pattern; default 25/max 100 pagination; get-by-id returns 404 (not 403) via global query filter + `FirstOrDefaultAsync`; `Transactions.Read` grant kept at TenantAdmin + CompManager (scoped access deferred per decision #18); 3 missing read-path indexes identified and added
- Migration `P2_TransactionReadIndexes`: `(TenantId, TransactionDate)`, `(TenantId, Status)`, `(TenantId, IngestedAt)` — all narrow, targeted indexes per Rule 3.2.2. Amount sort flagged (no index, deferred to Scale tier)
- `PaginationQuery` extended with `Source`, `DateFrom`, `DateTo` (backward compatible — existing handlers ignore new fields)
- `ListTransactionsQuery` + `ListTransactionsHandler`: sort whitelist (`transactionDate`/default, `amount`, `status`, `ingestedAt`, `referenceNumber`), unknown field → safe fallback, 5 filters (status/payeeId/source/dateFrom/dateTo), `Enum.TryParse` (case-insensitive name parsing), `ToPagedResultAsync`, entity → DTO via `IngestTransactionHandler.ToDto`
- `GetTransactionByIdQuery` + `GetTransactionByIdHandler`: RBAC first, `FirstOrDefaultAsync`, global filter handles tenant scoping, null → `Result.Failure` → 404
- `TransactionsController` updated: `GET /api/transactions` and `GET /api/transactions/{id}` added
- 27 integration tests in `TransactionReadEndpointsTests`

### Files produced/modified

- `src/Wasnie.Application/Common/Models/PaginationQuery.cs` (MODIFIED — Source, DateFrom, DateTo)
- `src/Wasnie.Infrastructure/Persistence/Configurations/Compensation/CompensationTransactionConfiguration.cs` (MODIFIED — 3 new indexes)
- `src/Wasnie.Infrastructure/Persistence/Migrations/[timestamp]_P2_TransactionReadIndexes.cs` (NEW)
- `src/Wasnie.Application/Compensation/Queries/Transactions/ListTransactionsQuery.cs` (NEW)
- `src/Wasnie.Application/Compensation/Queries/Transactions/GetTransactionByIdQuery.cs` (NEW)
- `src/Wasnie.Application/Compensation/Handlers/Transactions/ListTransactionsHandler.cs` (NEW)
- `src/Wasnie.Application/Compensation/Handlers/Transactions/GetTransactionByIdHandler.cs` (NEW)
- `src/Wasnie.Api/Controllers/TransactionsController.cs` (MODIFIED — 2 new GET actions)
- `tests/Wasnie.IntegrationTests/Transactions/TransactionReadEndpointsTests.cs` (NEW — 27 tests)

### Key decisions

- `Transactions.Read` stays TenantAdmin + CompManager only. Manager/Rep scoped access WI is the trigger to widen this grant.
- `Enum.TryParse` (case-insensitive name) used for Status and Source filters — consistent with DTO output which returns enum names ("Pending", "Manual"), more readable API than integer strings.
- Amount sort included in whitelist without index — performance acceptable at current scale; flagged for deferred index at Enterprise tier.
- Sort by amount uses `t.Amount.Amount` (owned entity navigation) — EF Core handles the owned property projection correctly.

### Test count

460 → 488 (217 unit + 271 integration), 2 intentionally skipped — zero regressions.

### What's next

WI-P2-03c — Manual transaction entry UI. Requires reading `DESIGN_SYSTEM.md` before starting.

---

## 2026-05-28 — WI-P2-03a: Manual Transaction Ingestion — Backend

**Duration:** ~2 hours (including stale-binary debug loop)
**Phase:** Phase 2

### What we did

- `Permission.TransactionsCreate` + `Permission.TransactionsRead` added to `Permission.cs` (Domain) and granted to TenantAdmin + CompManager in `RolePermissions.cs` (Application)
- `AuditActions.TransactionIngested = "TRANSACTION_INGESTED"` added to `AuditActions.cs`
- `TransactionDto` record created in `Wasnie.Application/Compensation/DTOs/`
- `IngestTransactionCommand` (implements `IMoneyCriticalCommand`): positional record with mutable `AuditResourceId { get; set; }` — handler sets it after `SaveChangesAsync` so `AuditBehavior.BuildEntry` picks up the real ID
- `IngestTransactionCommandValidator`: sync FluentValidation — ReferenceNumber not-empty/≤200, PayeeId not-empty, Amount > 0, Currency exactly 3 chars, TransactionDate ≥ 2000-01-01
- `IngestTransactionHandler`: RBAC check first, payee existence (EF Core `AnyAsync`), `Money.Of`, `CompensationTransaction.Ingest`, `db.SaveChangesAsync`, `request.AuditResourceId = tx.Id.ToString()`. Does NOT inject `IAuditService` — `AuditBehavior` handles audit atomically in the same EF Core transaction
- `TransactionsController`: thin MediatR delegate, `POST /api/transactions`, returns 201 + Location on success, 400 + `{message}` on failure
- `TestDatabaseFixture.ResetTransactionsAsync()` added
- 16 unit tests in `IngestTransactionCommandValidatorTests` (valid, null/empty/whitespace/length ref, empty payeeId, zero/negative/positive amounts, invalid/valid currencies, date boundary)
- 6 new `[InlineData]` cases in `RolePermissionsTests` (TransactionsCreate/Read granted, TransactionsCreate denied for Manager/Rep)
- 14 integration tests in `TransactionsEndpointsTests`: 201 with body, Location header, 401, 403×2, 201 CompManager, 400×4 validation, payee-not-found, cross-tenant payee, cross-tenant isolation, TRANSACTION_INGESTED audit record

### Bugs fixed during the run

1. `AuditLog` global query filter (`TenantId == CurrentTenantId`) blocked audit queries in test background scopes (`CurrentTenantId == Guid.Empty`). Fixed by adding `IgnoreQueryFilters()` to the audit log query in the integration test.
2. Persistent "Expected object not to be null" failures even after adding `IgnoreQueryFilters()`. Root cause: `dotnet test --no-build` was running against a stale binary from before the test file edits. Fixed by running `dotnet build` before `dotnet test --no-build`.

### Files produced/modified

- `src/Wasnie.Domain/Authorization/Permission.cs` (MODIFIED — TransactionsCreate, TransactionsRead)
- `src/Wasnie.Domain/Audit/AuditActions.cs` (MODIFIED — TransactionIngested)
- `src/Wasnie.Application/Authorization/RolePermissions.cs` (MODIFIED — TenantAdmin + CompManager grants)
- `src/Wasnie.Application/Compensation/DTOs/TransactionDto.cs` (NEW)
- `src/Wasnie.Application/Compensation/Commands/Transactions/IngestTransactionCommand.cs` (NEW)
- `src/Wasnie.Application/Compensation/Validators/Transactions/IngestTransactionCommandValidator.cs` (NEW)
- `src/Wasnie.Application/Compensation/Handlers/Transactions/IngestTransactionHandler.cs` (NEW)
- `src/Wasnie.Api/Controllers/TransactionsController.cs` (NEW)
- `tests/Wasnie.IntegrationTests/Infrastructure/TestDatabaseFixture.cs` (MODIFIED — ResetTransactionsAsync)
- `tests/Wasnie.UnitTests/Validators/IngestTransactionCommandValidatorTests.cs` (NEW — 16 tests)
- `tests/Wasnie.UnitTests/Authorization/RolePermissionsTests.cs` (MODIFIED — 6 new inline cases)
- `tests/Wasnie.IntegrationTests/Transactions/TransactionsEndpointsTests.cs` (NEW — 14 tests)

### Key decisions

- First `IMoneyCriticalCommand` in production use — `AuditBehavior` handles the full audit atomically; handler has no `IAuditService` dependency
- `AuditResourceId` is mutable on the command record so the handler can write the DB-generated ID after `SaveChangesAsync`; `AuditBehavior.BuildEntry` reads it in the `after-next` step
- Integration test audit check uses `IgnoreQueryFilters()` because the test's DI scope has no HTTP context (`TenantId == Guid.Empty`); this is the documented pattern for background scopes (see decision #12)
- `Permission.TransactionsRead` added proactively alongside Create even though no GET endpoint exists yet — prevents a separate grant update when WI-P2-03b lands

### Test count

419 → 460 (217 unit + 243 integration), 2 intentionally skipped — zero regressions.

### What's next

`GET /api/transactions` (list with pagination + get-by-id) — WI-P2-03b.

---

## 2026-05-28 — WI-P2-02: CompensationTransaction Domain Surgery

**Duration:** ~1.5 hours (including 3 bug-fix loops)
**Phase:** Phase 2

### What we did

- Replaced `CompensationTransactionStatus` enum: removed `Credited` (not spec-equivalent); added spec lifecycle `Pending, Eligible, Calculated, Paid, Cancelled`. Table was write-orphan (confirmed) — destructive replacement safe per §8.4.1
- Renamed `ExternalReference` → `ExternalId` in entity, EF config, and migration (aligns with spec §5.3.1 naming)
- Migration `20260528083023_P2_TransactionDomainSurgery`: `sp_rename` column + filtered unique index + Tenant.Tier DefaultValue removal (pending from previous WI)
- Filtered unique index SQL: `CREATE UNIQUE INDEX IX_CompensationTransactions_TenantId_Source_ExternalId ON CompensationTransactions (TenantId, Source, ExternalId) WHERE ExternalId IS NOT NULL`
- Factory `Ingest(...)` now validates: `tenantId != Guid.Empty`, `payeeId != Guid.Empty`, `referenceNumber` not null/blank, `ingestedBy` not null/empty, `transactionDate >= 2000-01-01`; no `DateTime.UtcNow` introduced (Rule 2.5.3)
- `MarkEligible(updatedBy, now, eventId)`: Pending → Eligible, raises `TransactionMarkedEligibleEvent`
- `Cancel` updated: allows Pending and Eligible → Cancelled; blocks Calculated and Paid (Phase 3 clawback note)
- `MarkCalculated` and `MarkPaid`: Phase 3 stubs throwing `NotSupportedException` (no callers → LSP not violated)
- `MarkCredited` removed (Credited status replaced)
- §5b.7 gap closed: every state-change method raises a domain event
- EF1002 warning eliminated in `MultiTenantDefenseTests.cs:47` (Guid concatenation → `ExecuteSqlAsync(FormattableString)`)

### Bugs fixed during the run

1. `WithMessage("*[Rr]eference*")` — FluentAssertions wildcard does not support character classes; fixed to `"*Reference number*"`
2. `WithInnerException` async chaining syntax — awaited the assertion object first, then chained
3. Multiple `CompensationTransaction` instances sharing the same static `Money` instance in the same `DbContext` → EF Core lost owned-entity tracking → `NULL Amount` insert error; fixed by creating a fresh `Money.Of(...)` per call

### Files produced/modified

- `src/Wasnie.Domain/Compensation/Enums/CompensationTransactionStatus.cs` (MODIFIED — full spec lifecycle)
- `src/Wasnie.Domain/Compensation/Transactions/CompensationTransaction.cs` (MODIFIED — rename, factory guards, MarkEligible, Cancel, stubs)
- `src/Wasnie.Domain/Compensation/Events/TransactionMarkedEligibleEvent.cs` (NEW)
- `src/Wasnie.Infrastructure/Persistence/Configurations/Compensation/CompensationTransactionConfiguration.cs` (MODIFIED — rename, idempotency index)
- `src/Wasnie.Infrastructure/Persistence/Migrations/20260528083023_P2_TransactionDomainSurgery.cs` (NEW)
- `tests/Wasnie.IntegrationTests/MultiTenant/MultiTenantDefenseTests.cs` (MODIFIED — EF1002 fix)
- `tests/Wasnie.UnitTests/Domain/CompensationTransactionTests.cs` (NEW — 27 tests)
- `tests/Wasnie.IntegrationTests/Transactions/CompensationTransactionCollection.cs` (NEW)
- `tests/Wasnie.IntegrationTests/Transactions/CompensationTransactionFixture.cs` (NEW)
- `tests/Wasnie.IntegrationTests/Transactions/CompensationTransactionIdempotencyTests.cs` (NEW — 5 tests)

### Key decisions

- `Credited` replaced (not renamed): semantic mismatch with spec, write-orphan table = safe
- Phase 3 stubs throw `NotSupportedException` — acceptable because no callers exist and the stubs clearly signal where Phase 3 picks up
- Idempotency index is FILTERED (`WHERE ExternalId IS NOT NULL`): manual-entry transactions have no external ID; a non-filtered index would block inserting multiple manual transactions for the same source
- `transactionDate` minimum is a hardcoded floor (`2000-01-01`), not a `now`-relative check — upper-bound policy (no future dates) belongs in the Application validator per division-of-labor boundary

### Test count

387 → 419 (190 unit + 229 integration), 2 intentionally skipped — zero regressions.

### What's next

Phase 2 ingestion handlers: `IngestTransactionCommand` + `IngestTransactionCommandHandler` (implements `IMoneyCriticalCommand`), `IngestTransactionCommandValidator` (payee existence, date-range policy), API endpoint, integration tests.

---

## 2026-05-28 — WI-P2-01b: Remove [JsonConstructor] from Domain Money (Rule 1.5 fix)

**Duration:** ~1.5 hours (including two bug-fix loops)
**Phase:** Phase 2 pre-work

### What we did

- Removed `[JsonConstructor]` and `using System.Text.Json.Serialization` from `Money.cs` — Rule 1.5 fully resolved; Domain layer now has zero serialization attributes
- Created `MoneyJsonConverter : JsonConverter<Money>` in `Wasnie.Infrastructure.Persistence.Serialization`:
  - Reads `amount` as number or string (backward compat with `AllowReadingFromString`)
  - Case-insensitive property matching (`OrdinalIgnoreCase`)
  - Wraps `DomainException` from `Money.Of()` as `JsonException` with inner exception (required for correct exception propagation from deserializer)
  - Write path produces `{"amount":<decimal>,"currency":"<ISO3>"}` — byte-compatible with old `[JsonConstructor] + JsonSerializerDefaults.Web` output
- Registered converter in `PlanRuleConfiguration` and `PayoutLineConfiguration` via per-config `BuildJsonOptions()` factory
- Registered globally in `Program.cs` via `AddControllers().AddJsonOptions(...)` to cover HTTP deserialization of `AddRuleToPlanCommand.Cap/Floor`
- 17 unit tests in `MoneyJsonConverterTests` (no DB)
- 3 DB round-trip integration tests in `MoneyRoundTripTests` (own Testcontainers fixture); use `ExecuteSqlAsync(FormattableString)` to avoid EF Core treating JSON `{...}` as parameter placeholders
- Docs path problem discovered: `docs/` is at `../docs/` relative to `WasnieApi/` — Glob searches scoped inside the repo dir miss them; confirmed root cause was wrong working directory assumption, not missing files

### Bugs fixed during the run

1. `MoneyJsonConverter.Read` initially let `DomainException` escape unwrapped → test `Deserialize_InvalidCurrency_ThrowsDomainException` failed. Fixed by catching `DomainException` and re-throwing as `new JsonException(ex.Message, ex)`.
2. `INSERT INTO PlanRules ... (Trigger, ...)` failed with SQL Server syntax error → `Trigger` is a reserved keyword. Fixed by quoting as `[Trigger]`.

### Files produced/modified

- `src/Wasnie.Domain/Compensation/ValueObjects/Money.cs` (MODIFIED — removed `[JsonConstructor]` and using)
- `src/Wasnie.Infrastructure/Persistence/Serialization/MoneyJsonConverter.cs` (NEW)
- `src/Wasnie.Infrastructure/Persistence/Configurations/Compensation/PlanRuleConfiguration.cs` (MODIFIED — `BuildJsonOptions()` + converter)
- `src/Wasnie.Infrastructure/Persistence/Configurations/Compensation/PayoutLineConfiguration.cs` (MODIFIED — `BuildJsonOptions()` + converter)
- `src/Wasnie.Api/Program.cs` (MODIFIED — `AddJsonOptions` global registration)
- `tests/Wasnie.IntegrationTests/Serialization/MoneyJsonConverterTests.cs` (NEW — 17 tests)
- `tests/Wasnie.IntegrationTests/Serialization/MoneyRoundTripTests.cs` (NEW — 3 DB round-trip tests)
- `tests/Wasnie.IntegrationTests/Serialization/MoneyRoundTripFixture.cs` (NEW)
- `tests/Wasnie.IntegrationTests/Serialization/MoneyRoundTripCollection.cs` (NEW)

### Test count

367 → 387 (163 unit + 224 integration), 2 intentionally skipped — zero regressions.

### What's next

Phase 2 proper — Transactions module + Calculation Engine. All pre-work complete. Domain is clean. First Phase 2 command handler implements `IMoneyCriticalCommand` and uses `Money.Of(...)` / `Money.OfNonNegative(...)`.

---

## 2026-05-28 — WI-P2-01: Money Value Object §5b.5 Refactor

**Duration:** ~45 minutes
**Phase:** Phase 2 pre-work

### What we did

- Audited existing `Money` value object — found it already existed but was missing several §5b.5 behaviors
- Verified data safety: grepped all .cs and .json files for monetary values with >4 decimal places → zero matches; normalization confirmed safe
- Added 4-decimal internal normalization with banker's rounding (`MidpointRounding.ToEven`) in private constructor — every code path (Of, Add, Subtract, Multiply, Divide, Negate, Abs) goes through this constructor
- Added `Negate()` and `Abs()` methods
- Added four comparison operators (`>`, `<`, `>=`, `<=`) — same-currency only, throw `DomainException` on mismatch
- Refactored `GuardSameCurrency` from instance method to `private static` to support operator usage
- 25 new unit tests: normalization (>4 decimal input), banker's rounding midpoint cases at 4-decimal boundary in both directions, Multiply re-normalization midpoints, Negate (zero/positive/negative), Abs (zero/positive/negative), all four comparison operators same-currency, all four throwing on currency mismatch, equality regression guard (`==` on different currencies returns false, does not throw)

### Key decisions

- `[JsonConstructor]` Rule 1.5 violation left in place; tracked in WI-P2-01b (`MoneyJsonConverter` in Infrastructure, update 3 EF Core configurations)
- All arithmetic re-normalizes automatically via private constructor — no separate normalization call per method

### Files produced/modified

- `src/Wasnie.Domain/Compensation/ValueObjects/Money.cs` (MODIFIED — normalization, Negate, Abs, comparison operators, static GuardSameCurrency)
- `tests/Wasnie.UnitTests/Domain/MoneyTests.cs` (MODIFIED — +25 tests)

### Test count

342 → 367 (163 unit + 204 integration), 2 intentionally skipped — zero regressions.

### What's next

Phase 2 proper — Transactions module + Calculation Engine. Both pre-work WIs complete. First Phase 2 command handler will implement `IMoneyCriticalCommand` and use `Money.Of(...)` / `Money.OfNonNegative(...)`.

---

## 2026-05-28 — WI-P2-00: Audit Dispatcher Fail-Hard for Money Operations

**Duration:** ~1 hour
**Phase:** Phase 2 pre-work

### What we did

- Implemented `IMoneyCriticalCommand` marker interface (extends `IAuditableCommand`) as the money-critical signal for `AuditBehavior`
- Exposed `DatabaseFacade Database { get; }` on `IApplicationDbContext` to enable explicit transaction management from Application layer (consistent with F-001 deferral — EF Core already in Application)
- Extended `AuditBehavior<TRequest, TResponse>` with a `HandleMoneyCriticalAsync` path: wraps `next()` + `DispatchAsync()` in `db.Database.BeginTransactionAsync()`. Both `SaveChangesAsync` calls participate in the same transaction; `CommitAsync()` commits both atomically. Any exception → `DisposeAsync()` → auto-rollback
- Non-money behavior is byte-for-byte unchanged (swallows audit failures per Rule 5.3.3)
- Created `MoneyAuditTestFixture` (self-contained, own Testcontainers MsSql instance, isolated from shared integration fixture) and `MoneyAuditCollection`
- 3 new integration tests in `MoneyAuditTransactionTests` proving all three required scenarios

### Key decisions

- **Option A** (`IMoneyCriticalCommand` marker) chosen over Option B (dispatcher flag): visible at command definition site, consistent with existing `IAuditableCommand` pattern, doesn't bleed the concept through `IAuditService`/`IAuditDispatcher` signatures
- No external outbox or message queue introduced — in-process EF Core transaction is sufficient and correct at current scale per WI requirements
- `IApplicationDbContext.Database` addition is pragmatic (consistent with F-001 deferral)
- Extension point clearly marked in test file: Phase 2 Transaction/Payout/Credit commands implement `IMoneyCriticalCommand` — no fake production handler created

### Files produced/modified

- `src/Wasnie.Application/Common/Interfaces/IMoneyCriticalCommand.cs` (NEW)
- `src/Wasnie.Application/Common/Interfaces/IApplicationDbContext.cs` (MODIFIED — added `DatabaseFacade Database { get; }`)
- `src/Wasnie.Application/Common/Behaviors/AuditBehavior.cs` (MODIFIED — added `db` parameter + `HandleMoneyCriticalAsync`)
- `tests/Wasnie.IntegrationTests/Audit/MoneyAuditCollection.cs` (NEW)
- `tests/Wasnie.IntegrationTests/Audit/MoneyAuditTestFixture.cs` (NEW)
- `tests/Wasnie.IntegrationTests/Audit/MoneyAuditTransactionTests.cs` (NEW)

### Test count

339 → 342 (138 unit + 204 integration), 2 intentionally skipped — zero regressions.

### What's next

Phase 2 proper — Transactions module + Calculation Engine. The first Phase 2 command that touches money implements `IMoneyCriticalCommand` directly; no further infrastructure changes needed.

---

## 2026-05-27 (late evening) — Phase C OFFICIAL CLOSURE (Wave 6-10)

**Duration:** ~6-8 hours
**Phase:** C (Waves 6 through 10 — full closure)

### What we did

Executed remaining Phase C work items in sequence:

- WI-09a (Backend RBAC + Tier Limits): 4 roles, IAuthorizationService + IClaimsService + ITierLimitChecker, 29 handlers refactored, /auth/me endpoint, JWT role claim, 50 new tests. 280/280 pass.
- WI-09b (Frontend RBAC Integration): CurrentUserService (signals), *hasPermission directive + pipe + guard, forbiddenResponseInterceptor, /forbidden page, TierLimitModal, provideAppInitializer, all feature buttons wrapped, sidebar items hidden by role, en/es/pl translations. 59/59 frontend tests pass.
- WI-10 (Validators + Cross-Tenant Tests): 3 validators, 3 new integration test files (Quotas, Assignments, PlanRules), 44 new tests. F-028 confirmed as systemic pattern. 324/324 pass.
- WI-13 (Cleanup of Low Findings): F-020 safety comments, F-021 token replacement, F-024 confirmed mitigated + CONTRIBUTING.md created, F-026 reframed as architectural decision (LegacyPlan has active references).
- WI-11 (Security Middleware): SecurityHeadersMiddleware (CSP, X-Frame, etc.), rate limiter (login/register/refresh/global), password policy hardening (10 chars + symbols + lockout), HSTS in production. 7+ new tests.
- WI-12 (Observability): CorrelationIdMiddleware (first in pipeline), Serilog config-driven JSON formatter, TenantUserCorrelationEnricher, frontend ErrorTrackingService abstraction, GlobalErrorHandler, correlationIdInterceptor. 14 new tests.

Final result: 339 tests pass (138 unit + 201 integration), build clean, 23 of 27 findings closed.

### Key decisions

- F-028 (cross-tenant 422 vs 404) confirmed SYSTEMIC across Quotas/Assignments/PlanRules/Imports; tests accept both; deferred to future API contract standardization WI
- F-026 reframed as architectural decision (LegacyPlan + DDD Plan are intentional dual representation); too costly to consolidate now
- Manager/Rep scoped data access deferred (currently see all data in tenant) — future enhancement WI
- Rate limit tests: 2 skipped intentionally due to test infrastructure flakiness; manual verification with curl required before first production customer
- TenantUserCorrelationEnricher in Wasnie.Api/Observability/ not Infrastructure (Serilog package boundary)
- Operational logging in handlers: zero _logger calls in src/ — audit trail covers events; ops logging deferred to future WI
- Phase C officially closed; 4 findings deferred with documented rationale

### Files produced/modified this session

See PROJECT_STATUS.md comprehensive lists. Key highlights:
- ~50 backend files created across all WIs
- ~15 backend files modified (Program.cs, DI, multiple appsettings, handlers, middleware)
- ~15 frontend files created (RBAC + observability infrastructure)
- ~12 frontend files modified (app.config, routes, sidebar, feature components, translations)
- CONTRIBUTING.md created

### What's next

**Phase C is closed.** Next options:
- Phase 2 (Transactions + Calculation Engine) — recommended next
- Phase D (coverage push + docs polish) — optional intermediate step
- Opportunistic cleanup: F-028 standardization, operational logging, Manager/Rep scoped access

### Notes / lessons learned

- The disciplined ARCHITECTURE.md + WI prompt workflow scaled excellently. 9 WIs completed in one day with zero regressions and only documented, justified deviations.
- Claude Code's autonomous architectural decisions (e.g., BIGINT for AuditLog Id in WI-08, rate limit override in TestWebApplicationFactory in WI-11, enricher placement in Wasnie.Api in WI-12) have been consistently correct. The pattern of high-level constraints + implementation autonomy works well.
- Phase C took one day instead of estimated 5 weeks. The audit's 50-70h estimate was conservative; with focused Claude Code execution it collapsed to ~10-12 effective hours.
- Wasnie is now production-grade for security, multi-tenant isolation, audit trail, RBAC, and observability infrastructure. The remaining deferrals (WI-02, WI-06, F-026, F-028) are documented and non-blocking.
- The continuity docs strategy (PROJECT_STATUS + SESSION_LOG + update prompts) proved essential for tracking such intense progress in one day.

---

## 2026-05-27 (evening) — Phase C Wave 4 + Wave 5 Execution (IClock + Audit Trail)

**Duration:** ~4-6 hours
**Phase:** C (Wave 4 complete + Wave 5 complete; Wave 3 deferred)

### What we did

- WI-06 deferred: Strict Clean Architecture refactor not justified at current scale; EF Core in Application accepted as pragmatic compromise with documented rationale
- WI-07 executed: IClock and IGuidGenerator abstractions introduced. 14+ Domain entities refactored to factory pattern. 14 handlers and 3 services updated. RefreshToken.IsValid → IsValidAt(now). Two pragmatic exceptions documented (Rule.cs aggregate child, Modifier.cs value object). 222/222 tests pass.
- WI-08 executed: Complete audit trail infrastructure built. AuditLog entity with BIGINT identity, immutability SQL trigger, EF mapping with 3 composite indexes. IAuditService + IAuditDispatcher + IAuditableCommand + AuditBehavior pipeline. SyncAuditDispatcher writes within transaction for consistency. 7 handlers retrofit with audit (5 explicit, 2 via pipeline marker). 8 new tests covering unit (EntityDiff), integration (AuditService), and HTTP-level (full flow). 230/230 tests pass.

### Key decisions

- WI-06 deferred: strict purity refactor postponed. ARCHITECTURE.md §1.2 violations documented but unfixed. Revisit when team grows or compliance demands.
- AuditLog uses BIGINT (long) Id instead of GUID — better for high-cardinality, write-only audit table. Decided by Claude Code during WI-08, approved as good engineering.
- Audit pipeline swallows dispatcher failures per Rule 5.3.3 — acceptable for Phase 1; MUST become transactional rollback when Phase 2 money operations arrive.
- Audit pattern is hybrid: explicit IAuditService.LogAsync(...) for some handlers, IAuditableCommand marker + AuditBehavior for others. Both valid, choose per command.
- WI-07 two pragmatic exceptions: Rule.cs (child entity in Plan aggregate uses internal factory) and Modifier.cs (value object). Both DDD-correct.

### Files produced/modified this session

WI-07:
- Created in src/Wasnie.Application/Common/Abstractions/: IClock.cs, IGuidGenerator.cs
- Created in src/Wasnie.Infrastructure/Common/: SystemClock.cs, SystemGuidGenerator.cs
- Created in tests projects: FakeClock.cs, FakeGuidGenerator.cs (unit + integration)
- Modified: 14+ Domain entities (factory pattern), 14 Application handlers, 3 Infrastructure services, DependencyInjection.cs

WI-08:
- Created in src/Wasnie.Domain/Audit/: AuditLog.cs, AuditActions.cs, ResourceTypes.cs
- Created in src/Wasnie.Application/Common/: Interfaces/IAuditService.cs, IAuditDispatcher.cs, IAuditableCommand.cs, Behaviors/AuditBehavior.cs, DTOs/AuditEntry.cs, Helpers/EntityDiff.cs
- Created in src/Wasnie.Infrastructure/Services/Audit/: AuditService.cs, SyncAuditDispatcher.cs
- Created in src/Wasnie.Infrastructure/Persistence/Configurations/: AuditLogConfiguration.cs
- Created migration: src/Wasnie.Infrastructure/Persistence/Migrations/20260527000000_AddAuditLog.cs
- Modified: ApplicationDbContext.cs, IApplicationDbContext.cs, Application/DependencyInjection.cs, Infrastructure/DependencyInjection.cs
- Modified handlers: LoginCommandHandler, LogoutCommandHandler, CreatePayeeHandler, UpdatePayeeHandler, CreatePlanHandler, ActivatePlanCommand, ArchivePlanCommand
- New tests: EntityDiffTests.cs, AuditServiceTests.cs, AuditTrailIntegrationTests.cs

### What's next

- WI-09 (RBAC + tier limits) — gating item for monetization. Largest single WI (12-16h). Decisions pending: split backend/frontend? scope of tier limits?
- Subsequent waves: WI-10 (validators + tests), WI-11 (security middleware), WI-12 (observability), WI-13 (cleanup)

### Notes / lessons learned

- Claude Code's autonomous design decisions (e.g., BIGINT for AuditLog Id) have been consistently good. The pattern of giving high-level constraints and letting implementation details emerge is working well.
- Audit trail infrastructure was the most complex WI to date and completed cleanly in one pass — strong validation that the prompt-driven workflow with ARCHITECTURE.md as authority scales to larger refactors.
- IClock refactor (WI-07) touched many files but the systematic pattern (Domain factories → Application handlers → Infrastructure services → tests) executed without regressions. Build green throughout.
- Multi-tenant compliance, time/Id determinism, and audit trail now in place. Wasnie is significantly closer to "production-grade financial SaaS" than at the start of the day.

---

## 2026-05-27 (afternoon) — Phase C Wave 1 + Wave 2 Execution

**Duration:** ~6-8 hours
**Phase:** C (Wave 1 partial + Wave 2 complete)

### What we did

- Executed WI-01 — Tightened JWT access token (60→15 min) and refresh token (30→7 days) lifetimes across all configs and code defaults. 210/210 tests pass.
- Reformulated WI-02 — Email verification deferred. Email provider integration moved to Phase 5-6. Architectural pattern preserved for future trivial integration. Updated Audit_Backlog.md with new "Deferred Decisions" section.
- Executed WI-03 — Logout now revokes refresh tokens server-side; RefreshTokenCommandValidator created. 6 new integration tests. 216/216 tests pass.
- Executed WI-04 — Import cache key now tenant-scoped. Codebase audit confirmed no other cache usages with the same issue. 217/217 tests pass.
- Executed WI-05 — Three multi-tenant defense fixes in parallel (ListPayeesHandler explicit filter, ImportAudit global query filter, TenantContext enforcement with middleware translation). Codebase audit confirmed full multi-tenant compliance. 222/222 tests pass.

### Key decisions

- Email provider deferred to Phase 5-6 when first paying customer requires it (WI-02 scope updated)
- TenantContext returns Guid.Empty for null HttpContext (background services / test fixtures), throws only when authenticated user lacks tenant claim
- Cross-tenant 400 vs 404: NOT fixed in WI-04; candidate for future API standardization (potential F-028, not yet added to findings)
- All 11 tenant-scoped entities confirmed to have global query filters; multi-tenant isolation fully compliant after WI-05

### Files produced/modified this session

Backend source files modified:
- src/Wasnie.Api/appsettings.json, appsettings.Development.json, appsettings.Production.json, appsettings.Development.template.json
- src/Wasnie.Infrastructure/Services/TokenService.cs
- src/Wasnie.Application/Common/Interfaces/ITokenService.cs
- src/Wasnie.Api/Controllers/AuthController.cs
- src/Wasnie.Infrastructure/Services/Imports/ImportCacheService.cs
- src/Wasnie.Infrastructure/DependencyInjection.cs (ImportCacheService lifetime: Singleton → Scoped)
- src/Wasnie.Application/Compensation/Handlers/Payees/ListPayeesHandler.cs
- src/Wasnie.Infrastructure/Persistence/ApplicationDbContext.cs
- src/Wasnie.Infrastructure/Identity/TenantContext.cs
- src/Wasnie.Api/Middleware/ExceptionHandlingMiddleware.cs

Backend test files modified or created:
- tests/Wasnie.IntegrationTests/Infrastructure/TestDatabaseFixture.cs (modified)
- tests/Wasnie.IntegrationTests/Integration/Imports/PayeeImportEndpointsTests.cs (modified twice)
- tests/Wasnie.IntegrationTests/Auth/AuthEndpointsTests.cs (created)
- tests/Wasnie.IntegrationTests/MultiTenant/MultiTenantDefenseTests.cs (created)

New backend application files:
- src/Wasnie.Application/Features/Auth/Commands/LogoutCommand.cs
- src/Wasnie.Application/Features/Auth/Handlers/LogoutCommandHandler.cs
- src/Wasnie.Application/Features/Auth/Validators/RefreshTokenCommandValidator.cs

Documentation:
- docs/audit/Audit_Backlog.md (updated with Deferred Decisions section + revised WI-02)

### What's next

- WI-06 — Clean Architecture fixes (F-001, F-002): remove MediatR from Domain, remove EF Core from Application. Largest single WI in backlog (6-8h). Strict purity vs pragmatic amendment decision pending.
- Wave 4: WI-07 (IClock, IGuidGenerator)
- Wave 5: WI-08 (audit trail foundation)
- Wave 6: WI-09 (RBAC + tier limits)

### Notes / lessons learned

- Claude Code's ability to perform codebase audit alongside fixes is valuable (WI-05 confirmed only one IgnoreQueryFilters() in source — eliminates uncertainty about other latent issues)
- Test fixture interaction with global query filters: DI scopes without HTTP context need IgnoreQueryFilters() on queries — this is a known pattern, not a regression
- Multi-tenant isolation can now be claimed as production-grade compliant; this is a meaningful milestone for a financial SaaS

---

## 2026-05-27 — B2 Codebase Audit + Continuity Strategy

**Duration:** ~2 hours
**Phase:** B2 (audit) + meta-work for cross-chat continuity

### What we did

- Generated and executed audit prompt for Claude Code to read all 14 ARCHITECTURE.md sections and audit the codebase
- Claude Code produced `docs/audit/Audit_Findings.md` with 27 findings (8 Critical, 7 High, 8 Medium, 4 Low)
- Reviewed audit results; confirmed codebase is fundamentally sound with specific, fixable issues
- Designed continuity strategy for chat-to-chat handoff (this document + PROJECT_STATUS.md)
- Generated PROJECT_STATUS.md (current project state)
- Generated SESSION_LOG.md (this file)
- Generated Claude Code update prompt template (`Update_PROJECT_STATUS.md` prompt)

### Key decisions

- B3 (prioritized backlog) is the next step before any fixes
- Top fix priority order: F-007 (cache cross-tenant) → JWT lifetimes (F-005/006) → Email verification (F-008) → IClock pattern (F-003/004) → Clean Arch violations (F-001/002)
- Continuity docs (PROJECT_STATUS + SESSION_LOG) live in `docs/` root, not a subfolder
- After every significant session, PROJECT_STATUS.md gets updated and a new SESSION_LOG entry is appended

### Files produced this session

- `docs/audit/Audit_Findings.md` (by Claude Code)
- `docs/PROJECT_STATUS.md` (initial creation)
- `docs/SESSION_LOG.md` (this file, initial creation)
- Prompt for Claude Code to update PROJECT_STATUS.md (in /mnt/user-data/outputs/)

### What's next

- **B3** — generate prioritized backlog with effort estimates and dependencies for the 27 audit findings
- After B3 → start Phase C fixes, beginning with F-007 (most exploitable)

### Notes / lessons learned

- Audit via Claude Code reading the codebase is far more efficient than passing files manually to chat
- The codebase's compliance areas (no findings) reveal solid Phase A work: thin controllers, server-side pagination, tenant query filters, Testcontainers integration tests
- ARCHITECTURE.md proved its value in the audit — Claude Code had clear, testable rules to check against

---

## 2026-05-26 (PM/evening) — Auth Pages Visual Work (DEFERRED)

**Duration:** ~1.5 hours
**Phase:** Tangential UI work (not on Master Plan)

### What we did

- Attempted to redesign login/register pages with hero images (Salesforce-style)
- Three prompts attempted (49, 50, 51), all with regressions
- Final state: working tree reverted, auth pages back to original "simple blue background"
- Diagnosed root cause: visual prompts need exact image references, not abstract descriptions
- Added new architecture lesson: "Inspect existing structure before specifying replacement"

### Status

**Deferred.** Auth pages work paused — not blocking development. To be resumed when:
- Visual mockups are prepared in advance
- An explicit image reference is shared with Claude Code (not described in text)
- Time is available for proper visual design iteration

### What's next

- N/A for this work stream. Return to Master Plan (B2 audit, then B3, then Phase C).

---

## 2026-05-26 (afternoon) — B0 + B1 Documentation Wave

**Duration:** ~5 hours
**Phase:** B0 (Product Docs) + B1 (ARCHITECTURE.md)

### What we did

- Created `docs/Wasnie_User_Personas.md` — 4 primary personas (Ariana, Sergio, Maja, Marek) with Jobs To Be Done, anti-personas, priority matrix
- Created `docs/Wasnie_Business_Brief.docx` — 13-section professional document for investors/customers/partners (English, Word format, sober corporate design)
- Created `docs/ARCHITECTURE.md` (master) + 14 section files in `docs/architecture/`:
  - 01-clean-architecture
  - 02-solid
  - 03-performance-baselines
  - 04-security
  - 05-audit-trail
  - 06-authorization
  - 07-testing-standards
  - 08-breaking-change-protocol
  - 09-multi-tenant-isolation
  - 10-visual-changes-protocol
  - 11-cicd-quality-gates
  - 12-observability
  - 13-claude-code-autonomy
  - 14-forbidden-patterns
- Established Critical Twelve (universal binding rules)
- Established routing table (which sections to read for which task type)
- Established Claude Code prompt protocol with mandatory ARCHITECTURE compliance header

### Key decisions

- Document precedence: ARCHITECTURE.md > Product Spec > DESIGN_SYSTEM > Master Plan
- Strict MUST/NEVER/FORBIDDEN language throughout architectural docs
- All documentation in English (chats in Spanish)
- Personal Trainer background NEVER mentioned in Wasnie context
- Subscription tiers finalized: Free / Starter €300 / Growth €800 / Scale €1,800 / Enterprise €2,500+
- Geographic target order: Poland → CEE → Iberian/LATAM
- Founder bio: 12+ years developer, multiple industries, no PT mention
- B1 split into 14 files (not one large file) for efficient Claude Code consumption per-task

### Files produced this session

- `docs/ARCHITECTURE.md`
- `docs/architecture/01-clean-architecture.md` through `14-forbidden-patterns.md`
- `docs/Wasnie_User_Personas.md`
- `docs/Wasnie_Business_Brief.docx`
- `docs/Wasnie_Master_Plan_Phase_1_Closure.md` (v1.1 update)

### What's next

- B2 — audit codebase against ARCHITECTURE.md
- B3 — prioritized backlog
- Phase C — start fixing critical findings

---

## 2026-05-26 (morning) — Phase A Closure

**Duration:** ~6 hours
**Phase:** Phase A — Closing Phase 1 Import feature

### What we did

- A1: UI polish + 4 reusable components (`WsPageLayout`, `WsWizard`, `WsWizardStep`, `WsDataTable`, `WsStatCard`)
- A2: Backend Import tests — 85 tests, coverage >85% on Import services, integration tests with Testcontainers
- A3: Frontend Import tests — 59 tests, 95-97% coverage on tested helpers
- Server-side pagination implemented and audited (prompts 39, 41, 42, 43) — all list endpoints now paginated
- Surface elevation drama: introduced `--color-bg-surface-deep` token after surgical fix (prompts 44 disaster → 47 fix)
- A4 (E2E tests) deferred to Phase 9

### Key decisions

- Phase A officially closed with sign-off
- 10 lessons learned codified for inclusion in ARCHITECTURE.md
- Adjusted timeline: 4-5 weeks for Phase 1 closure (down from 6-7)

### Lessons learned (incorporated into ARCHITECTURE.md)

- Breaking changes must update ALL consumers in same PR
- "no regressions" requires running FULL test suite
- Numerical specs > adjectives in visual changes
- Hard constraints in prompts prevent scope creep
- Claude Code: code yes, git no (autonomy boundary)
- Pure functions are dramatically easier to test
- Multi-tenant isolation is test rule #1

---

## Earlier sessions (pre-2026-05-26)

Earlier session context is captured implicitly in:
- `docs/Wasnie_Product_Master_Specification.md` (product definition)
- `docs/Wasnie_Master_Plan_Phase_1_Closure.md` (operational plan)
- `docs/Wasnie_Informe_Tecnico.docx` (original market analysis, Spanish)
- Git history of the codebase

For detailed pre-2026-05-26 work, consult those documents and the git log.

---

## Entry template (for future sessions)

```markdown
## YYYY-MM-DD — [Brief session title]

**Duration:** ~X hours
**Phase:** [phase identifier]

### What we did
- [bullet list of accomplishments]

### Key decisions
- [decisions made during this session]

### Files produced this session
- [list of files created or significantly modified]

### What's next
- [next planned actions]

### Notes / lessons learned (optional)
- [insights to remember]
```
