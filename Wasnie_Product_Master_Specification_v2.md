# Wasnie — Product Master Specification

**Document version:** 2.0 (revised and hardened)
**Last updated:** May 2026
**Owner:** Rodolfo A. Calvo Jaubert
**Status:** Binding contract — must be updated before any scope change

---

## 0. Document purpose and discipline

This document is the **single source of truth** for what Wasnie is, what it must do, and how it must behave. It supersedes all previous specifications, conversations, and assumptions.

Three rules of use:

1. **No feature work begins without this document being current.** If the scope changes, this document changes first.
2. **Every Claude Code session must read this document plus `WasnieUi/DESIGN_SYSTEM.md` before doing UI work.**
3. **Section 5b (immutable rules) is non-negotiable.** It exists because we are building software that handles money. A bug here is not a bug — it is a legal liability.

---

## 1. What Wasnie is

Wasnie is a **multi-tenant Sales Performance Management (SPM) and Incentive Compensation Management (ICM) platform** for mid-market sales organizations.

In one sentence: Wasnie automates the design, calculation, approval, and reporting of variable sales compensation for companies that have outgrown spreadsheets but cannot afford enterprise tools or six-month implementations.

This is **financial software**. It calculates real money that real people receive as their livelihood. A bug in Wasnie can:

- Cause an employee to miss rent
- Trigger a labor dispute
- Trigger a tax audit
- Break the contractual relationship between an employer and its sales team
- Expose Wasnie (the company) and Rodolfo (its founder) to civil liability

This framing must be present in every architectural and product decision.

---

## 2. Strategic context

### 2.1 Market reality (May 2026)

- Global SPM/ICM market: 3.2B USD in 2025, growing 10.4-16.7% CAGR
- Projection to 2030-2034: 7.8B to 12B USD
- Approximately 47-70% of mid-market companies still use spreadsheets for commission management

The market is real and growing. The opportunity is also real, but it is **not empty** — it is contested by a fast-evolving set of competitors who have already neutralized many of the differentiators Wasnie initially planned to claim.

### 2.2 Competitive landscape — realistic, not aspirational

#### Enterprise incumbents (NOT Wasnie's target)

| Vendor | Position | Wasnie's stance |
|---|---|---|
| Xactly Incent | Enterprise, 200+ reps, $200K-1M ACV | Out of scope; we do not compete here |
| Varicent | Enterprise, complex, large customers | Out of scope |
| SAP Commissions | SAP-native enterprises | Out of scope |
| SAP CallidusCloud (retiring Dec 2026) | Enterprise, migrating to Varicent / CaptivateIQ Enterprise | **Out of scope. These customers will not migrate to a one-person startup.** |

#### Mid-market direct competitors (THE REAL FIGHT)

| Vendor | Strengths | Where Wasnie can differentiate |
|---|---|---|
| **CaptivateIQ** (US, well-funded) | Spreadsheet-like flexible engine, mid-to-upper market, named Forrester Wave Leader Q1 2025, $100M+ Series C | Geography (LATAM, Eastern Europe), language depth, local pricing, local timezone support |
| **Qobra** (France, growing fast in Europe) | No-code plan editor, real-time rep simulator, native HubSpot/Salesforce/Pipedrive bi-directional, strong EU presence | LATAM coverage Qobra lacks; Polish and broader Eastern European languages; specific verticals |
| **Everstage** (India + global) | Workflow automation, modern UX, aggressive expansion into emerging markets, $30M Series B 2023 | Direct competition; differentiation harder; vertical focus essential |
| **Spiff** (acquired by Salesforce 2024) | Salesforce-native, modern UI | Wasnie targets non-Salesforce shops or shops wanting CRM-agnostic option |
| **Performio** | Mid-market established player, 18 years in market | Wasnie offers more modern stack and UX |
| **QCommission** | SMB, low-cost | Wasnie targets a slightly higher tier with better experience |

#### The real volume competitor: Excel + custom scripts

The most likely tool a Wasnie prospect uses today is not a competitor — it is **a Google Sheet maintained by the Head of Sales Ops, plus a few VLOOKUPs, plus an annual fight with finance about which numbers are correct**. This is what Wasnie actually replaces in 70% of deals.

### 2.3 Wasnie's defensible positioning

Wasnie does **not** win by being "cheaper Xactly". Wasnie wins by:

1. **Geographic and linguistic specialization** — first-class Spanish, Polish, and Portuguese; multi-currency UX designed for LATAM realities (high inflation, volatile FX); support in local timezones
2. **Vertical depth** — one industry chosen well, with pre-built plan templates, regulatory awareness, and reference customers in that vertical. Initial target verticals to choose from (pick exactly ONE before launch):
   - Insurance brokerages (Spain, Mexico, Colombia, Poland)
   - SaaS B2B (LATAM)
   - Real estate brokerages (LATAM)
   - Fintech and lending (LATAM, Spain)
