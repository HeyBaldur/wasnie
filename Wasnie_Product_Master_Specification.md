# Wasnie — Product Master Specification

**Document version:** 1.0
**Last updated:** May 2026
**Owner:** Rodolfo A. Calvo Jaubert
**Status:** Living document — update as scope evolves

---

## 0. How to use this document

This is the **single source of truth** for what Wasnie is, what it must do to compete in the SPM/ICM market, and the order in which we build it. Every Claude Code session that touches scope must read this document first. Every conversation about features must reference it.

The document has five layers:

1. **Strategic context** — what market, what positioning, against whom
2. **Functional modules** — what the software must do
3. **Build phases** — the order in which we build it
4. **Current state** — what is already built
5. **Glossary** — terms used consistently across the project

Sections are written so a developer, a potential investor, or a sales prospect can all read them and understand the product. Keep that bar.

---

## 1. Strategic context

### 1.1 What Wasnie is

Wasnie is a **Sales Performance Management (SPM) and Incentive Compensation Management (ICM) platform** delivered as a multi-tenant SaaS. It automates the design, calculation, approval, and reporting of sales commissions for organizations with between 10 and 200 sales representatives.

In plain words: companies that pay variable compensation to their salespeople use Wasnie to define the rules of that compensation, calculate it accurately, communicate it transparently to each rep, and resolve disputes without chaos.

### 1.2 Why Wasnie exists

Three confirmed market problems Wasnie addresses:

1. **70% of mid-market companies still calculate commissions in spreadsheets**, with documented error rates causing direct revenue loss and rep attrition.
2. **Enterprise SPM tools (Xactly, Varicent, SAP Commissions) are too complex, too expensive, and too slow to implement** for companies under 200 reps — typical implementations take 6 to 18 months and cost $100K–$400K in consulting alone.
3. **The "post-Excel, pre-Enterprise" gap is underserved** — companies that have outgrown spreadsheets but cannot afford or operate enterprise-grade tools.

### 1.3 Target market — primary

- **Geography:** LATAM, Spain, Portugal, Central and Eastern Europe (Poland, Czech Republic, Romania, Hungary), with later expansion to broader Europe and the US mid-market.
- **Company size:** 15 to 200 sales reps.
- **Industries:** SaaS B2B, financial services (insurance, fintech), distribution and wholesale, professional services, telecom resellers, real estate brokerages.
- **Buyer persona:** Director of Sales Operations, Head of Revenue Operations, CFO of mid-market companies, VP of Sales.

### 1.4 Competitors and positioning

| Competitor | Segment | Where Wasnie wins |
|---|---|---|
| **Xactly Incent** | Enterprise (200+ reps) | Wasnie targets a smaller segment; cheaper, faster to deploy, no mandatory consultants |
| **Varicent** | Large enterprise | Same as above; plus modern UX |
| **SAP Commissions / CallidusCloud** | Enterprise, especially SAP shops | CallidusCloud retires December 2026 — migration window for displaced customers |
| **CaptivateIQ** | Mid-market and upper-mid-market US | Wasnie focuses on LATAM and Eastern Europe with native language, local pricing, local timezone support |
| **Spiff** (Salesforce) | SMB / mid-market | Wasnie offers multi-tenant flexibility outside Salesforce's orbit |
| **Performio** | Mid-market | Wasnie is more modern in UX and pricing model |
| **Everstage** | SMB to mid-market | Closest direct competitor; Wasnie differentiates on language and geography |
| **QCommission** | SMB | Wasnie offers a more modern product |
| **Excel + custom scripts** | Anyone under 50 reps with no budget | Wasnie's real volume competitor — the alternative most companies actually use |

### 1.5 Wasnie's positioning statement

> **Wasnie is the modern SPM platform for sales teams of 15 to 200 reps in LATAM and Europe who have outgrown spreadsheets but do not want to pay enterprise prices or hire consultants. Native multi-language (Spanish, English, Polish), transparent pricing in USD or EUR, self-serve onboarding in under two weeks.**

### 1.6 Pricing model (planned, to validate)

