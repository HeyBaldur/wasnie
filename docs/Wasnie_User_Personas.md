# Wasnie — User Personas and Jobs To Be Done

**Status:** ACTIVE
**Version:** 1.0
**Created:** 2026-05-26
**Owner:** Rodolfo Calvo
**Purpose:** Define the human users Wasnie serves, what they are trying to accomplish, and what success looks like for each. This document feeds the Business Brief, product decisions, and UX priorities.

---

## How to use this document

Personas are not "average users." They are decision-making frames. When designing a feature, ask:

- Which persona is this for?
- What job is this persona trying to get done?
- What is the alternative they use today?
- What would make them switch to Wasnie?

If a feature does not clearly serve one of the four personas below, it does not belong in Wasnie.

---

## The four personas

Wasnie serves exactly four personas. Every screen, every endpoint, every notification exists for one of them.

| Persona | Role | Primary value |
|---|---|---|
| **Ariana** | RevOps Lead / Comp Manager | Designs plans, runs the system, ensures payouts are correct |
| **Sergio** | Sales Rep | Sees what he earns, raises disputes, trusts the number |
| **Maja** | Sales Manager | Tracks team performance, approves disputes, reviews team |
| **Marek** | Finance Director | Approves payouts, audits calculations, signs off |

Other roles (e.g., HR, IT) interact with Wasnie occasionally but are not primary personas. They are supported through the four primary personas.

---

## Persona 1 — Ariana — RevOps Lead / Comp Manager

### Identity

- **Role:** Head of Revenue Operations (or Sales Operations Manager)
- **Title variants:** RevOps Lead, Sales Operations Director, Compensation Manager
- **Company size:** 50-300 employees
- **Reports to:** VP Sales or CFO
- **Team:** 1-3 people (sometimes she IS the team)
- **Tenure in role:** 2-5 years
- **Background:** Started in finance or sales, moved into operations
- **Tools she uses today:** Excel (heavily), Salesforce (or HubSpot), Slack, sometimes a legacy SPM tool
- **Reports to her:** Often no one. She is a function of one.

### Context

Ariana is the most important person in the room when commissions get paid. She designs the compensation plans, models them in Excel, ingests CRM data, calculates payouts, fights disputes, and explains numbers to finance and to reps. When a rep is unhappy with their commission, the call goes to her. When the CFO asks why payouts are 8% over forecast, the explanation comes from her.

She is overworked, undertooled, and chronically under-recognized.

### What she does today (the painful reality)

- Maintains 5-15 Excel files for current quarter's compensation plans
- Pulls Salesforce data manually each month (or pays a developer to write a script that breaks every quarter)
- Manually maps transactions to reps using VLOOKUPs and pivot tables
- Recalculates payouts every time a deal is amended, returned, or disputed
- Spends 1-2 days per month reconciling Excel numbers with Salesforce numbers
- Sends individual emails or Slack messages to each rep with their payout summary
- Fields 10-30 disputes per quarter, each resolved over email
- Prepares finance handover spreadsheets, then answers finance's questions
- Receives plan changes from VP Sales mid-quarter and has to figure out how to apply them retroactively

### What she dreams of

- A system she can configure herself, not "implement" with consultants
- Plans she can update without breaking historical calculations
- Transparency for reps so they stop asking her individually
- An audit trail so she can defend any number to anyone
- A way to model "what if we change the accelerator" before changing it
- Coming home before 7 PM during commission run weeks

### Jobs To Be Done

1. **Build a new comp plan** so a new product line or new fiscal year can be onboarded
2. **Update an existing plan mid-period** without breaking past payouts
3. **Import this quarter's transactions** from CRM/CSV without manual cleanup
4. **Calculate payouts** for the period quickly and verifiably
5. **Answer "why is my commission this number"** instantly when a rep asks
6. **Handle disputes** with full context (the deal, the plan, the rule, the math)
7. **Hand off to finance** with documentation, not a spreadsheet free-for-all
8. **Model the impact of a plan change** before committing to it
9. **Show the VP Sales what the team is on track for** at any moment
10. **Sleep at night** during quarter-close week

