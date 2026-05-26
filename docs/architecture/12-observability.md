# 12 — Observability

**Reading time:** ~5 min
**Applies to:** Backend, Frontend, Production operations

---

## Why this matters

When a customer says "the system is slow today" or "I'm getting an error", we MUST be able to:

1. Verify the problem (is it real? is it widespread?)
2. Find the cause (which service? which query? which deploy?)
3. Confirm the fix (did the patch resolve the issue?)

Without observability, every customer report is a guessing game. With it, we can pinpoint and resolve issues in minutes instead of hours.

For financial software, this matters double: a bug that miscalculates one commission may stay silent unless we proactively monitor for anomalies.

---

## 12.1 Logging

### Rule 12.1.1 — Structured JSON logging

All logs MUST be structured (JSON), never free-text. Use Serilog (backend) and equivalent (frontend → Sentry).

```csharp
_logger.LogInformation(
    "Payee {PayeeId} terminated by {UserId} on {Date}",
    payee.Id,
    currentUser.Id,
    today);
```

NOT:

```csharp
_logger.LogInformation($"Payee {payee.Id} terminated by {currentUser.Id} on {today}");
// ← string interpolation = no structured fields for filtering
```

### Rule 12.1.2 — Log levels used correctly

| Level | Use for |
|---|---|
| **Trace** | Detailed debug info, off in production |
| **Debug** | Diagnostic info, off in production |
| **Information** | Normal flow milestones (request received, business event completed) |
| **Warning** | Recoverable issue (retry succeeded, fallback used, rate limit hit) |
| **Error** | Unrecoverable issue affecting one request/operation |
| **Critical** | System-level issue (DB unavailable, out of memory, security breach) |

### Rule 12.1.3 — Every request has a correlation ID

Middleware adds `X-Correlation-ID` to every request (generated if not provided). All logs for that request include this ID. Logs across services (when distributed) share the same correlation ID.

### Rule 12.1.4 — Logs MUST redact secrets

(File 04, Rule 4.9.4)

Use Serilog destructuring to omit sensitive fields:

```csharp
_logger.LogInformation("Login attempt for {Email}", request.Email);
// NEVER log the password, even hashed
```

### Rule 12.1.5 — Money operations logged with full context

Every commission calculation produces a log:

```
{
  "timestamp": "2026-06-15T14:23:45.789Z",
  "level": "Information",
  "correlationId": "abc-123-def",
  "tenantId": "...",
  "userId": "...",
  "event": "CommissionCalculated",
  "transactionId": "...",
  "payeeId": "...",
  "ruleType": "Tiered",
  "inputAmount": 5000.00,
  "calculatedAmount": 250.00,
  "currency": "EUR",
  "rulesApplied": ["base-rate", "q3-accelerator"],
  "durationMs": 12
}
```

This makes disputes resolvable and audits possible.

### Rule 12.1.6 — Log retention

- Production logs: 90 days hot, archived after
- Audit log: 7 years (separate from operational logs — file 05)

---

## 12.2 Metrics

### Rule 12.2.1 — Per-endpoint metrics

For every endpoint:
- Request count
- Response time histogram (P50, P95, P99)
- Error rate (HTTP 5xx)
- Status code distribution

Aggregated by tenant for billing-related metrics, anonymized for operational metrics.

### Rule 12.2.2 — Business metrics

Beyond technical metrics, track:
- Payees created per tenant per day
- Imports executed per tenant per day
- Commission calculations performed per day
- Authentication failures per hour
- Active tenants in the last 24h, 7d, 30d

### Rule 12.2.3 — Resource metrics

- CPU, memory, disk per server
- Database CPU, IOPS, connection pool usage
- Cache hit rate (when caching introduced)
- Queue depth (when async introduced)

### Rule 12.2.4 — Metric export

Metrics export via OpenTelemetry to Application Insights (Phase C6).

---

## 12.3 Tracing