3. **Assisted onboarding** — not "self-serve everything" (a known fallacy in this category), but a guided experience where the Wasnie team handles complex plan setup in 48 hours rather than leaving the customer to fight a configuration UI alone
4. **Audit-first architecture** — every cent traceable, every rule versioned, every calculation reproducible — built in from day one, not bolted on for compliance later

### 2.4 What Wasnie is NOT

These positionings are explicitly **abandoned** based on 2026 market analysis:

- ❌ **"Pure self-serve in 2 weeks"** — Mid-market compensation plans are never simple. The political and historical complexity of comp plans makes 80% self-serve a fantasy. Customers fear configuring their own engine because the cost of error is their job.
- ❌ **"Capture the SAP CallidusCloud migration wave"** — CallidusCloud customers are enterprise. They will not move to a young startup. This is not Wasnie's opportunity.
- ❌ **"Compete on price alone"** — Pricing too low destroys unit economics and signals lack of seriousness for financial software. Customers handling commissions do not want the cheapest option; they want the safest option at a reasonable price.

---

## 3. Pricing model (revised for unit economics viability)

The previous version of this document proposed 8 EUR/rep/month at the entry tier. **This was incorrect.** At that price, customer acquisition cost (CAC) and onboarding labor exceed annual contract value for years, making the business unviable without venture capital.

Revised pricing model:

| Tier | Reps | Base monthly (EUR) | Includes |
|---|---|---|---|
| **Starter** | Up to 25 | **300 EUR/month** flat | Core SPM, assisted onboarding, 1 admin, basic reporting, email support |
| **Growth** | 26-75 | **750 EUR/month** flat | Everything in Starter + integrations, multi-admin, advanced reporting, simulator, plan templates |
| **Scale** | 76-150 | **1,500 EUR/month** flat | Everything in Growth + SSO, audit log access, API, priority support, dedicated CSM check-ins |
| **Enterprise** | 150+ | Custom (from 2,500/month) | Custom contract, dedicated CSM, SLA, custom integrations, advanced compliance |

**Setup fee:** 800-2,500 EUR one-time per tenant, mandatory. Covers initial plan configuration done by Wasnie (assisted onboarding model). This is not optional and not negotiable for sub-Enterprise tiers. Industry standard, not punishment.

**Why this works:**
- Starter ARR per customer: 3,600 EUR + 800 EUR setup = 4,400 EUR Year 1, 3,600 EUR ongoing
- Growth ARR per customer: 9,000 EUR + 1,500 EUR setup = 10,500 EUR Year 1
- These ACVs make outbound sales economically viable; lower would not

Annual billing default; monthly billing available at +15% premium. Public pricing on the website. No "contact sales" wall except for Enterprise.

---

## 4. Target market — sharpened

### 4.1 Primary geography (first 50 customers)

In order of priority:

1. **Spain** — Wasnie's native language, regulatory environment manageable, mid-market SaaS and insurance sectors mature
2. **Mexico** — large mid-market, Spanish-speaking, growing SaaS sector, weak local SPM competition
3. **Colombia** — similar profile to Mexico, growing fintech and insurance
4. **Poland** — Rodolfo's home country, native language, growing SaaS sector, no local SPM player
5. **Chile, Peru, Argentina** — opportunistic, smaller markets
6. **Portugal, Czech Republic, Romania** — opportunistic European expansion

### 4.2 Vertical focus — choose ONE before launch

The strategic decision Wasnie must make before pursuing first customers:

- **Option A: Insurance brokerages and agencies** — heavily regulated (which means standardized commission structures, easier to template), commission-driven business model is core to the industry, weak digital tooling currently
- **Option B: SaaS B2B mid-market** — modern, digital-native buyers, faster sales cycles, but also more competitive against CaptivateIQ/Qobra
- **Option C: Real estate brokerages** — pure commission businesses, large team sizes, less competition from incumbents, but lower budgets per company
- **Option D: Fintech and lending** — sophisticated buyers, strong compliance needs, higher willingness to pay

**Decision required:** Rodolfo selects one before first customer acquisition campaign begins.

### 4.3 Anti-target

Wasnie explicitly **does not** target:

- Companies under 15 reps (Excel suffices)
- Companies over 250 reps (need full enterprise SPM)
- US Fortune 1000 (incumbents own this segment; Wasnie cannot compete)
- Industries with extreme compliance complexity (defense contractors, etc.)

---

## 5. Functional modules

This section catalogs every module Wasnie needs to be a credible SPM product. Each module is tagged:

- 🟢 **Built** — already implemented and functional
- 🟡 **Partial** — partially implemented, needs completion
- 🔴 **Not started** — to be built
- ⚪ **Future** — explicitly out of scope for initial launch, planned later
- 🚨 **Critical-Risk** — module where bugs translate directly to financial or legal liability; requires extra rigor (tests, audit, peer review)