Per-rep monthly subscription, billed annually with monthly discount option:

| Tier | Reps included | Price per rep / month | Includes |
|---|---|---|---|
| **Starter** | Up to 25 | 8 EUR | Core SPM, 1 admin, basic reporting, email support |
| **Growth** | Up to 100 | 12 EUR | Everything in Starter + integrations, multi-admin, advanced reporting, simulator |
| **Scale** | 100 to 250 | 15 EUR | Everything in Growth + SSO, audit log, API access, priority support |
| **Enterprise** | Custom | Custom | Custom contract, dedicated CSM, SLA, custom integrations |

Public pricing on the website. No "contact us for pricing" wall except for Enterprise.

---

## 2. Functional modules

This section lists **every module** Wasnie must include to compete realistically in the SPM/ICM market. Modules are grouped by area. Each module has a status indicator:

- 🟢 **Built** — already implemented and functional
- 🟡 **Partial** — partially implemented, needs completion
- 🔴 **Not started** — to be built
- ⚪ **Future** — explicitly out of scope for initial launch, planned for later

---

### 2.1 Foundation modules

These are the non-domain pieces every SaaS needs.

#### 2.1.1 🟢 Multi-tenant architecture
- Tenant entity as the root of all data isolation
- Discriminator column (TenantId) on every domain entity
- Global EF Core query filters applied automatically
- Tenant context injected per request from JWT claim
- Tenant provisioning during registration

#### 2.1.2 🟢 Identity and authentication
- ASP.NET Core Identity backed by SQL Server
- JWT bearer tokens with refresh tokens (SQL-persisted, revocable)
- Tenant-scoped user accounts
- Email + password authentication
- Future: SSO via SAML/OAuth providers (Google, Microsoft, Okta) — Scale tier only

#### 2.1.3 🟢 Role-based authorization
- Built-in roles: TenantAdmin, CompensationAdmin, Manager, Rep, Viewer
- Claims-based permission checks on every endpoint
- Frontend route guards aligned with backend permissions
- Future: custom roles with granular permissions

#### 2.1.4 🟢 Internationalization (i18n)
- Three supported languages from launch: English, Spanish, Polish
- All user-facing strings via `@ngx-translate/core`
- Backend error messages translatable
- Future languages: Portuguese, French, German (post first 50 paying customers)

#### 2.1.5 🟢 Theme system
- Three themes: Light (default), Soft (sepia/warm), Dark
- System preference detection
- Persistent user choice in localStorage
- Tokens-only architecture; zero hardcoded colors

#### 2.1.6 🟢 Design system foundation
- 15 component primitives in `shared/ui/`
- Full token system (radii, spacing, typography, colors, shadows)
- `DESIGN_SYSTEM.md` document as binding contract
- `/__design-system` preview route for visual inspection

---

### 2.2 Core compensation modules

This is the heart of Wasnie. Every SPM product on the market has these.

#### 2.2.1 🟡 Compensation plan management
**Status:** Backend done, frontend in progress.

**Capabilities required:**
- Create, read, update, archive compensation plans
- Plan metadata: name, description, currency, effective period
- Plan versioning: every change creates a new version; old versions remain auditable
- Plan lifecycle: Draft → Active → Archived (one-way transitions)
- Plan cloning (clone existing version as new draft)
- Plan comparison view: see differences between two versions side by side
- Plan templates: pre-built starting points for common patterns (SaaS bookings plan, channel rebate plan, retail commission plan)

#### 2.2.2 🟡 Rule engine
**Status:** Domain model done, execution engine not yet implemented.

**Capabilities required:**
- Rules as data (JSON), not code
- Rule components: trigger (predicate), measurement, rate table, modifiers, cap, floor
- Rate table types: flat percentage, flat per-unit amount, tiered, attainment-based
- Modifier types: accelerators (factor > 1 above a threshold), decelerators, multipliers, SPIFFs (bonus amounts)
- Caps: maximum per transaction, maximum per period
- Floors: minimum guaranteed payouts
- Clawback rules: automatic recovery when a deal cancels within X days
- Rule ordering and dependencies within a plan
- Rule disable/enable without deletion (audit-friendly)