### Rule 12.3.1 — Distributed tracing enabled

Every request gets a trace with spans for:
- HTTP request entry
- Authentication
- Authorization
- Database queries (each one)
- External API calls
- Background job queueing

OpenTelemetry instrumentation (Phase C6).

### Rule 12.3.2 — Spans MUST include relevant attributes

Database query spans include the query (parameterized, no secrets). HTTP spans include URL, status code. Don't leak PII into trace attributes.

### Rule 12.3.3 — Trace sampling

100% sampling in development. 10% sampling in production (adjustable). Error traces always sampled (100%).

---

## 12.4 Alerting

### Rule 12.4.1 — Alerts on actionable events only

Alert when:
- Error rate > 2× baseline for 10 minutes
- P95 response time > 2× baseline for 10 minutes
- Failed login spike (potential attack)
- DB connection pool exhausted
- Background job queue backed up > 30 min
- Security event (forbidden access pattern)

DO NOT alert on:
- Single 500 (might be a one-off)
- Slow but-still-functional response
- "Interesting" but non-actionable events

### Rule 12.4.2 — Alert routing

Critical alerts: page (call/SMS)
Warning alerts: email/Slack
Info-level events: dashboard only

### Rule 12.4.3 — Alert fatigue is a bug

If an alert fires often and we ignore it, the alert is broken. Either:
- Fix the underlying issue
- Adjust the threshold
- Remove the alert

Alert tuning is ongoing maintenance.

---

## 12.5 Frontend observability

### Rule 12.5.1 — Errors captured

Frontend errors caught by global error handler, sent to Sentry (or equivalent).

Include:
- User ID (anonymized)
- Tenant ID
- Browser, OS, device
- Page URL
- Stack trace
- Recent user actions (breadcrumbs)

### Rule 12.5.2 — Performance monitoring

Real User Monitoring (RUM) captures actual user experience:
- Page load time
- Interaction to next paint
- Cumulative layout shift

Compared against Rule 3.3.1 baselines (file 03).

### Rule 12.5.3 — No PII in error reports

Sanitize before sending. Specifically:
- No email addresses (use user ID)
- No financial values
- No customer names

---

## 12.6 Dashboards

### Rule 12.6.1 — Operational dashboard

Shows:
- Request rate, error rate, P95 per endpoint
- Active users, active tenants
- Background job queue health
- Database health

Reviewed daily during active development.

### Rule 12.6.2 — Business dashboard

Shows:
- Tenants by tier
- Tenant churn rate
- Feature usage (which features get used the most)
- Conversion funnel (free → paid)

Reviewed weekly.

### Rule 12.6.3 — Incident dashboard

When an incident is active, a focused dashboard shows everything relevant in one screen.

---

## 12.7 Cost-conscious observability

### Rule 12.7.1 — Use cost-effective tools

Wasnie targets small-to-mid SaaS, not enterprise. Choose observability stack accordingly:

- Sentry free tier (5k errors/month)
- Application Insights with caps
- Self-hosted Grafana for dashboards
- Open-source where viable

Avoid Datadog enterprise pricing until customer revenue justifies.

### Rule 12.7.2 — Sample aggressively

Not every log needs to be retained. Not every trace needs full detail. Sample sensibly.

### Rule 12.7.3 — Aggregate before storing

Don't store every individual metric data point forever. Aggregate to 1-min, 5-min, 1-hour resolutions for older data.

---

## Enforcement

- **Logging configuration** in code, reviewable
- **Code review** verifies new code uses structured logging
- **Phase C6 task** implements full observability stack
- **Alert review** monthly

---

## Bug history

- **Phase 1:** observability is currently minimal. Phase C6 will introduce the full stack.
- **Lesson from Phase A:** when bugs occurred (filter not applied, etc.), debugging relied on guesswork because there were no structured logs. With proper observability, the same bugs would have been found in minutes.