---

### 5.1 Foundation modules

#### 5.1.1 🟢 Multi-tenant architecture
- Discriminator column on every entity
- Global EF Core query filters
- Tenant context per request from JWT
- Tenant provisioning during registration

#### 5.1.2 🟢 Identity and authentication 🚨
- ASP.NET Core Identity, SQL Server
- JWT bearer + SQL-backed refresh tokens, revocable
- Tenant-scoped accounts
- Password hashing per ASP.NET Identity defaults (PBKDF2)
- Future: SSO (SAML, OAuth) at Scale tier

#### 5.1.3 🟢 Role-based authorization
- Built-in roles: TenantAdmin, CompensationAdmin, Manager, Rep, Viewer
- Permission checks on every endpoint
- Frontend route guards aligned with backend
- Future: custom roles with granular permissions

#### 5.1.4 🟢 Internationalization
- Three launch languages: English, Spanish, Polish
- All user-facing strings via `@ngx-translate/core`
- Translatable backend error messages
- Future: Portuguese, French, German (post 50 paying customers)

#### 5.1.5 🟢 Theme system
- Light, Soft, Dark + system preference
- Tokens-only architecture, zero hardcoded colors
- Persistent user choice

#### 5.1.6 🟢 Design system foundation
- 15 component primitives
- Token system (radii, spacing, typography, colors, shadows)
- `DESIGN_SYSTEM.md` as binding contract
- `/__design-system` preview route

---

### 5.2 Core compensation modules

#### 5.2.1 🟡 Compensation plan management 🚨
**Status:** Backend done; frontend in progress.

- CRUD with strict status transitions (Draft → Active → Archived)
- Plan versioning, every change a new version
- Immutability invariants enforced (Active plans never modified; mutation requires cloning)
- Cloning across versions
- Comparison view between two versions
- Plan templates per vertical (post vertical decision)

#### 5.2.2 🟡 Rule engine — model 🚨
**Status:** Domain model done; execution engine not yet implemented.

- Rules as data (JSON), not code
- Components: trigger, measurement, rate table, modifiers, cap, floor
- Rate table types: flat, tiered, attainment-based
- Modifier types: accelerator, decelerator, multiplier, SPIFF
- Caps, floors, clawback rules
- Rule ordering and dependencies

#### 5.2.3 🔴 Calculation engine — execution 🚨
**Status:** Not started. **The most critical-risk module in the product.**

Requirements:

- **Deterministic:** same input + same plan version + same calculation timestamp → same output, byte-for-byte. No randomness, no time-of-day dependencies, no floating-point arithmetic on currency.
- **Currency precision:** all money operations use `decimal` with 4 decimal places of internal precision, 2 decimal places of display precision. Rounding mode banker's rounding (half-to-even) for fairness. **NEVER use `double` or `float` for money.**
- **Reproducibility:** every payout line stores the complete calculation chain (transaction, credit, rule version snapshot, measurement, rate, modifiers applied, cap, floor, final amount) — calculation can be reproduced months or years later from the stored data alone, without consulting any external state.
- **Two execution modes:**
  - Real-time preview (rep dashboard): non-authoritative, advisory only
  - Batch authoritative (period close): produces the final, legally-binding payouts
- **Performance target:** 10,000 transactions × 50 reps × 5 rules in under 60 seconds
- **Concurrency:** calculation runs are serialized per tenant; no two simultaneous official calculations on the same period
- **Idempotency:** running the same calculation twice produces identical results; the second run does not create duplicate payouts

This module **must** have unit tests (the only module where the "no tests" project policy is overridden — see section 5b.4). Test coverage target: every rate table variant, every modifier combination, every edge case (zero, negative, very large numbers, currency edge cases).

#### 5.2.4 🟡 Quota management 🚨
**Status:** Domain model done; UI not started.

- Quotas per payee per period
- Multiple measurement types
- Quota versioning and history
- Quota ramping (new hires)
- Mid-period adjustments with audit
- Aggregate views by team/territory/product

#### 5.2.5 🟡 Plan assignments 🚨
**Status:** Domain model done; UI not started.

- Assign plan to payee for defined period
- Multiple plans per payee simultaneously
- Effective dating with no overlap conflicts
- Bulk assignment
- Assignment history per payee
- Re-assignment workflow on role change

#### 5.2.6 🔴 Territory management
**Status:** Not started.

- Territory definition: geographic, account, vertical, hybrid
- Assignment to payees with effective dates
- Crediting rules per territory
- Rebalancing tools and what-if analysis
- ⚪ Future: AI-driven optimization

#### 5.2.7 🔴 Effective dating and temporal modeling 🚨
**Status:** Not started. **Major technical risk previously underestimated.**