#### 2.2.3 🔴 Calculation engine — execution
**Status:** Not started. **This is the core competitive piece.**

**Capabilities required:**
- Deterministic calculation: same input + same plan version + same calculation time → same output, byte-for-byte
- Two execution modes:
  - **Real-time preview:** as a transaction enters, the rep sees the projected commission within seconds
  - **Batch close:** period-end official calculation, locked once approved
- Full traceability: every payout line stores the exact chain (transaction → credit → rule → measurement → rate → modifiers → cap/floor → final amount)
- Reprocessing: ability to recalculate a period under a new rule version (simulation only — never overwrites approved data)
- Calculation log: every run produces an immutable record with timestamp, user, parameters, result summary
- Performance target: 10,000 transactions × 50 reps × 5 rules calculated in under 60 seconds

#### 2.2.4 🟡 Quota management
**Status:** Domain model done, UI not started.

**Capabilities required:**
- Quotas per payee per period
- Multiple measurement types: revenue, units, margin, ACV, bookings
- Quota versioning and history
- Quota ramping (e.g., 50% in month 1, 75% in month 2, 100% from month 3 — for new hires)
- Quota adjustments mid-period (with audit trail)
- Quota overlap detection and prevention
- Aggregate views: total quota by team, by territory, by product

#### 2.2.5 🟡 Plan assignments
**Status:** Domain model done, UI not started.

**Capabilities required:**
- Assign a plan to a payee for a defined period
- Multiple plans can apply to the same payee simultaneously (e.g., base plan + SPIFF plan)
- Effective dating with no overlap conflicts
- Bulk assignment (assign a plan to all members of a team / role)
- Assignment history per payee
- Re-assignment workflow when a rep changes role

#### 2.2.6 🔴 Territory management
**Status:** Not started.

**Capabilities required:**
- Territory definition: geographic, account-based, vertical-based, hybrid
- Territory assignment to payees
- Territory effective dating
- Crediting rules: how a transaction maps to a territory (by account, by geography, by product)
- Territory rebalancing tools (visualizations, what-if analysis)
- ⚪ Future: AI-driven territory optimization (post first 100 customers)

---

### 2.3 Transaction and data modules

#### 2.3.1 🟡 Transaction ingestion
**Status:** Domain model done, ingestion APIs not yet built.

**Capabilities required:**
- Manual entry via UI
- CSV upload with column mapping and validation
- REST API for system-to-system integration
- Webhook receiver for real-time ingestion from CRM
- Native connectors (see Integrations module)
- Idempotency: same external ID + source = no duplicate
- Validation pipeline: required fields, currency consistency, date ranges, payee existence
- Error queue: invalid transactions held for review with clear error messages
- Bulk reprocessing of errored transactions after fixes

#### 2.3.2 🔴 Credit allocation
**Status:** Domain model done, allocation logic not implemented.

**Capabilities required:**
- Single payee crediting (simplest case)
- Multi-rep splits with percentages summing to 100%
- Role-based crediting: primary rep, overlay rep, manager rep
- Hierarchy-based crediting: manager gets X% of direct reports' deals
- Automatic credit allocation based on territory and role rules
- Manual credit override with approval workflow
- Split templates for common patterns

#### 2.3.3 🔴 Transaction lifecycle management
**Status:** Not started.

**Capabilities required:**
- Status flow: Pending → Eligible → Calculated → Paid → (optionally) Cancelled
- Cancellation handling with automatic clawback trigger
- Amendments: transaction value or attributes change after ingestion
- Period locking: once a period closes, transactions in it cannot be modified without explicit unlock
- Audit log of every state change

---

### 2.4 Payee and organization modules

#### 2.4.1 🟡 Payee management
**Status:** Domain model done, UI not started.

**Capabilities required:**
- Payee profile: full name, employee code, email, role, manager, hire date, termination date
- Payee status: Active, On Leave, Terminated
- Multiple identifiers per payee (employee ID, CRM ID, payroll ID)
- Payee import via CSV
- Future: HRIS integration (BambooHR, Workday, Personio)