### Frustrations with existing solutions

- Xactly: "powerful but unbearable" — she needs a PhD to make a simple change
- CaptivateIQ: "the rebrand of Spiff that still bugs out" — better UX but still requires expensive implementation
- Excel: "actually the best one until we hit 50 reps, then it's hell"
- Most tools: "designed for the implementer, not the operator"

### What success looks like for Ariana with Wasnie

- Day 1: she imports her current Excel plan in 30 minutes, not 3 months
- Day 30: she runs a clean payout cycle without escalating a single issue to a vendor
- Day 90: she models 3 plan variants before the next fiscal year, presents them to the CFO with confidence
- Day 365: she's promoted because compensation operations is no longer a fire drill

### Anti-Ariana

- People who **need** consultants to feel secure
- People who **want** a 6-month implementation because it justifies their budget
- People who have **already adopted** Xactly enterprise and have political reasons to stay

---

## Persona 2 — Sergio — Sales Rep

### Identity

- **Role:** Account Executive, Sales Development Rep, Solution Sales, etc.
- **Company size:** mid-market (same as Ariana's)
- **Quota:** €500k-€2M ARR annually
- **Compensation mix:** 50/50 base/variable or 60/40
- **Tools he uses today:** Salesforce / HubSpot, email, Slack, a personal spreadsheet to track his own deals
- **Tenure:** 1-5 years in this role
- **Mindset:** Trust but verify. Wants the deal closed AND wants to see his name on the payout.

### Context

Sergio cares about three numbers:

1. How much he has made so far this quarter
2. How much he would make if he closes the deal he's working on right now
3. When the next payout hits his bank account

Anything else is noise. The company can have the most sophisticated plan in the world — if Sergio can't quickly answer those three questions, the plan is broken from his perspective.

### What he does today

- Maintains a personal spreadsheet because he doesn't trust the company's calculation
- Calls Ariana ("Hey, quick question, why is my commission down this month?") at least once per quarter
- Asks his manager ("Maja, is this number right?") even more often
- Raises disputes over email when something seems off
- Doesn't fully understand his own compensation plan (because Ariana hasn't had time to walk through it with him)

### What he dreams of

- Open the app, see his number, trust it
- A "what if" simulator so he can see "if I close this Acme deal at €80k, what's my commission?"
- An understandable rule explanation: "you got 8% on this deal because it was a new logo and it crossed your Q2 accelerator threshold"
- A way to raise a question without an email chain
- Real-time updates, not month-end surprises

### Jobs To Be Done

1. **Check current month's earnings** in under 10 seconds
2. **See where he stands against quota** without exporting anything
3. **Simulate "what if I close X"** without asking anyone
4. **Understand why a calculation is what it is** — see the rule applied, the math
5. **Raise a dispute** with full context and a real workflow (not email)
6. **See historical earnings** across quarters to plan personal finances
7. **Receive a notification** when his payout is processed
8. **Trust the number** — without needing to recompute in his own spreadsheet

### Frustrations with existing solutions

- Most SPM tools have a "rep portal" that is an afterthought — Sergio uses it once and goes back to his spreadsheet
- Calculations feel like a black box
- He doesn't know if his current quarter is "good" until 5 days after the month closes
- Disputes feel adversarial: "you're saying I'm wrong" rather than "let's check the math together"

### What success looks like for Sergio with Wasnie

- Day 1: he logs in, sees his current month earnings, and goes "wait, this is right? cool"
- Day 30: he stops keeping his personal spreadsheet
- Day 90: he simulates 3 deals before closing them, optimizes which to push
- Day 365: he hits the year more accurately than ever because he can see in real time what's happening

### Why Sergio matters more than Ariana might think

Reps talk to each other. If 10 reps tell Ariana "this Wasnie thing is actually good" she's a hero. If 10 reps tell her "I still can't tell what I made this month" she has a new fire.

**Rule:** Wasnie is bought by Ariana but renewed by Sergio.