Real-world compensation is temporally messy:
- A deal closes in March, gets modified in May, retroactively affects January's quota attainment
- A rep changes territory in April; deals booked in February still need to be credited against the February territory
- A plan version changes mid-quarter; transactions before the change must use the old version, transactions after the change use the new

Requirements:

- Every business-relevant entity must be **bitemporally tracked**: it has a business validity period (when this fact was true in the world) and a system recording period (when Wasnie learned about it). These two timelines never collapse into one.
- Calculations always specify a temporal context: "calculate as of Date X using data known as of Date Y". This is critical for reproducing past calculations and for what-if analysis without polluting official records.
- Adjustments to closed periods generate new records (Adjustment entities) — they never overwrite past payouts. The historical payout remains; the adjustment is a separate, additive event.

**This is non-negotiable architecture.** A mistake here makes the entire calculation engine untrustworthy.

---

### 5.3 Transaction and data modules

#### 5.3.1 🟡 Transaction ingestion 🚨
**Status:** Domain model done; ingestion APIs and UIs not yet built.

- Manual entry via UI
- CSV upload with mapping and validation
- REST API for system-to-system
- Webhook receiver for real-time
- Native connectors (separate module)
- **Idempotency:** unique constraint on (TenantId, Source, ExternalId)
- Validation pipeline: required fields, currency consistency, date ranges, payee existence
- Error queue for invalid transactions with reproducible reprocessing
- Currency conversion at ingestion or at calculation time (tenant choice)

#### 5.3.2 🔴 Credit allocation 🚨
**Status:** Domain model done; allocation logic not implemented.

- Single payee, splits, overlays, manager credit
- Hierarchy-based crediting
- Automatic allocation based on territory and role
- Manual override with approval workflow
- Split templates

#### 5.3.3 🔴 Transaction lifecycle 🚨
**Status:** Not started.

- Status flow: Pending → Eligible → Calculated → Paid → (optionally) Cancelled
- Cancellation triggers clawback evaluation
- Amendments preserve history
- Period locking with explicit unlock procedure
- Full audit log of every state change

---

### 5.4 Payee and organization modules

#### 5.4.1 🟡 Payee management 🚨
**Status:** Domain model done; UI not started.

- Profile, employee code, role, manager, dates
- Status: Active, On Leave, Terminated
- Multiple identifiers per payee
- CSV import
- Future: HRIS integration

#### 5.4.2 🔴 Organizational hierarchy 🚨
**Status:** Not started.

- Manager-report relationships, multi-level
- Team groupings independent of hierarchy
- **Effective-dated hierarchy changes** (critical for historical accuracy of past calculations)
- Visual org chart

#### 5.4.3 🔴 Role and team management
**Status:** Not started.

- Role and team definitions
- Bulk operations
- Role-based plan defaults

---

### 5.5 Workflow modules

#### 5.5.1 🔴 Dispute management 🚨
**Status:** Not started. High value, high visibility.

- Rep submits dispute against a payout line
- Automatic routing to manager
- Approve / reject / request info actions
- Threaded comments, attachments
- Configurable SLA per tenant
- History per rep and per period
- Resolution generates an adjustment (not a payout edit)
- Email and in-app notifications

#### 5.5.2 🔴 Approval workflows 🚨
- Period close approval
- Plan activation approval
- Adjustment approvals
- Configurable approval chains
- Delegation when approver out of office
- Full audit trail

#### 5.5.3 🔴 Adjustments and one-off payments 🚨
- Manual positive and negative adjustments
- Configurable reason codes
- Discretionary bonuses outside any plan
- Approval workflow integration
- Clear labeling on rep statements

#### 5.5.4 🔴 Period close 🚨
- Monthly, quarterly, annual periods
- Pre-close checklist (disputes, approvals, errored transactions)
- Soft close (statements published, disputes window)
- Hard close (sealed, payments triggered, no edits)
- Reopen procedure with elevated permission + reason + full audit

---

### 5.6 Visibility modules

#### 5.6.1 🟡 Admin dashboard
**Status:** Visual built with mock data; real data wiring pending.

- Period-to-date totals
- Quota attainment overview by team
- Top performers
- Open disputes counter
- Pending approvals counter
- Recent activity feed
- Quick actions

#### 5.6.2 🔴 Rep portal 🚨
**Status:** Not started. Critical for product value.

- Personal dashboard with attainment
- Statement view with full breakdown
- Commission estimator ("what if I close this deal?")
- Pipeline projections
- Quota progress and milestone alerts
- Dispute submission
- Statement PDF download
- Historical statements archive
- **Disclaimer on all forward-looking views:** "Estimates are not guaranteed. Final commission is determined by official period close calculation."

#### 5.6.3 🔴 Manager portal
**Status:** Not started.

- Team performance
- Direct reports' attainment and earnings
- Team disputes and approvals
- Team ranking
- Period commission forecast