#### 2.4.2 🔴 Organizational hierarchy
**Status:** Not started.

**Capabilities required:**
- Manager-report relationships
- Multi-level hierarchies
- Team groupings independent of hierarchy (e.g., "EMEA Enterprise team")
- Effective-dated hierarchy changes (track historical org structure for past calculations)
- Visual org chart

#### 2.4.3 🔴 Role and team management
**Status:** Not started.

**Capabilities required:**
- Define roles (AE, SDR, CSM, Sales Manager, Channel Manager, etc.)
- Define teams (geographic teams, product teams, segment teams)
- Bulk operations: apply plan to all members of a role or team
- Role-based plan defaults

---

### 2.5 Workflow modules

#### 2.5.1 🔴 Dispute management
**Status:** Not started. **High value differentiator.**

**Capabilities required:**
- Rep submits a dispute against a specific payout line with reason + comment + optional attachment
- Automatic routing to the right reviewer (rep's manager by default, configurable)
- Reviewer can: approve, reject, request more info
- Threaded comments per dispute
- SLA tracking: configurable target resolution time per tenant
- Dispute history per rep and per period
- Resolution status syncs to payout: approved dispute creates an adjustment
- Notifications: email + in-app, configurable
- Bulk view for managers: all open disputes on their team

#### 2.5.2 🔴 Approval workflows
**Status:** Not started.

**Capabilities required:**
- Period close approval (finance signs off on the run)
- Plan activation approval (compensation admin → finance → VP Sales)
- Adjustment approvals (above-threshold manual adjustments require manager + finance approval)
- Configurable approval chains per workflow type
- Delegation when an approver is out of office
- Approval audit trail (who approved what, when, with what comment)

#### 2.5.3 🔴 Adjustments and one-off payments
**Status:** Not started.

**Capabilities required:**
- Manual positive adjustments (bonuses, corrections in rep's favor)
- Manual negative adjustments (clawbacks, corrections against rep)
- Reason codes (configurable per tenant)
- Adjustments outside any plan (discretionary bonuses)
- Adjustment approval workflow integration
- Adjustments appear on the rep's statement with clear labeling

#### 2.5.4 🔴 Period close
**Status:** Not started.

**Capabilities required:**
- Define period (monthly, quarterly, annual)
- Pre-close checklist: outstanding disputes, pending approvals, unprocessed transactions
- Soft close: rep statements published, disputes window opens
- Hard close: period sealed, payments triggered, no further edits possible
- Reopen procedure with full audit trail (requires elevated permission + reason)

---

### 2.6 Visibility modules

#### 2.6.1 🟡 Admin dashboard
**Status:** Visual built with mock data, real data wiring pending.

**Capabilities required:**
- Period-to-date totals: total commissions, paid, pending, in dispute
- Quota attainment overview by team
- Top performers (period and YTD)
- Open disputes counter
- Pending approvals counter
- Recent activity feed
- Quick actions: run calculation, close period, import transactions, create plan

#### 2.6.2 🔴 Rep portal
**Status:** Not started. **Critical for product value.**

**Capabilities required:**
- Personal dashboard: this period's commission, YTD, attainment to quota
- Statement view: detailed breakdown by deal, by rule, by modifier
- Commission estimator: "if I close this deal worth X, what will I earn?"
- Pipeline view: deals in progress and projected commissions
- Quota progress bar with key milestones (next accelerator threshold)
- Dispute submission flow
- Statement download (PDF)
- Historical statements archive

#### 2.6.3 🔴 Manager portal
**Status:** Not started.

**Capabilities required:**
- Team performance view
- Direct reports' attainment and earnings
- Open disputes on the team
- Pending approvals for the manager
- Team ranking
- Forecast: projected team commissions for current period

#### 2.6.4 🔴 Reporting and analytics
**Status:** Not started.

**Capabilities required:**
- Standard report library (20+ predefined reports): payout summary, attainment by team, top performers, plan ROI, dispute analytics, etc.
- Custom report builder (drag-and-drop columns, filters, groupings)
- Scheduled report delivery via email
- Export to CSV, Excel, PDF
- Saved report views per user
- ⚪ Future: BI tool integration (Tableau, Power BI, Looker connectors)

---

### 2.7 Integration modules

#### 2.7.1 🔴 REST API
**Status:** Auth endpoints exist; full API surface to be built.

**Capabilities required:**
- Full CRUD on every domain entity
- Bulk endpoints for high-volume operations
- Webhook subscriptions: tenant subscribes to events (transaction.ingested, payout.calculated, dispute.created)
- API key management per tenant (multiple keys with scopes)
- Rate limiting per API key
- OpenAPI/Swagger documentation always in sync
- API versioning strategy (URL-based, /v1, /v2)
- API changelog and deprecation policy

#### 2.7.2 🔴 CRM connectors
**Status:** Not started.

**Connectors needed at launch:**
- HubSpot (priority — large mid-market presence)
- Salesforce (priority — table stakes for SPM)
- Pipedrive (LATAM and Europe heavy)

**Each connector provides:**
- Initial sync of historical deals
- Incremental sync (configurable interval: real-time webhook, hourly, daily)
- Field mapping configuration UI
- Conflict resolution (which side wins on amendments)
- Error queue and retry logic

#### 2.7.3 🔴 ERP and payroll connectors
**Status:** Not started.

**Connectors needed:**
- NetSuite (US mid-market)
- SAP Business One (LATAM and Europe SMB)
- QuickBooks Online (smaller companies)
- Generic CSV export for payroll providers (most flexible, lowest priority)
- ⚪ Future: Workday, Personio, BambooHR for HRIS sync

#### 2.7.4 🔴 Notification channels
**Status:** Not started.

**Channels required:**
- Email (transactional via SendGrid, Postmark, or similar)
- In-app notifications
- ⚪ Future: Slack integration (for dispute notifications, period close alerts)
- ⚪ Future: Microsoft Teams integration

#### 2.7.5 🔴 SSO providers
**Status:** Not started. Scale tier only.

**Providers required at launch of Scale tier:**
- Google Workspace (SAML)
- Microsoft Entra ID / Office 365 (SAML, OAuth)
- Okta (SAML)
- Generic SAML 2.0 endpoint

---

### 2.8 Administration modules

#### 2.8.1 🔴 Tenant settings
**Status:** Not started.

**Capabilities required:**
- Company profile (name, address, tax ID, billing email)
- Default currency, default locale, default time zone
- Fiscal period definition (calendar year vs custom fiscal year)
- Branding (logo, accent color — optional, paid tier)
- Notification preferences

#### 2.8.2 🔴 User management
**Status:** Backend exists for auth; UI not started.

**Capabilities required:**
- Invite users by email
- Role assignment per user
- Deactivate users (preserves history, blocks login)
- Bulk user import
- User activity log per tenant admin

#### 2.8.3 🔴 Audit log
**Status:** Not started. **Required for any serious customer.**

**Capabilities required:**
- Every state-changing action logged: who, what, when, before/after values
- Filterable by user, by entity, by action type, by date range
- Tamper-evident: log is append-only
- Export to CSV
- Retention: minimum 2 years for compliance
- Searchable

#### 2.8.4 🔴 Billing and subscription
**Status:** Not started.

**Capabilities required:**
- Stripe-based billing (subscriptions, invoices, payment methods)
- Tier upgrade and downgrade flows
- Usage tracking (active reps in the period)
- Overage charges or tier-up prompts when limits exceeded
- Invoice history per tenant
- Tax handling (VAT for EU, regional taxes for LATAM)

---

### 2.9 Differentiator modules (Wasnie-specific)

These are features that distinguish Wasnie from incumbents and that we will market explicitly.

#### 2.9.1 🔴 Simulation mode
**Status:** Not started.

**Description:** Run any plan against historical transactions without producing official payouts. Visualize impact of plan changes before activating them. Key competitive differentiator over Xactly (which requires consultants for what-if analysis).

#### 2.9.2 🔴 Plan templates marketplace
**Status:** Not started.

**Description:** Pre-built compensation plan templates for common patterns (SaaS new logo, SaaS renewal, channel partner, retail tiered, etc.). Templates are exportable and shareable between tenants. Reduces time-to-first-value to under an hour.

#### 2.9.3 🔴 Self-serve onboarding wizard
**Status:** Not started.

**Description:** A guided flow from tenant creation to first calculation in under two weeks, with zero consulting required. Steps: company setup → import payees → import a sample of transactions → choose a plan template → run a test calculation → see your first payouts. Onboarding completion tracked per tenant.

#### 2.9.4 🔴 Multi-currency native support
**Status:** Backend supports it via Money value object; full multi-currency UX not built.

**Description:** Each plan in its own currency. Conversion at calculation time using configurable FX provider. Statements presented in rep's preferred currency or plan currency. Important for LATAM companies with regional teams.

#### 2.9.5 🔴 AI-assisted plan builder
**Status:** Not started. **Future, post-launch.**

**Description:** Natural language plan creation: "Pay 5% on closed deals, 8% above 100K quarterly quota, with a 1.5x accelerator above 120% attainment." The AI parses this into structured rules. Differentiator vs incumbents whose engines are JSON-based and require admin training.

---

### 2.10 Quality, security, compliance modules

#### 2.10.1 🔴 Data retention and deletion
**Status:** Not started.

**Capabilities required:**
- Tenant-configurable retention periods per entity type
- Hard delete on tenant termination (GDPR right to erasure)
- Soft delete with retention window before hard delete
- Data export on tenant termination (GDPR data portability)

#### 2.10.2 🔴 GDPR compliance
**Status:** Not started.

**Capabilities required:**
- Privacy policy and terms in three languages
- Cookie consent (frontend)
- Right to access (rep can export their own data)
- Right to erasure (admin can anonymize a former employee)
- Data processing agreement template for tenants
- Subprocessor list maintained publicly

#### 2.10.3 ⚪ SOC 2 Type II
**Status:** Future, post first 10 paying customers.

**Description:** Standard security certification required by mid-market and enterprise buyers in the US. Estimated 12 months process, 30K to 80K EUR cost. Not blocking for initial launch but blocking for upmarket growth.

#### 2.10.4 ⚪ ISO 27001
**Status:** Future, post SOC 2.

**Description:** Standard security certification in Europe. Often expected by buyers in Germany, Nordics, and large companies in general.

#### 2.10.5 🟡 Encryption
**Status:** TLS in transit done by Azure; at-rest encryption pending review.

**Capabilities required:**
- TLS 1.2+ for all traffic
- Encryption at rest for SQL Server (TDE)
- Encryption at rest for any blob storage used (logos, attachments)
- Sensitive fields (payee SSN-equivalents, banking info) additionally encrypted at application layer

---

## 3. Build phases

The order matters. Each phase builds on the previous one. Do not skip phases.

### Phase 0 — Foundation ✅ Largely complete
- Multi-tenant scaffold
- Auth, identity, JWT
- i18n EN/ES/PL
- Theme system (light/soft/dark)
- Design system foundation
- Compensation domain model

### Phase 1 — Plans, the operational core (in progress)
- Plan list, create, detail, version views — frontend wiring to backend
- Rule form with live preview (Tiered, Flat, Attainment-based)
- Modifiers, caps, floors
- Plan activation, archival, cloning
- Quota management UI
- Plan assignments UI
- Payee management UI

### Phase 2 — Transactions and ingestion
- Manual transaction entry UI
- CSV import with validation
- Transaction list and detail views
- Credit allocation rules
- Status lifecycle UI

### Phase 3 — Calculation engine (the heart)
- Rule evaluation engine in C# (deterministic, fully tested)
- Calculation runs management UI
- Payout list and detail with full traceability
- Simulation mode

### Phase 4 — Visibility
- Rep portal with statements, estimator, dispute submission
- Manager portal
- Reporting library (10 standard reports)
- Custom report builder (basic)

### Phase 5 — Workflows
- Dispute management end-to-end
- Approval workflows
- Adjustments
- Period close

### Phase 6 — Integrations
- REST API surface complete with OpenAPI
- HubSpot connector
- Salesforce connector
- Generic webhook ingestion

### Phase 7 — Administration
- Tenant settings UI
- User management UI
- Audit log
- Billing integration (Stripe)
- Self-serve onboarding wizard

### Phase 8 — Polish and differentiators
- Plan templates marketplace
- Multi-currency UX
- AI plan builder (natural language)
- Help center and in-app guidance
- Marketing site

### Phase 9 — Compliance and certifications
- GDPR full compliance
- SOC 2 Type II audit
- ISO 27001 audit
- Penetration testing

---

## 4. Current state — May 2026

### What is already built

**Backend (WasnieApi):**
- ASP.NET Core .NET 8 with Clean Architecture across four projects (Domain, Application, Infrastructure, Api)
- Multi-tenancy: discriminator column, JWT-based tenant context, EF Core global filters
- Authentication: ASP.NET Identity, JWT with SQL-backed refresh tokens
- Compensation domain model: Plan (aggregate), Rule, Trigger, Measurement, RateTable, Modifier, Cap, Floor, Quota, PlanAssignment, Transaction, Credit, Payout, PayoutLine — all in their own files, all with EF Core configurations
- Value objects: Money, Percentage, DateRange, PayeeReference, RuleSnapshot, ModifierApplication
- Application layer: commands, queries, handlers, validators, manual mappers for plans, quotas, assignments
- REST API endpoints for auth, plans, quotas, assignments
- Code-First migrations applied to SQL Server (HEYBALDUR\WasnieDb)

**Frontend (WasnieUi):**
- Angular 20 standalone components with Signals
- 15 design system primitives in `shared/ui/`
- Three theme modes wired (light, soft, dark)
- Three languages wired (en, es, pl)
- Login and register-tenant pages
- Dashboard with mocked data
- Plan list, create, detail pages wired to backend
- Rule form with live preview
- `/__design-system` preview route
- `DESIGN_SYSTEM.md` contract document

**Infrastructure:**
- Local development: SQL Server local (HEYBALDUR), Angular dev server on 4200 with proxy to API on 5000
- Production: not yet deployed (Azure App Service is the target)

### Known gaps to close in current Plans iteration

- Dispute submission flow (UI placeholder only)
- Versions tab in plan detail (table exists, comparison view not built)
- Plan templates (none yet)
- Multi-currency in UI is partial

### Next planned iteration

**Phase 1 completion:** Quota UI + Payee UI + Plan Assignment UI. After that, move to Phase 2 (Transactions + Ingestion) which unblocks Phase 3 (Calculation Engine).

---

## 5. Out of scope (explicit)

To avoid scope creep, these are explicitly NOT part of Wasnie:

- General-purpose CRM functionality (use HubSpot, Salesforce, Pipedrive — Wasnie integrates with them)
- Payroll processing (Wasnie outputs payable amounts; payroll providers process payments)
- General-purpose HRIS (Wasnie reads from HRIS; we don't replace BambooHR)
- General-purpose BI (Wasnie produces reports; for deep analytics, export to Tableau/PowerBI)
- Sales forecasting (CaptivateIQ has it; we may add it later, not at launch)
- Sales coaching or training content
- Lead management
- Quote-to-cash / CPQ functionality
- Contract lifecycle management

---

## 5b. Immutable rules (binding policy)

### 5b.1 Plan lifecycle and modification rules

Plans follow a strict status-based modification policy. These rules are enforced both in the backend (domain invariants) and in the frontend (UI affordance):

| Plan status | Allowed modifications |
|---|---|
| Draft | All edits permitted. Plan, rules, assignments are mutable. |
| Active | No modifications allowed. To change, clone as a new version. Plan can be archived. |
| Archived | Fully immutable. Can only be cloned as a new draft version. |

### 5b.2 Plan deletion policy

- Draft plans with zero rules and zero assignments → deletable by TenantAdmin.
- Draft plans with rules or assignments → not deletable. Must be archived.
- Active and Archived plans → never deletable. They persist forever for audit purposes.

### 5b.3 Status-aware UX

The frontend must reflect these rules through **affordance**, not through error messages after the fact. Forbidden actions are hidden from the UI. A user must never spend time filling out a form whose outcome is rejected at submit time.

This rule applies to plans, rules, modifiers, quotas, and any entity with a lifecycle state. The principle is: **the UI tells the user what they can do, not what they cannot do.**

The centralized permission source of truth is `WasnieUi/src/app/features/plans/services/plan-permissions.ts`. Every component that gates plan-related actions reads from `getPlanPermissions(status)`. No component duplicates this logic.

---

## 6. Success criteria for a market-ready product

Wasnie is "market-ready" — i.e., sellable to the first 10 paying customers — when:

1. A new tenant can sign up, configure their company, import payees and transactions, and produce their first calculated payouts within two weeks, without any human help from Wasnie's side.
2. Rep portal allows every payee to see their commissions and submit disputes.
3. Dispute resolution flow is end-to-end functional.
4. Period close produces locked, auditable results.
5. At least HubSpot and Salesforce connectors work for one-way transaction sync.
6. Billing via Stripe is operational.
7. Audit log records every state-changing action.
8. Public documentation site exists with at least 30 articles covering setup, plan design, and operations.
9. GDPR-compliant (privacy policy, terms, data export, data deletion flow).
10. Production deployment on Azure with monitoring, automated backups, and 99.5% uptime target.

That is the bar for **first revenue**, not for enterprise readiness. Enterprise readiness adds SOC 2, ISO 27001, SSO, advanced approvals, and territory optimization.

---

## 7. Glossary

| Term | Meaning |
|---|---|
| **SPM** | Sales Performance Management — broad category covering compensation, quotas, territories, planning |
| **ICM** | Incentive Compensation Management — narrower than SPM, focused on commission calculation |
| **Payee** | A person who earns commissions (typically a sales rep) |
| **Plan** | A versioned compensation policy attached to one or more payees |
| **Rule** | A single calculation unit inside a plan |
| **Trigger** | The predicate that determines when a rule applies to a transaction |
| **Measurement** | The metric on which commission is calculated (revenue, units, margin, etc.) |
| **Rate table** | The structure that converts measurement into commission amount |
| **Modifier** | An adjustment to base commission (accelerator, multiplier, SPIFF) |
| **Cap** | Maximum commission per transaction or period |
| **Floor** | Minimum guaranteed commission |
| **Quota** | A sales target assigned to a payee for a period |
| **Attainment** | How much of a quota the payee has achieved (typically as a percentage) |
| **SPIFF** | "Sales Performance Incentive Fund" — a one-time bonus tied to a specific behavior or product |
| **Clawback** | Recovery of previously paid commission when the underlying deal cancels |
| **Transaction** | An ingested deal/order eligible for commission |
| **Credit** | The allocation of a transaction (or part of it) to a specific payee |
| **Split** | Multiple payees sharing credit for a single transaction |
| **Overlay** | An additional rep earning commission on top of the primary rep (e.g., specialist) |
| **Payout** | A calculated commission record for a payee for a period |
| **PayoutLine** | A single line item inside a payout showing one (credit + rule) calculation chain |
| **Period close** | The act of finalizing a calculation period and locking it from changes |
| **Dispute** | A rep's formal challenge to a specific payout line |
| **Adjustment** | A manual modification to a payout (positive or negative) |
| **Tenant** | A customer organization using Wasnie (multi-tenant isolation boundary) |

---

## 8. References to external materials

- **Design system contract:** `WasnieUi/DESIGN_SYSTEM.md`
- **Backend solution:** `WasnieApi/Wasnie.sln`
- **API base path (dev):** `http://localhost:5000/api`
- **Frontend (dev):** `http://localhost:4200`
- **Production target:** Azure App Service, West Europe

---

*This document is owned by Rodolfo A. Calvo Jaubert and updated as scope evolves. Treat it as a contract: any feature work that contradicts this document must update the document first.*