---

## Persona 3 — Maja — Sales Manager

### Identity

- **Role:** Regional Sales Manager, Territory Manager, Team Lead
- **Reports to:** VP Sales or Director of Sales
- **Reports to her:** 5-15 reps
- **Tenure:** 3-8 years
- **Background:** Was a rep, got promoted
- **Tools she uses today:** Salesforce (for team pipeline), Excel (for her own tracking), Slack
- **Pressure:** Hit team quota every quarter, retain her best reps, develop her weaker ones

### Context

Maja is the bridge between Sergio and Ariana. When Sergio raises a dispute, it lands on Maja first. When Ariana needs context on a transaction credit, she asks Maja. When VP Sales asks "how is the team trending," Maja answers without taking a week.

Maja cares about her team's earnings because:

1. Team earnings = team morale = team retention
2. Team performance against quota = her own bonus
3. Knowing where reps stand lets her coach (and intervene) before quarter-close

### What she does today

- Receives the team's payout summary from Ariana monthly
- Reviews disputes raised by her team members via email
- Tracks attainment percentages in her own spreadsheet (because she doesn't have a real dashboard)
- Has 1-on-1s where commissions are sometimes the topic
- Escalates to Ariana when something doesn't look right

### What she dreams of

- One screen showing every rep's attainment, earnings, and forecast
- A dispute workflow where she can review, comment, and decide without email chains
- Visibility into which deals are credited to which reps (so she can resolve split credit issues fast)
- Trend data: who's heating up, who's cooling down, who's at risk

### Jobs To Be Done

1. **See team attainment at a glance** — one screen, current period
2. **Drill into a specific rep** to coach effectively
3. **Review and decide on disputes** raised by team
4. **Approve adjustments** when finance/comp manager flags them
5. **Forecast team performance** vs quota
6. **Identify reps at risk** (low attainment, frustrated tone) before quarter-close
7. **Compare quarters** to spot patterns

### Frustrations with existing solutions

- Most SPM tools either don't have a manager view at all, or have a watered-down version of the comp manager view
- Dashboards are dense and unreadable
- Drilling from team → rep → deal is multiple clicks and confusing

### What success looks like for Maja with Wasnie

- Day 1: she opens "My Team" and instantly sees the 12 reps with their current attainment
- Day 30: she has resolved 3 disputes in Wasnie without a single email
- Day 90: she identifies a rep cooling down 6 weeks before quarter-end, intervenes, saves the quarter
- Day 365: she gets credit for her team's improved performance because she had visibility she never had

---

## Persona 4 — Marek — Finance Director

### Identity

- **Role:** Finance Director, Controller, VP Finance
- **Reports to:** CFO
- **Tools he uses today:** ERP (SAP, NetSuite, Oracle), Excel, BI tool (Power BI / Tableau)
- **Tenure:** 5-15 years
- **Pressure:** Accuracy, auditability, on-time close

### Context

Marek doesn't care about the compensation plan as a sales tool. He cares about:

1. Are the numbers right?
2. Can he prove they're right (audit trail)?
3. Are they ready on time for payroll/accruals?
4. Are the booked accruals matching the actual payouts?

If Marek doesn't trust Wasnie, the company doesn't renew. Period.

### What he does today

- Receives a spreadsheet from Ariana with the period's payouts
- Spot-checks 5-10 lines against the original transactions and the plan
- Books journal entries based on the totals
- Reconciles the booking with the actual payout sent to payroll
- Files everything for audit
- Stresses about audit findings

### What he dreams of

- A read-only finance view with full audit trail
- Drill-down from a payout total to individual transactions, to the rule that applied, to the rep's plan version on that date
- A clean export to ERP without manual reformatting
- Confidence to defend any number to any auditor

### Jobs To Be Done

1. **See period totals** (per tenant, per cost center, per plan) for accrual booking
2. **Drill into any number** to verify (transaction → calculation → rule → plan version)
3. **Export to ERP / GL** in his standard format
4. **Audit a specific payout** from a previous period if questioned
5. **Reconcile** between accrued amounts and actually paid amounts
6. **Approve the period close** with confidence
7. **Pass audit** when external auditors ask "how do you calculate commissions"

### Frustrations with existing solutions

- "The drill-down works for 3 levels then it's broken"
- "I can't see the version of the plan that was active when this commission was calculated"
- "The export looks like a comp manager designed it, not a finance person"
- "There's no proper audit trail" — usually a deal-killer for him

### What success looks like for Marek with Wasnie

- Day 1: he sees the period totals, drills into one, drills again, drills again, gets to the source — and it all reconciles
- Day 30: he books accruals from Wasnie data without manual transformation
- Day 90: an external auditor asks "show me how this rep's October commission was calculated" and Marek shows the full audit trail in 30 seconds
- Day 365: he's the one recommending Wasnie at the next CFO mastermind, because his life is better

---

## The supporting cast (not primary, but always present)

These roles are not primary personas, but they interact with Wasnie and their needs are accommodated:

### VP Sales — uses dashboards, doesn't operate the system

Wants: high-level team performance, pacing, forecast vs quota. Mostly consumes outputs from Maja and Ariana.

### CFO — sets the standard, doesn't operate the system

Wants: confidence that Marek and Ariana have what they need. Will not log in monthly.

### IT / Admin — sets up SSO, integrations, permissions

Wants: a system that doesn't create work. SAML SSO that just works. Documented permissions model. Logs they can ingest into their SIEM.

### HR — owns the org chart

Wants: confidence that when a person is hired/promoted/terminated in HR system, Wasnie reflects it accordingly.

---

## Job-To-Be-Done Priority Matrix

These are the jobs that, if Wasnie does NOT do them well, the customer churns:

| Persona | Critical JTBD | Wasnie capability needed | Priority |
|---|---|---|---|
| Ariana | Build a plan and run a period | Plan designer + calculation engine | P0 |
| Ariana | Update a plan without breaking history | Versioning + effective dating | P0 |
| Sergio | Trust the number | Audit trail, real-time, transparency | P0 |
| Sergio | Simulate "what if I close X" | Simulation mode | P1 |
| Maja | See team attainment | Manager portal | P1 |
| Maja | Review disputes | Dispute workflow | P1 |
| Marek | Drill into any payout | Full audit trail | P0 |
| Marek | Export to ERP | Reporting + integrations | P1 |

P0 = must exist for any sellable version
P1 = must exist before market expansion past first 10 customers

---

## Anti-personas (who Wasnie does NOT serve)

These are not pejorative. These are people for whom Wasnie is genuinely the wrong choice, and selling to them produces unhappy customers.

### Anti-Ariana

- Enterprise Comp Managers at 5,000+ employee firms with custom logic that takes 12 months to implement (they need Xactly)
- Comp Managers who measure their value by the complexity of their plans (Wasnie deliberately simplifies)
- People who want a consultant to hold their hand for 6 months (Wasnie is self-serve)

### Anti-Sergio

- Reps at very small startups (< 10 reps) where Excel is genuinely sufficient
- Highly variable compensation in commission-only / outside sales contexts where commission is the entire job

### Anti-Maja

- Managers of teams > 50 reps with multiple sub-managers (different tooling needed)

### Anti-Marek

- Public companies subject to SOX with full ITGC controls (Wasnie may not be ready until Phase 9; SOC 2 Type II is needed first)

---

## Implications for product decisions

When the team is debating whether to build a feature, the test is:

1. **Which persona does this serve?** If we can't name one, don't build it.
2. **Which Job To Be Done does this complete?** If it's not on the list above, don't build it (or add the job to the list with justification).
3. **What's the alternative the persona uses today?** If our version isn't 10× better, they won't switch.
4. **Will this make Ariana's life easier OR will Sergio trust the number more?** If neither, deprioritize.

---

## Document changelog

| Version | Date | Change |
|---|---|---|
| 1.0 | 2026-05-26 | Initial creation. Four primary personas + anti-personas defined. |