#### 5.6.4 🔴 Reporting and analytics
**Status:** Not started.

- 20+ standard reports
- Custom report builder
- Scheduled email delivery
- Export CSV, Excel, PDF
- Saved views per user
- ⚪ Future: BI tool connectors (Tableau, Power BI, Looker)

---

### 5.7 Integration modules

#### 5.7.1 🔴 REST API 🚨
**Status:** Auth endpoints done; full surface pending.

- Full CRUD on every entity
- Bulk endpoints
- Webhook subscriptions (transaction.ingested, payout.calculated, dispute.created, etc.)
- API key management per tenant with scopes
- Rate limiting per key
- OpenAPI/Swagger always in sync
- API versioning (URL-based, /v1, /v2)
- Deprecation policy: 12 months minimum notice for breaking changes

#### 5.7.2 🔴 CRM connectors 🚨
- HubSpot (priority)
- Salesforce (table stakes)
- Pipedrive (LATAM/Europe presence)
- Each connector: initial sync, incremental sync, field mapping UI, conflict resolution, error queue
- **Tolerance to schema drift:** customers customize Salesforce/HubSpot constantly. Connectors must degrade gracefully when fields disappear or change type, with clear error messages and manual remapping options.

#### 5.7.3 🔴 ERP and payroll connectors
- NetSuite
- SAP Business One
- QuickBooks Online
- Generic CSV export for payroll providers
- ⚪ Future: Workday, Personio, BambooHR

#### 5.7.4 🔴 Notification channels
- Email (transactional)
- In-app notifications
- ⚪ Future: Slack, Microsoft Teams

#### 5.7.5 🔴 SSO providers (Scale tier and above)
- Google Workspace SAML
- Microsoft Entra ID / Office 365
- Okta
- Generic SAML 2.0

---

### 5.8 Administration modules

#### 5.8.1 🔴 Tenant settings
- Company profile, tax ID, billing email
- Default currency, locale, time zone
- Fiscal period definition
- Optional branding (paid tiers)
- Notification preferences

#### 5.8.2 🔴 User management
- Invite by email
- Role assignment
- Deactivate (preserve history)
- Bulk import
- Per-tenant activity log

#### 5.8.3 🔴 Audit log 🚨
**Required from day one.** Without this, no serious customer signs.

- Every state-changing action logged: who, what, when, before/after values
- Filterable by user, entity, action type, date
- Append-only, tamper-evident
- Minimum 2-year retention; configurable up to 10 years per tenant
- Export to CSV
- Searchable

#### 5.8.4 🔴 Billing and subscription
- Stripe-based
- Tier upgrade/downgrade flows
- Active-rep usage tracking
- Tier-up prompts (no surprise overages)
- Invoice history
- Tax handling (VAT EU, regional LATAM)

---

### 5.9 Differentiator modules

#### 5.9.1 🔴 Simulation mode 🚨
**Critical differentiator.** Run any plan against historical transactions without producing official payouts. Visualize impact of plan changes before activating. Stripe what-if analysis.

The simulation engine and the production engine share the same code path but write to different output stores. **Simulation output is always clearly labeled and never accessible to reps.**

#### 5.9.2 🔴 Plan templates marketplace
Vertical-specific templates (insurance, SaaS, etc.) with one-click setup. Reduces time-to-first-value drastically.

#### 5.9.3 🔴 Assisted onboarding flow (NOT pure self-serve)
Combination of:
- Wizard-style guided setup for the customer
- A Wasnie operator (initially Rodolfo, later a Customer Success person) performs final plan configuration
- 48-hour turnaround commitment from contract signature to first calculation
- Customers can self-serve simple plans, but complex plans go through Wasnie team

This corrects the previous "100% self-serve" assumption that does not match market reality.

#### 5.9.4 🔴 Multi-currency native support 🚨
- Each plan in its own currency
- FX conversion at configurable rates (daily, monthly, locked-at-period-start)
- FX provider integration (e.g., openexchangerates.org, or custom uploadable tables)
- Statements presented in rep's preferred currency or plan currency
- **Critical for LATAM:** support locked rates (anti-inflation protection) and configurable rounding rules

#### 5.9.5 ⚪ AI-assisted plan builder
**Future.** Natural-language plan creation. Post-launch differentiator, not blocker.

---

### 5.10 Quality, security, compliance modules

#### 5.10.1 🔴 Data retention and deletion 🚨
- Configurable retention per entity type
- Tenant termination triggers data export + hard deletion (GDPR)
- Soft delete with retention window
- Cryptographic erasure for sensitive blobs

#### 5.10.2 🔴 GDPR compliance 🚨
- Privacy policy and terms in three languages
- Cookie consent
- Right to access (rep can export own data)
- Right to erasure (admin anonymizes former employee while preserving aggregate calculation history)
- Data Processing Agreement template
- Public subprocessor list
- DPO designation (initially Rodolfo)

#### 5.10.3 ⚪ SOC 2 Type II
**Required by month 12-18.** Standard mid-market US/EU requirement. Estimated 30-80K EUR cost, 12 months process. Without SOC 2, growth ceiling at ~20 customers.

#### 5.10.4 ⚪ ISO 27001
**Required by month 24-36.** European mid-market and large company expectation. Often co-pursued with SOC 2 to share evidence collection.

#### 5.10.5 🟡 Encryption 🚨
- TLS 1.2+ for all traffic
- Encryption at rest for SQL Server (TDE)
- Encryption at rest for blob storage
- Application-layer encryption for highly sensitive fields (banking info if stored, tax IDs)
- Key management via Azure Key Vault

#### 5.10.6 🔴 Backup and disaster recovery 🚨
- Automated daily backups, retained 30 days
- Point-in-time recovery for SQL Server
- Documented Recovery Time Objective: 4 hours
- Documented Recovery Point Objective: 1 hour
- Quarterly DR drills (restore to staging, verify integrity)

#### 5.10.7 🔴 Monitoring, alerting, observability 🚨
- Application Insights (or equivalent) for backend
- Frontend error tracking (Sentry or similar)
- Calculation engine alerts: any failure on an official calculation run is P0
- Database query performance monitoring
- Uptime monitoring (UptimeRobot or similar)

---

## 5b. Immutable rules (binding policy)

These rules are non-negotiable. They exist because Wasnie processes money. Violations are not bugs; they are liabilities.

### 5b.1 Plan lifecycle and modification

| Plan status | Permitted modifications |
|---|---|
| Draft | All edits permitted |
| Active | No modifications. Period. To change, clone as new version. Can be archived. |
| Archived | Fully immutable. Can only be cloned as new draft. |

### 5b.2 Plan deletion

- Draft plans with zero rules and zero assignments → deletable by TenantAdmin
- Draft plans with rules or assignments → not deletable; must be archived
- Active or Archived plans → never deletable

### 5b.3 Payout immutability

- An approved payout cannot be modified. Period.
- Corrections to approved payouts are made via Adjustment entities (additive), not by editing the original payout
- Reopening a closed period requires elevated permission + recorded reason + full audit trail

### 5b.4 Testing policy override for critical-risk modules

The project's general "no unit tests" policy does **not** apply to modules tagged 🚨 Critical-Risk. These modules **must** have comprehensive unit tests, integration tests, and where applicable property-based tests. Specifically:

- Calculation engine: 100% coverage of every rate table variant, modifier combination, cap, floor, clawback path
- Money value object: every arithmetic operation, every currency edge case
- Effective dating logic: every temporal transition scenario
- Credit allocation: every split percentage scenario, every overlay configuration

A failing test on a critical-risk module is a build-breaking event. No exceptions.

### 5b.5 Currency handling

- All money in domain code uses `Money` value object (decimal with 4 internal decimals, 2 display decimals)
- Operations between different currencies throw `DomainException` unless explicit conversion is performed via documented FX module
- Rounding mode: banker's rounding (half-to-even) for all financial calculations
- `double` and `float` are forbidden anywhere money is involved
- Display rounding never affects stored values

### 5b.6 Status-aware UI

The frontend tells the user what they **can** do, not what they cannot. Forbidden actions are hidden, not disabled. This rule applies to plans, rules, modifiers, quotas, payouts, disputes, and all entities with a lifecycle state.

### 5b.7 Audit trail

Every state-changing operation generates an audit log entry. This is not optional. Audit log is append-only and tamper-evident. Customers must be able to answer "who changed what, when, why" for any operation in the system, in court if necessary.

### 5b.8 Disclaimers on advisory outputs

Any view showing forward-looking, estimated, or in-period commission data must include a clear disclaimer: estimates are advisory only; final commission is determined by official period close. This includes the rep dashboard, commission estimator, manager forecast views. The disclaimer protects Wasnie and the customer from legal disputes over "the system said I would earn X."

---

## 6. Build phases

Phases are ordered. Earlier phases enable later phases. Do not skip.

### Phase 0 — Foundation ✅ Complete or near-complete
Multi-tenant scaffold, auth, i18n, theme system, design system, compensation domain model.

### Phase 1 — Plans, Quotas, Assignments, Payees (in progress)
Complete the operational core: every entity has full UI CRUD wired to backend.

### Phase 2 — Transactions and ingestion
Manual entry, CSV import, REST API ingestion, credit allocation, status lifecycle.

### Phase 3 — Calculation engine 🚨
Deterministic execution, reproducibility, simulation mode, full traceability. **Comprehensive tests mandatory.**

### Phase 4 — Visibility
Rep portal, manager portal, reporting library, custom reports.

### Phase 5 — Workflows
Disputes, approvals, adjustments, period close.

### Phase 6 — Integrations
REST API surface, HubSpot connector, Salesforce connector, webhooks.

### Phase 7 — Administration and billing
Tenant settings, user management, audit log, Stripe billing, assisted onboarding flow.

### Phase 8 — Polish and differentiators
Plan templates per chosen vertical, multi-currency UX, AI plan builder (basic).

### Phase 9 — Compliance and certifications
GDPR full compliance, SOC 2 Type II, ISO 27001, penetration testing.

**Realistic timeline estimate** (single founder, part-time, with assisted Claude Code development): 18-30 months to a customer-ready product including basic compliance. **Not the 8 months previously suggested.** A serious financial product cannot be rushed.

---

## 7. Current state — May 2026

### What is built

**Backend:**
- ASP.NET Core .NET 8, Clean Architecture (Domain / Application / Infrastructure / Api)
- Multi-tenancy with discriminator + global query filters
- ASP.NET Identity + JWT + SQL-backed refresh tokens
- Compensation domain model: Plan, Rule, Trigger, Measurement, RateTable, Modifier, Cap, Floor, Quota, PlanAssignment, Transaction, Credit, Payout, PayoutLine
- Value objects: Money, Percentage, DateRange, PayeeReference, RuleSnapshot, ModifierApplication
- Application layer for plans, quotas, assignments
- REST endpoints for auth, plans, quotas, assignments
- Code-First migrations applied to SQL Server

**Frontend:**
- Angular 20 standalone components, Signals
- 15 design system primitives
- Three themes wired (light, soft, dark)
- Three languages wired (en, es, pl)
- Login, register, dashboard (mocked), plan list/create/detail, rule form, `/__design-system` preview
- Status-aware UI for plans (per section 5b.6)

### What is critically missing for any sellable version

- Calculation engine execution (Phase 3)
- Rep portal (Phase 4)
- Dispute workflow (Phase 5)
- Audit log (5.8.3)
- Effective dating proper implementation (5.2.7)
- HubSpot or Salesforce connector (Phase 6)
- Stripe billing (5.8.4)
- GDPR compliance basics (5.10.2)

**Conclusion:** Wasnie is approximately 25-30% built toward a sellable product. Honest assessment.

### Audit results — Phase 1 closure (May 2026)

**Security fix (BUG-X — CRITICAL):** EF Core constant-folding bug. `HasQueryFilter` was applied in Configuration classes with an injected `ITenantContext`. EF Core evaluated `tenantContext.TenantId` once at query compilation time and hardcoded the first tenant's GUID into the SQL plan — cross-tenant data was readable for all subsequent requests. Fixed by moving all `HasQueryFilter` calls into `ApplicationDbContext.OnModelCreating` using `this.CurrentTenantId` (a property set at DbContext construction). All 10 entity configurations affected and corrected.

**BUG-001 (FIXED):** `Money.Of()` accepted negative amounts with no guard. Added `Money.OfNonNegative()` factory that rejects negatives. `Money.Of()` intentionally retains signed behavior for adjustments and clawbacks. `CreateQuotaHandler` updated to use `OfNonNegative`.

**BUG-002 (FIXED):** `Money.ToString()` was using `F2` half-up rounding instead of banker's rounding (half-to-even) as required by spec 5b.5. Fixed with `Math.Round(Amount, 2, MidpointRounding.ToEven)`.

**BUG-003 (FIXED):** `Plan.Archive()` allowed Draft → Archived transition. Spec 5b.1 permits only Active → Archived. Guard updated; domain throws `DomainException` on non-Active source.

**BUG-004 (FIXED):** `Plan.CloneAsNewVersion()` had no status restriction — Draft plans could clone themselves, creating two Draft versions of the same plan name. Guard added; domain throws `DomainException` on Draft source. `ClonePlanVersionHandler` updated to catch `DomainException` and return `Result.Failure`.

**BUG-005 (FIXED):** `DELETE /api/plans/{planId}` endpoint was missing. Added `DeletePlanCommand`, `DeletePlanHandler`, and controller action. Domain enforces: only Draft plans with zero active rules are deletable.

**Test coverage (2026-05-25):**
- `Wasnie.UnitTests`: 101 tests — Domain (Money, DateRange, Percentage, Plan) + Validator tests
- `Wasnie.IntegrationTests`: 27 tests — Testcontainers SQL Server 2022, real EF migrations, JWT auth override
- Total: **128 tests, 0 skipped, 0 failed**

---

## 8. Out of scope (explicit)

- General-purpose CRM
- Payroll processing (Wasnie outputs amounts; payroll providers pay)
- General HRIS
- Generic BI (Wasnie exports; deep analytics in Tableau/PowerBI)
- Sales forecasting (potential future module, not launch)
- Sales coaching
- Lead management
- CPQ
- Contract lifecycle management

---

## 9. Success criteria for market-readiness

Wasnie is "sellable" — i.e., ready for the first 5 paying customers — when:

1. A tenant can sign up, be onboarded by Wasnie team in 48 hours, import payees and transactions, and produce first calculated payouts
2. Calculation engine produces deterministic, reproducible, audited payouts
3. Rep portal allows visibility and dispute submission
4. Dispute workflow is functional end-to-end
5. Period close produces locked, auditable results
6. HubSpot or Salesforce connector works for one-way transaction sync
7. Stripe billing operational
8. Audit log records every state-changing action
9. Public docs site with 30+ articles
10. GDPR-compliant (privacy, terms, export, deletion flows)
11. Production deployed on Azure with monitoring, automated backups, 99.5% uptime target
12. Liability insurance in place (professional indemnity, technology errors & omissions)

That is the bar for first revenue. Enterprise readiness adds SOC 2, ISO 27001, SSO, advanced approvals, territory optimization.

---

## 10. Realistic outlook

**Honest probability assessment (May 2026):**

| Outcome | Probability |
|---|---|
| Product reaches sellable state in 18-24 months | 60% |
| 5 paying customers in 24 months | 25-35% |
| 100K EUR ARR in 24 months | 10-15% |
| 300K EUR ARR in 24 months | 3-5% |
| Sustainable independent business at 36 months | 15-25% |
| Acquisition exit | <2% |

This is financial software. The risks are real. The opportunity is also real. Neither extreme — "this will fail" or "this will make you rich" — is honest.

**The reasonable case:** Wasnie becomes a stable side business with 10-25 customers over 3-4 years, generating 80-200K EUR ARR, providing meaningful supplementary income and significant career capital.

**The optimistic case:** vertical specialization and assisted onboarding produce strong retention, organic growth via referrals in the chosen vertical, and a 500K-1M ARR business by year 4-5 that can support hiring.

**The pessimistic case:** customer acquisition proves harder than expected, Rodolfo recognizes within 12 months that the business does not scale without a sales co-founder, and Wasnie remains a portfolio piece that opens doors to senior engineering roles or consulting opportunities.

All three cases have positive elements. None is failure.

---

## 11. Glossary

| Term | Meaning |
|---|---|
| **SPM** | Sales Performance Management — broad category covering compensation, quotas, territories, planning |
| **ICM** | Incentive Compensation Management — narrower than SPM, focused on commission calculation |
| **ACV** | Annual Contract Value — revenue from one customer in one year |
| **ARR** | Annual Recurring Revenue — sum of ACV across all active customers |
| **CAC** | Customer Acquisition Cost — total cost to acquire one customer |
| **LTV** | Lifetime Value — total revenue from a customer over their lifetime as customer |
| **Payee** | Person who earns commissions (typically sales rep) |
| **Plan** | Versioned compensation policy |
| **Rule** | Single calculation unit within a plan |
| **Trigger** | Predicate determining when a rule applies |
| **Measurement** | Metric on which commission is calculated |
| **Rate table** | Structure converting measurement to commission amount |
| **Modifier** | Adjustment to base commission |
| **Cap** | Maximum commission per transaction or period |
| **Floor** | Minimum guaranteed commission |
| **Quota** | Sales target assigned to a payee for a period |
| **Attainment** | Percentage of quota achieved |
| **SPIFF** | One-time bonus tied to specific behavior or product |
| **Clawback** | Recovery of previously paid commission |
| **Transaction** | Ingested deal eligible for commission |
| **Credit** | Allocation of a transaction to a specific payee |
| **Split** | Multiple payees sharing credit for a transaction |
| **Overlay** | Additional rep earning commission on top of primary rep |
| **Payout** | Calculated commission record for a payee for a period |
| **PayoutLine** | Single line item showing one (credit + rule) calculation chain |
| **Period close** | Finalization of a calculation period |
| **Dispute** | Rep's formal challenge to a payout line |
| **Adjustment** | Manual modification to a payout (additive, not destructive) |
| **Tenant** | Customer organization (multi-tenant isolation boundary) |
| **Effective dating** | Recording when facts were true in the world (vs when Wasnie learned about them) |
| **Banker's rounding** | Rounding mode that rounds halves to the nearest even number, fairer than standard rounding for financial calculations |
| **TDE** | Transparent Data Encryption (SQL Server feature) |
| **RPO / RTO** | Recovery Point Objective / Recovery Time Objective (disaster recovery targets) |

---

## 12. Document references

- Backend solution: `WasnieApi/Wasnie.sln`
- UI design system: `WasnieUi/DESIGN_SYSTEM.md`
- API base path (dev): `http://localhost:5000/api`
- Frontend (dev): `http://localhost:4200`
- Production target: Azure App Service, West Europe

---

*Living document. Update before scope changes. The integrity of this document protects the integrity of Wasnie.*
