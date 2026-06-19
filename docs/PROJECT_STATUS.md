# Wasnie — Project Status

**Last updated:** 2026-06-19 — WI-UNITS-MEASUREMENT COMPLETE. Implementa medición por unidades (Type=Units) en el motor de comisiones. (1) **Motor backend:** `CreditAllocationService.BuildCreditsAsync` ramifica según `rule.Measurement.Type`: Revenue usa `transaction.Amount` (sin cambios); Units calcula `CommissionCalculator.ComputeUnitsCommission(quantity, flatRate, currency)` = `ratePerUnit × quantity`. Guarda de runtime para Units+no-Flat: `LogError` + comisión cero. (2) **Validación de dominio (FASE 3):** `Rule.Create` y `Rule.Update` ahora llaman `ValidateMeasurementRateTableCompatibility` — lanza `DomainException` si Type=Units con Tiered o Attainment (esas combinaciones no tienen semántica definida). (3) **RuleSnapshot:** campo `Measurement` añadido (backward-compat: créditos históricos sin campo reciben default Revenue/amount/Sum al deserializar). `RuleSnapshotJsonConverter` lee y escribe el campo. (4) **UI (FASE 2):** Measurement section — Source Field y Aggregation eliminados de la UI (se transmiten al API con defaults: `sourceField="amount"`, `aggregation=Sum`); solo queda el selector de Type. Aggregation options reducidas a `[Sum]` (los demás no están implementados). En modo Units: botones Tiered y Attainment del Rate Table se deshabilitan con nota explicativa; etiqueta/hint/tooltip del campo Flat Rate cambian a "Rate per Unit" / "importe €/unidad"; Live Preview muestra `{rate} / unit` en vez de `{rate*100}%`. Suscripción reactiva fuerza `rateTable.type = Flat` al cambiar a Units. (5) **i18n:** EN/ES/PL — `FIELD_FLAT_RATE_PER_UNIT`, `HINT_RATE_UNITS`, `UNITS_RATE_TABLE_NOTE`, `TOOLTIP_FLAT_RATE_UNITS`. (6) **Tests:** +5 unit (CommissionCalculatorTests), +4 domain (PlanTests Units validation), +5 integration (CreditAllocationServiceTests: Units×Q1, Units×Q10, Units+Cap, Units+Floor, Revenue regression). Backend 625 unit / 0 failures. Frontend 391/410 (19 pre-existing unchanged). `ng build --configuration production` clean.

**Last updated:** 2026-06-19 — WI-PLAN-DROPDOWN-STATUS COMPLETE. Auditoría + filtrado de dropdowns de planes: (1) Backend `ListPlansHandler` ahora soporta `?statuses=Active,Archived` (comma-separated plural) además del `?status=Active` singular existente (backward-compat completo). 3 tests de integración nuevos (multi-estado, aislamiento de tenant con multi-estado, singular sigue funcionando). (2) Frontend: `SelectOption` extendido con campo `badge?: { text: string; variant: BadgeVariant }` opcional; `WsSelect` (template + TS) actualizado para renderizar `ws-badge` en opciones y en el trigger — backward-compatible (callers sin badge no ven ningún cambio visual). SCSS: `.ws-select__value` cambiado a flex para co-mostrar badge + texto; `.ws-select__option-label` nuevo para ellipsis correcto. (3) Seis `planSearchFn` actualizados: Credits (Active+Archived server-side), Payouts (Active+Archived), Pay Run Detail (Active+Archived), Pay Runs Calculate (Active only — antes sin filtro), Assignments Create (Active only — migrado de client-side .filter() a server-side), Quotas Create (Active+Archived — migrado de client-side a server-side). Todos emiten badge con clave i18n `PLANS.STATUS_<STATUS>` (ya existían en EN/ES/PL — sin cambios de i18n necesarios). Mock de PayRunsListComponent spec corregido (faltaba `totalCount` signal). Test count: backend Application compila limpio; frontend 391/410 pass (19 pre-existing: ProcessPendingComponent ×5, SubscriptionReactivation ×14 — sin cambios). `ng build --configuration production` limpio (0 errores).

**Last updated:** 2026-06-18 — WI-OVERLAP-WARNING-UX COMPLETE. Pay run overlap warning rewritten to inform instead of alarm. Root cause: old text said "could be paid more than once" — inaccurate because the motor already excludes consumed transactions (ConsumedAt != null filter in CalculatePayoutsForPeriodHandler) and the backend has a hard block guard at Mark Paid time. No data gaps: `store.run()!.totalAmounts` already reflects only new transactions post-exclusion; no backend query needed. Fix: i18n-only change in EN/ES/PL — `PAY_RUNS.DETAIL.OVERLAP_WARNING` rewritten to: (1) state the period overlap factually, (2) explain that already-paid transactions were automatically excluded at calculation time, (3) confirm no transaction will be paid twice. Anti-double-pay guard in `MarkPayRunPaidHandler` untouched. No TS, no HTML, no component changes. `ng build --configuration production` clean.

**Last updated:** 2026-06-18 — WI-FIX-DATE-FILTER COMPLETE. Pay Runs and Payouts date filter now operates on creation date, not period. `ListPayRunsHandler.BuildQuery`: `PeriodFrom`/`PeriodTo` now compare against `r.CreatedAt` (DateTimeOffset) via UTC midnight conversion (`DateOnly → DateTimeOffset(ToDateTime(MinValue), TimeSpan.Zero)`, upper bound uses `AddDays(1)` exclusive). `ListPayoutsHandler.BuildQuery`: same pattern against `p.CalculatedAt`. Both handlers already sort by the correct creation field (`CreatedAt` / `CalculatedAt`). Frontend: `pay-runs.store.ts` sortBy changed from `'periodStart'` to `'createdAt'`; `payouts.store.ts` sortBy signal default changed from `'updatedAt'` to `'calculatedAt'` (was already functionally correct via `_` catch-all, now explicit). Comments in `PayRunFilterQuery` and `PayoutFilterQuery` updated to reflect new semantics. Integration test `ExportPayRuns_PeriodFilter_MatchesRunPeriod` renamed `ExportPayRuns_CreatedAtFilter_ReturnsSuccessRegardlessOfMatch` with updated date range (only checked `IsSuccess`, not row count — unaffected). Backend Application + IntegrationTests: build succeeded. Frontend store tests: 22/22 pay-runs + 20/20 payouts. `ng build --configuration production` clean (pre-existing budget warning only). FASE 5 (owner action): verify "This month" preset in Pay Runs list shows the run created today for Apr–Jun.

**Last updated:** 2026-06-18 — WI-FIX-DRAG-RECALCULATE-PAYRUN: FASEs 1–4 COMPLETE (FASE 5 = owner action). Recalculate Credits now detects and handles pay runs for the affected period: if any payee has an Approved or Paid pay run overlapping the period the entire operation is blocked (HTTP 409, structured `blockingPayRuns` list with period + status). If only Draft pay runs exist they are auto-deleted via `DeletePayRunDraftHandler` before credits are superseded, so stale totals never survive a recalculation. Backend: `RecalculateCreditsHandler` extended with ISender + ILogger, pays-run detection query (two-step: payouts→payRunIds→PayRuns with period overlap), block path returns `Result.Success(blockedResult)` with `BlockedByPayRuns` populated (matches `PayRunsController` 409 pattern), delete path dispatches `DeletePayRunDraftCommand` per draft, `DeletedDraftCount` added to `RecalculateCreditsResult`. `CreditsController.Recalculate` returns 409 when `BlockedByPayRuns.Count > 0`. Frontend: `BlockingPayRunInfo` model + `deletedDraftCount` on `RecalculateCreditsResult`; `recalculateBlockedRuns` signal populated on 409; blocked state renders `app-overlap-warning` reusing existing component (no new styles); success banner shows "Draft pay run deleted" message when `deletedDraftCount > 0`; `onRecalculate()` navigates to `/pay-runs` after draft auto-deletion (pay run no longer exists). i18n EN/ES/PL: `RECALCULATE_SUCCESS_WITH_DRAFT` + `RECALCULATE_BLOCKED_WARNING`. Tests: 4 new integration tests (draft auto-deleted, Approved blocks, Paid blocks, mix-of-Approved+Draft blocks all); 5 frontend specs (success-no-draft, success-with-draft-navigates, 409-blocked, generic error, idempotency guard). Backend 616/616 unit. Frontend 387/406 pass (19 pre-existing failures unchanged). `ng build --configuration production` clean.

**Last updated:** 2026-06-18 — WI-FIX-DRAG-CALCULATION: FASEs 1–4 COMPLETE (FASE 5 = owner action). Root causes fixed: (F-4) `ProcessPendingTransactionsJobHandler` processed transactions in SQL insertion order → wrong `PriorCumulative` for split-at-quota splits; fixed with `OrderBy(TransactionDate).ThenBy(Id)` on all 3 load methods. (F-2) `CalculatePayoutsForPeriodHandler` aggregates existing credits — never regenerates them — so changing `splitAtQuota` has no effect on a Draft pay run with stale credits. Fixed with new "Recalculate Credits" operation: supersedes stale credits, reverts Calculated transactions to Pending, enqueues `ProcessPendingTransactionsJob` per affected payee. Backend: `Permission.CreditsRecalculate`, `AuditActions.CreditsRecalculated`, `ResourceTypes.Credit`, `RecalculateCreditsCommand/Handler`, `POST /api/credits/recalculate`. `CompensationTransaction.RevertCalculatedToPending` domain method (blocks Paid/Cancelled, anti-double-pay guard). Frontend: "Recalculate credits" button on Draft pay runs (`Credits.Recalculate` permission), WsModal with warning explaining the 2-step flow (recalculate → wait for jobs → Calculate Pay Run), i18n EN/ES/PL, success banner showing superseded count + jobs queued. Tests: 4 domain unit (RevertCalculatedToPending state machine), 3 integration (RecalculateCreditsHandler happy path + consumed-credit guard + Paid-tx guard), 2 CreditAllocationService ordering (correct/wrong date order), 3 component (success / failure / idempotency guard). Backend 46 new tests. Frontend 46/46 pay-runs specs pass. `ng build --configuration production` clean. FASE 5 owner action required: EU Accelerator rule → toggle splitAtQuota ON + save → click "Recalculate credits" on Apr 1–Jun 30 Draft run → wait for jobs → Calculate Pay Run. Expected: Adrian €11,951.62.

**Last updated:** 2026-06-18 — WI-FIX (splitAtQuota end-to-end) COMPLETE. Root cause: frontend `RateTable` TypeScript interface never emitted `splitAtQuota` field → EF Core always deserialized `SplitAtQuota=false` → dispatch always chose bracket path → Adrian's Q2 EU Accelerator pay run showed €11,115.21 (all at 4%) instead of €11,951.62. Fix: (1) Added `splitAtQuota: boolean` to `RateTable` interface, FormGroup, `_buildRateTable()` serialization, `_loadExistingRule()` hydration; (2) Toggle UI in rule form (AttainmentBased only) with tooltip, i18n EN/ES/PL; (3) `StubQuotaAttainmentService` now accepts optional `splitContext` constructor param — `GetSplitContextAsync` returns it instead of hardcoded `null`; (4) 4 new integration tests covering EF Core round-trip, dispatch, real quota (caso Adrian → €11,951.6175), no-quota guard. Backend: 612 unit pass. Frontend: 15/15 spec pass; `ng build --configuration production` clean. Owner action still required (FASE 3): edit EU Accelerator rule in UI, activate toggle, save, recalculate pay run.

**Last updated:** 2026-06-17 — WI-FIX-MOTOR-ATTAINMENT COMPLETE (Phases 1–5). Root cause: EU Accelerator Q2 2026 reps never earned Tier 2 commission due to three compounding bugs — wrong quota data (Adrian had Apr €190k + Jun €10k instead of single Q2 €250k; other 5 reps had no Q2 quotas), bracket-lookup algorithm (entire tx amount taxed at one tier rate, never split at quota boundary), and attainment stored as ratio instead of absolute cumulative. Fixes: (1) New `SplitAtQuota: bool` on `RateTable` — when true, commission is computed by splitting the transaction at the quota boundary using `ComputeAttainmentSplitCommission`; default `false` preserves backward-compatible bracket-lookup for all existing plans. (2) New `AttainmentSplitContext` record and `GetSplitContextAsync` on `IQuotaAttainmentService`; implementation queries DB for active quota, no caching (PriorCumulative changes after each tx). (3) `CreditAllocationService.BuildCreditsAsync` dispatches per-rule to split or bracket path; Phase 5 guard: null split context → zero commission + LogWarning. (4) EU Accelerator rule `9edea449` updated to `"splitAtQuota":true` in DB; Adrian's wrong quotas deleted; 6 correct Q2 2026 quotas inserted (Apr 1–Jun 30, Active, Revenue). (5) "Calculate Payouts" button removed from Payouts page UI (TS + HTML); calculation remains in Pay Runs only. Unit tests: +10 attainment-split tests including mandatory caso Adrian (quota €250k, revenue €277,880.25 → commission €11,951.6175). Integration test stub updated. Store spec mismatches fixed (pageSize 25→10 in PayRunsStore, PayRunDetailStore, PayoutsStore specs). Backend: 612 unit tests (all pass). Frontend: 399 total, 380 pass, 19 pre-existing failures (ProcessPendingComponent ×5, SubscriptionReactivation ×14 — unchanged). `ng build --configuration production` clean.

**Last updated:** 2026-06-17 — WI-PAYEE-CHART-REFACTOR COMPLETE. (1) Replaced hand-drawn SVG `ws-bar-chart` with Chart.js vertical bar chart matching dashboard standards (`ws-hbar-chart` pattern: `afterNextRender` + `effect`, compact Y-axis labels, blue/gray per-bar color array for current/past months, Chart.js native tooltip). (2) Fixed `metric-card__value--full` — was inline `<span>` so `text-overflow: ellipsis` never activated; added `display: block`. Large numbers (€5.93T) no longer wrap across two lines on narrow viewports. (3) Removed `sparklineLabels` (last-7-day date strings) from all three sparklines — synthetic values don't have specific-day precision; removing date labels stops tooltips from claiming "Jun 13 had €X" when the value is a proportional allocation. Spec updated: removed 4 SVG-specific tests, added compact-notation test. Frontend: 402 total, 380 pass, 22 pre-existing failures (unchanged).

**Last updated:** 2026-06-17 — WI-DASHBOARD-PENDING-LABEL COMPLETE. Two fixes: (1) "ProcessPending" literal string removed from `PENDING_BY_PLAN_DESC` in EN/ES/PL (was an accidental key name leak — `"eligible for ProcessPending"` should have been `"ready to calculate"`). (2) Added globe-icon scope note `PENDING_BY_PLAN_SCOPE` ("All periods · not affected by the date filter") below the subtitle in the Pending by Plan card, with `.pending-plan-card__scope` style (font-size-11, color-text-placeholder). No logic changes. Build clean.

**Last updated:** 2026-06-17 — WI-ASSIGNMENTS-BULK-ACTIONS COMPLETE. Bulk checkbox selection + Activate/Deactivate/Delete (all-or-nothing) for assignments. "Already used" criterion: assignment is blocked if any CompensationPayout exists with same TenantId+PayeeId+PlanId where periods overlap. All-or-nothing: if ≥1 blocked → none deleted, frontend shows scrollable OverlapWarningComponent table of blocked items. Backend: new `Assignments.Delete` permission (TenantAdmin+CompManager), `PlanAssignment.Activate()` domain method, 4 handlers (ActivateAssignment, BulkActivate, BulkDeactivate, BulkDelete), 4 controller endpoints. Frontend: selection store signals (selectedIds, allSelected, someSelected, toggleSelect, toggleSelectAll, clearSelection), bulk API methods, bulk action bar, checkbox column, 3 confirmation modals + 1 blocked modal with OverlapWarningComponent, i18n EN/ES/PL. Backend tests: 602 (13 new). Frontend tests: 406/384 (22 pre-existing failures unchanged).

**Last updated:** 2026-06-17 — WI-EXPLANATION-GAPS (currency dropdown fix) COMPLETE. Bug root cause: `ws-select` always uses `position: fixed` for its dropdown. In upward mode the inline `[style.top.px]` is cleared (null), exposing the CSS `top: calc(100% + …)`. With `position: fixed`, that `100%` resolves to viewport height (~768px) instead of the element height — combined with the `bottom` constraint the dropdown collapses to zero height and is invisible. On a 1366×768 laptop the currency trigger is ~580px from viewport top, leaving only ~180px below (< 280px needed) → upward mode always triggers → "nothing happens." Fix: changed `[style.top.px]` binding to `[style.top]` (string), explicitly setting `'auto'` in upward+fixed mode to neutralize the CSS. Also changed default currency from `'USD'` to `'EUR'` (product is European). Pre-existing TypeScript type error in `subscription-wizard.component.spec.ts` (`CurrentUser` missing `emailConfirmed`+`isQualified`) also fixed. Build clean. Tests: 406 run → 384 pass, 22 pre-existing failures (ProcessPending HTTP mocks, SubscriptionReactivation HttpClient, PayRuns/Payouts pagination — unrelated to this WI).

**Last updated:** 2026-06-16 — UI-DASHBOARD-LAYOUT: Dashboard container cambiado de `maxWidth="wide"` (1440px) a estándar (1200px) para igualar el layout de planes. `action-grid` pasa a 2 columnas en ≤1100px. Breakpoint inferior unificado en 640px.

**Last updated:** 2026-06-16 — WI-PAYMENT-BLOCK-UI COMPLETE. El mensaje de bloqueo anti-doble-pago rediseñado: en vez de muro de texto rojo concatenado, ahora devuelve HTTP 409 Conflict con cuerpo estructurado `{ blocked: true, totalConflicts, conflicts: [{transactionReference, paidInPayoutId, paidInPayoutPeriodStart, paidInPayoutPeriodEnd}] }`. Handlers `MarkPayRunPaidHandler` + `MarkPayoutPaidHandler`: devuelven `Result<PaymentBlockResult?>` (null = pagado, non-null = bloqueado con datos). Comandos cambiados a `IRequest<Result<PaymentBlockResult?>>`. Controladores: 204 si pagado, 409 si bloqueado, 400 si error. Frontend: `pay-run-detail` + `payout-detail` capturan status 409 → populan signal `doublePayConflicts: OverlapRow[]` → renderizan `<app-overlap-warning>` reutilizando el componente existente; filas clickables → abren payout consumidor en nueva pestaña. i18n EN+ES+PL: `DOUBLE_PAY_BLOCKED` + `DOUBLE_PAY_COL_TX_REF` en `PAYOUTS.DETAIL` y `PAY_RUNS.DETAIL`. Tests de integración actualizados (blocked → IsSuccess=true, Value not null). Build backend (Application + IntegrationTests): 0 errores. Build frontend: 0 errores TS. 10/10 tests pasan.

**Last updated:** 2026-06-16 — WI-ANTI-DOUBLE-PAY-GUARD COMPLETE. Guarda bloqueante en el punto de pago implementada en las 3 rutas. Root cause: el mismo crédito aparecía en PayoutLines de 7 payouts distintos (todos calculados antes de pagar ninguno). Al pagar el primero, el segundo (y sucesivos) debían bloquearse — en vez de eso el código tenía "graceful skip" que dejaba pasar el pago. Fix: antes de consumir créditos, cada handler verifica si algún crédito ya tiene ConsumedAt != null → si sí, devuelve error claro ("Transaction SMOCK-XYZ was already paid in payout [2026-01-01 – 2026-01-31]") y NO pasa a Paid. Fix en 3 rutas: MarkPayRunPaidHandler (la que usa la UI), MarkPayoutPaidHandler (individual), BulkMarkPaidHandler (por payout, permite parciales). Concurrencia: Credit.RowVersion (timestamp SQL Server) como token de concurrencia optimista — DbUpdateConcurrencyException capturada con error claro. Migración B5_CreditRowVersion: columna RowVersion rowversion aplicada y registrada en __EFMigrationsHistory. AuditAction: PAYMENT_BLOCKED_DOUBLE_PAYMENT. Tests nuevos: 3 AntiDoublePay (PayingPayout_WithAlreadyConsumedCredit_IsBlocked_WithClearError, AfterPaymentBlock_RevertFirstPayout_SecondPayoutCanBePaid, BulkMarkPaid_WithOneConsumedCredit_BlocksConflictAndAllowsCleanPayouts) + 1 PayRunEngine (MarkPayRunPaid_WhenCreditAlreadyConsumedByAnotherPayout_ReturnsFailure). Total: 589 unit + 30 integration (9 AntiDoublePay + 21 PayRunEngine). Build 0 errores.

**Last updated:** 2026-06-16 — WI-TX-PAID-PROPAGATION-FIX COMPLETE. Bug root cause: `MarkPayRunPaidHandler` (ruta principal de la UI) llamaba `payout.MarkPaid()` en loop sin cargar `PayoutLines` (sin `.Include(p => p.Lines)`). Resultado: estado del Payout cambiaba a Paid pero cero créditos consumidos y cero transacciones marcadas Paid. Fix: handler ahora incluye Lines, batch-carga créditos y transacciones para todo el pay run, llama `credit.Consume()` + `tx.MarkPaid()` por cada payout, audit `PAYOUT_CREDITS_CONSUMED`. Constructor añade `ILogger<MarkPayRunPaidHandler>`. Test factory `PayRunEngineTests.MarkPaidHandler()` actualizado con `NullLogger`. Build: 0 errores. Tests: 589 unit + 26 integration (6 AntiDoublePay + 20 PayRunEngine) — todos pasan.

**Last updated:** 2026-06-16 — WI-ANTI-DOUBLE-PAY (Phase 3, B+C) COMPLETE. Anti-doble-pago completo: propagación de estado Paid a transacciones + consumo de créditos + exclusión en motor de cálculo + reversibilidad. Decisión documentada: consume al PAGAR (no al aprobar). Campos nuevos en `Credit`: `ConsumedAt` (DateTimeOffset?) + `ConsumedByPayoutId` (Guid?). Migración `B4_CreditConsumptionFields` aplicada. `CompensationTransaction.MarkPaid()` implementado (stub NotSupportedException eliminado). `CompensationTransaction.RevertPaidToCalculated()` nuevo método de dominio. `CompensationPayout.RevertPaidToApproved()` nuevo. Eventos de dominio: `CreditConsumedEvent`, `CreditUnconsumedEvent`, `TransactionMarkedPaidEvent`. `CalculatePayoutsForPeriodHandler`: filtro `c.ConsumedAt == null` añadido — ESTE ES EL FIX CENTRAL. `MarkPayoutPaidHandler` + `BulkMarkPaidHandler`: cargan Lines (Include), consumen créditos no-Superseded, marcan transacciones Calculated→Paid, audit `PAYOUT_CREDITS_CONSUMED`. `RevertPayoutToApprovedCommand` + `RevertPayoutToApprovedHandler`: Paid→Approved, unconsume créditos (ConsumedAt=null), revertir transacciones Paid→Calculated, audit `PAYOUT_REVERTED_TO_APPROVED`. Endpoint nuevo `POST /api/payouts/{id}/revert-paid`. AuditActions: `TransactionMarkedPaid`, `PayoutCreditsConsumed`, `PayoutRevertedToApproved`. Tests de motor intactos (8 pasan). PayRunEngineTests fijo (pre-existing NoOpAuditService faltante). Tests nuevos: 6 `AntiDoublePayTests` — MarkPaid propaga, solape excluye créditos consumidos, no-solape funciona normal, revert libera créditos, recalculación post-revert, aislamiento multi-tenant. Unit: 589 total (4 nuevos — MarkPaid desde Calculated, MarkPaid desde Pending falla, RevertPaidToCalculated, RevertFromPending falla; stub NotSupportedException eliminado). Build: 0 errores.

**Last updated:** 2026-06-16 — WI-PAYOUT-OVERLAP-GUARD COMPLETE. Aviso de solapamiento al aprobar/pagar payouts individuales y en bulk, reutilizando markup extraído a componente compartido. Backend: 4 nuevas audit constants (`PayoutApprovedWithOverlap`, `PayoutPaidWithOverlap`, `PayoutBulkApprovedWithOverlap`, `PayoutBulkPaidWithOverlap`); `GetPayoutOverlapsHandler` (por payee, período solapado, Approved/Paid) + `CheckPayoutsOverlapsHandler` (count bulk); `ApprovePayoutHandler` y `MarkPayoutPaidHandler` ahora inyectan `IAuthorizationService`, `IAuditService` y hacen check de solapamiento antes de la transición; `BulkApprovePayoutsHandler` + `BulkMarkPaidHandler` añaden IAuditService + overlap count batch; 2 nuevos endpoints `GET /api/payouts/{id}/overlaps` + `POST /api/payouts/overlaps-check`. Frontend: `OverlapRow` interface en shared models; `OverlapWarningComponent` en `shared/components/overlap-warning/` (extrae el markup de tabla que existía inline en pay-run-detail — zero duplication); `pay-run-detail` refactorizado para usar `<app-overlap-warning>` + computed signals `approveOverlapRows`/`markPaidOverlapRows`; `payout-detail` reemplaza `ws-confirmation-modal` por `ws-modal` + `<app-overlap-warning>`, método `openApproveConfirm()`/`openMarkPaidConfirm()` que fetcha overlaps al abrir; `payouts-list` añade `openBulkApproveConfirm()`/`openBulkMarkPaidConfirm()` que llaman `checkBulkOverlaps` mostrando count en modal; `OverlappingPayout` interface + `getOverlaps`/`checkBulkOverlaps` en `payouts.api.service.ts`. i18n EN/ES/PL: `OVERLAP_WARNING.COL_*` (shared), `PAYOUTS.DETAIL.OVERLAP_WARNING`, `PAYOUTS.DETAIL.OVERLAP_COL_PLAN`, `PAYOUTS.BULK_OVERLAP_WARNING`. 10 nuevos unit tests `GetPayoutOverlapsHandlerTests` (586 total). `ng build --configuration production` limpio.

**Last updated:** 2026-06-16 — WI-DELETE-DRAFT COMPLETE. Borrado permanente de pay runs en estado Draft (y SOLO Draft). Regla de oro: Approved y Paid nunca son borrables — validado en backend incondicionalmente. Cascada: payouts del Draft borrados explícitamente (FK Restrict), payout lines se cascadean automáticamente. Credits, CompensationTransactions y otros pay runs intactos. Backend: permiso `Payouts.DeleteDraft` en `Permission.cs` + `RolePermissions.cs` (TenantAdmin + CompManager); `DeletePayRunDraftCommand` + `DeletePayRunDraftHandler` (RequireAsync, status check, RemoveRange payouts, Remove payRun, SaveChanges, audit `PAY_RUN_DRAFT_DELETED`); `DELETE /api/pay-runs/{id}` — 204 success, 404 not found, 409 Conflict para Approved/Paid locked. UI: botón trash en columna de acciones de la lista (Draft + `*hasPermission="'Payouts.DeleteDraft'"`, opacity:0 → 1 on hover); botón "Delete draft" en header del detail (misma guardia); modales de confirmación con período interpolado + advertencia irreversible + variante `danger`; recarga lista tras borrado, navega a `/pay-runs` tras borrado desde detalle. i18n EN/ES/PL: 6 claves (`DELETE_DRAFT`, `DELETE_CONFIRM_TITLE`, `DELETE_CONFIRM_BODY`, `DELETE_CONFIRM_IRREVERSIBLE`, `DELETE_CONFIRM_BTN`, `DELETE_ERROR`). 7 nuevos unit tests (576 total). `ng build --configuration production` limpio.
**Last updated:** 2026-06-16 — WI-OVERLAP-GUARD COMPLETE. Anti doble-pago: antes de Aprobar o Marcar Pagado un pay run, el sistema carga los pay runs solapados (Approved/Paid) del mismo tenant y los muestra en una tabla scrolleable y clickable dentro del modal de confirmación existente. No bloquea la acción — solo informa. Lógica: `PeriodStart ≤ theirEnd AND PeriodEnd ≥ theirStart` (endpoints compartidos = solapamiento). Nuevo endpoint `GET /api/pay-runs/{id}/overlaps`. Audit override: `PAY_RUN_APPROVED_WITH_OVERLAP` y `PAY_RUN_PAID_WITH_OVERLAP` en `AuditActions.cs`; los handlers `ApprovePayRunHandler` y `MarkPayRunPaidHandler` inyectan `IAuditService` y loguean si había solapamiento al momento de la transición. Frontend: signal `approveOverlaps`/`markPaidOverlaps`; botones Approve/MarkPaid ahora llaman `openApproveConfirm()`/`openMarkPaidConfirm()` que fetchean los overlaps antes de abrir el modal; tabla con max-height 220px scrolleable, filas clickables → `window.open('/pay-runs/{id}', '_blank')`; skeleton mientras carga. i18n EN/ES/PL: `OVERLAP_WARNING`, `OVERLAP_COL_PERIOD`, `OVERLAP_COL_STATUS`, `OVERLAP_COL_PAYEES`, `OVERLAP_COL_TOTAL`. 11 nuevos unit tests (569 total, 558→569). Build backend (test project): 0 errores. `ng build --configuration production` 601.21 kB, limpio.
**Last updated:** 2026-06-16 — WI-PAYMENT-TRACEABILITY COMPLETE. Trazabilidad pago → transacción de origen expuesta en API + UI + PDF. `PayoutLineDto` extendido con 6 campos nullable: `TransactionId`, `TransactionReference`, `TransactionExternalId`, `TransactionDate`, `TransactionAmount`, `TransactionCurrency`. `GetPayoutByIdHandler.BuildLinesAsync` (ahora `public static`) resuelve la cadena `CreditId → TransactionId → CompensationTransaction` en 2 queries bulk (sin N+1). `ExportPayoutPdfHandler` reutiliza el mismo helper. PDF: nueva columna "Source Transaction" (Reference + fecha). UI: columna "Source Transaction" en la tabla de líneas — muestra `referenceNumber` en negrita + fecha en texto secundario; líneas sin fuente muestran "—". Multi-tenant seguro (global query filter cubre Credits y Transactions). i18n `COL_SOURCE` EN/ES/PL. 4 nuevos unit tests (reference, preserve fields, graceful null, multi-line). Total: 545→549. Build backend: 0 errores. `ng build --configuration production`: limpio.
**Last updated:** 2026-06-16 — WI-PAYMENT-SUBCRIPTION (2FA Reminder) COMPLETE. Toast de recordatorio de 2FA no intrusivo: aparece en esquina inferior izquierda 4s después de cargar (solo si el usuario NO tiene 2FA activo). Jerarquía de acciones: botón primario "Enable now" → navega a `/profile#security`, botón secundario "Remind me in 3 days" → guarda timestamp en localStorage, link de texto "Don't show again" → guarda flag permanente. Persistencia: `wasnie:2fa-reminder-snooze` (timestamp, 3 días) + `wasnie:2fa-reminder-dismissed` (flag permanente). Animación slide-in/out suave desde abajo. Si el usuario activa 2FA, la condición deja de cumplirse y el popup nunca vuelve. i18n EN/ES/PL completo (5 claves nuevas). `ng build --configuration production` limpio. Archivos: `shared/components/two-fa-reminder/*` (nuevo), `app-shell.component.ts/html` (mounting), `manage-profile.component.html` (id="security"), `assets/i18n/en|es|pl.json` (+REMINDER_* keys). LocalStorage keys: `wasnie:2fa-reminder-snooze`, `wasnie:2fa-reminder-dismissed`.
**Last updated:** 2026-06-15 — WI-2FA-TOTP COMPLETE. 2FA opcional por usuario (TOTP — Google Authenticator, Authy). Flujo activación: QR + secreto → confirmar código 6 dígitos → recovery codes (mostrados una vez). Flujo login: credentials OK + 2FA enabled → challenge JWT 5min → TOTP/recovery code → sesión completa. Desactivación y regeneración de recovery codes requieren doble verificación (contraseña + código). Rate limiting anti-brute-force (`auth-verify-2fa` 5/15min por IP, `profile-2fa` 10/5min por usuario). 6 nuevas audit actions. Sin migración DB (AspNetUserTokens existe desde InitialCreate). 14 nuevos unit tests; total 527→541. Build backend 0 errores. Build frontend limpio (budget angular.json 500→650kB; qrcode en chunk lazy). i18n EN/ES/PL completo (~40 claves/locale). Próximo: Payout Engine unit tests (deuda Critical Twelve #2) o frontend 2FA tests.
**Last updated:** 2026-06-15 — WI-MANAGE-PROFILE COMPLETE (migración B3 aplicada). Tabla `EmailChangeTokens` confirmada en DB. Fix: faltaba `20260615150000_B3_AddEmailChangeTokens.Designer.cs` — EF no indexaba la migración sin él. Creado Designer.cs con modelo completo; `dotnet ef migrations list` confirma B3 aplicada sin `(Pending)`. Backend build: 0 errores.
**Last updated previo:** 2026-06-15 — WI-MANAGE-PROFILE COMPLETE. Pantalla de gestión de perfil del usuario con dos secciones: (1) Datos personales editables: nombre (given_name/family_name claims), cambio de contraseña (exige actual, valida fortaleza ≥10+uppercase+digit+special, revoca todos los refresh tokens → re-login), cambio de email con re-verificación (token SHA256 una sola vez, 24h, enviado al nuevo email vía Resend, email no cambia hasta confirmar, comprobación de unicidad en el momento del request Y al confirmar, anti-abuse: cooldown 2min + cap 5/hora). (2) Datos de organización (solo lectura): nombre de compañía, organization identifier, borrado de cuenta (GDPR) → todos con "contáctanos" → `support@wasnie.com` / `privacy@wasnie.io` según contexto. Backend: dominio `EmailChangeToken` + migración `B3_AddEmailChangeTokens`; `IIdentityService` extendida con `VerifyPasswordAsync`, `ChangePasswordAsync`, `UpdateClaimAsync`, `ChangeEmailAsync`; `IEmailService` extendida con `SendEmailChangeConfirmationAsync` + template HTML EN/ES/PL; nuevas audit actions `PROFILE_NAME_UPDATED`, `PROFILE_PASSWORD_CHANGED`, `PROFILE_EMAIL_CHANGE_REQUESTED`, `PROFILE_EMAIL_CHANGE_CONFIRMED`; 5 handlers (`GetProfile`, `UpdateProfileName`, `ChangePassword`, `RequestEmailChange`, `ConfirmEmailChange`) + validadores; `ProfileController` (`GET /api/profile`, `PUT /api/profile/name`, `POST /api/profile/change-password`, `POST /api/profile/request-email-change`, `GET /api/profile/confirm-email-change`); rate limiting `profile-password` (5 req/5min per userId) + `profile-email-change` (3 req/5min per userId); snapshot EF actualizado. Frontend: `ProfileService`, `ManageProfileComponent` (signals, FormsModule, WsPageLayout + WsCard + WsInput + WsButton), `ConfirmEmailChangeComponent` (landing page para el link del email, AllowAnonymous, redirige a login tras confirmar), `profile.routes.ts`, ruta `/profile` + `/profile/confirm-email-change` en `app.routes.ts`; sidebar: "Profile" nav item (icono user, permission Payees.Read) en sección Settings; i18n EN/ES/PL completo (PROFILE.* — 38 claves, NAV.PROFILE). **Decisiones de seguridad**: cambio de password revoca TODOS los refresh tokens (el usuario debe volver a hacer login, consistente con `ResetPasswordCommandHandler`); email no cambia hasta que el usuario confirma desde el nuevo inbox. **Enlace de contacto GDPR**: `privacy@wasnie.io` (mismo que el flujo de reactivación). Build backend: 0 errores. Tests: 527/527 (sin regresión). Build frontend: 0 errores TS (warning de bundle preexistente sin cambio).
**Last updated:** 2026-06-15 — WI-LOGO-GRADIENT: Fondo del logo con gradiente por tier (planes de pago). La PNG del logo tiene fondo cyan opaco con "W" blanca → approach: span overlay `position:absolute; inset:0; mix-blend-mode:hue` sobre la imagen. `mix-blend-mode:hue` hace que la "W" blanca (acromática, sat=0) permanezca blanca mientras el fondo cyan adopta el hue del gradiente. `SidebarComponent` inyecta `SubscriptionStateService` (el singleton ya cargado por `AppShellComponent`) y expone `tierName = computed(...)`. 3 bloques `@if/else-if` en template con clases Tailwind literales para que el scanner las incluya. Mismos gradientes que el badge: Starter=purple→blue, Growth=cyan→blue, Scale=green→blue. Free: sin cambios. `.sidebar__brand-monogram` recibió `position:relative; overflow:hidden` (mejora también el clipping de bordes redondeados). **Cómo revertir:** eliminar los 3 bloques `@if` del overlay en `sidebar.component.html`. Build: 0 errores.
**Last updated:** 2026-06-15 — WI-PLAN-BADGE v2: Gradientes por tier + ícono check en badge de plan. Starter=purple→blue, Growth=cyan→blue, Scale=green→blue. Efecto "gradient border button": outer button tiene el gradiente como fondo, inner span tiene `bg-[var(--color-surface-raised)]` (topbar background) revelando solo un borde gradiente; hover → span se vuelve transparente → relleno gradiente completo + `hover:text-white`. SVG rosette-discount-check (16×16, `stroke="currentColor"`) incrustado inline en cada badge. 3 bloques `@if/else-if` en template para que Tailwind detecte las clases literal. Clases no existentes en el proyecto mapeadas: `bg-neutral-primary-soft`→`bg-[var(--color-surface-raised)]`, `text-heading`→`text-[var(--color-fg-primary)]`, `rounded-base`→`rounded-lg`/`rounded-md`; `dark:*` omitidos (CSS vars manejan dark mode). SCSS `.topbar__plan-badge` previo eliminado. Build: 0 errores.
**Last updated:** 2026-06-15 — WI-PLAN-BADGE: Badge del plan activo en la barra superior para planes de pago. `TopbarComponent` — añadidos `isPaidTier` + `tierName` computed signals (Starter/Growth/Scale); badge `<button class="topbar__plan-badge" [data-tier]="...">` con dot indicator + nombre del plan; clicable → `/subscription`. Estilos por tier con tokens del design system: Starter = info (azul), Growth = warning (ámbar), Scale = brand (brand color). Free sin cambios (botón Upgrade intacto). Estados de borde: `_tier` empieza en `null` → badge invisible hasta resolver API; error silencioso → sin badge. i18n `TOPBAR.PLAN_BADGE_TOOLTIP` EN+ES+PL. Build: 0 errores (warnings pre-existentes sin cambio).
**Last updated:** 2026-06-15 — WI-TENANT-SETTINGS v2: Seed completado — 7 campos en vez de 2. Root cause: `RegisterTenantCommandHandler` sembraba solo `Payee/Email` + `Payee/HireDate`; faltan `Payee/Role`, `Payee/ManagerId`, `Payee/EmploymentType`, `Payee/Location`, `Transaction/PayeeId`. Fuente canónica: 3 migraciones (`P2_FieldRequirementSettings` + `P2_PayeeNewColumns` + `P2_PayeeLifecycle`) + constantes `PayeeFieldNames` / `TransactionFieldNames`. Fix: `RegisterTenantCommandHandler` ahora usa las constantes y siembra los 7 campos (todos `isRequired: false`). Repair SQL: 5 INSERTs con `WHERE NOT EXISTS` repararon 7 tenants afectados por el fix incompleto anterior. Verificación DB: todos los 17 tenants muestran FieldCount=7, sin duplicados. Build: 0 errores. Verificación runtime pendiente: crear tenant nuevo → /admin → 7 campos.
**Last updated:** 2026-06-15 — WI-TENANT-SETTINGS: Two bugs fixed. (1) Field Requirements section rendered title/desc but no controls for tenants created after migration `P2_FieldRequirementSettings` — root cause: `RegisterTenantCommandHandler` never seeded `FieldRequirementSetting` rows (the migration comment said "set by the tenant-creation path" but that path was never implemented). Fix: added seeding of 2 rows (`Payee/Email` + `Payee/HireDate`, both `isRequired: false`) in `RegisterTenantCommandHandler` after `Tenants.Add()`. Repair SQL applied via `sqlcmd`: 8 affected existing tenants now have the missing rows (`INSERT … SELECT … WHERE NOT EXISTS`). Build: 0 errors. (2) Architecture amendment: `ARCHITECTURE.md` v1.0→v1.1 + `08-breaking-change-protocol.md` v1.0→v1.1 — added Critical Rule 13 + Rule 8.4.4 mandating migrations must be applied and verified before reporting WI complete. Verification required: restart API → founder tests `/admin` → Field Requirements shows Email + HireDate controls; register new tenant → same controls present.
**Last updated:** 2026-06-15 — WI-PASSWORD-RESET-DEBUG: Silent failure diagnosed and fixed. Two root causes: (1) IP rate limiter (`auth-password-reset`, 3 req/5 min) rejected requests silently after multiple test attempts — fixed by adding `options.OnRejected` callback in `Program.cs` that logs `[DEV] Rate limit exceeded: {Method} {Path} from {IP}` and returns JSON `{"code":"rate_limited"}`. (2) Branch 1 of `RequestPasswordResetCommandHandler` (user not found) returned silent success with NO log — fixed by adding `[DEV] Password reset: no account found for {EmailMask}` info log before the early return (anti-enumeration maintained in HTTP response). Application project builds 0 errors. Api project confirmed syntactically correct (DLL locked by running process — needs restart to rebuild). Verification pending: restart API → test with founder email → confirm one of the three `[DEV]` lines appears (rate-limited / no-account / reset link).
**Last updated:** 2026-06-15 — WI-PASSWORD-RESET COMPLETE. Full forgot-password/reset-password flow. Backend: `PasswordResetToken` domain entity (SHA256-hashed, single-use, 30-min expiry, `MarkUsed()`); `PasswordResetTokenConfiguration` (EF, `PasswordResetTokens` table); `RequestPasswordResetCommand` + handler (anti-enumeration: unknown/unconfirmed → silent 200; 2-min cooldown per user; 5/hr cap; invalidates prior unused tokens; logs reset URL at Info level before send); `ResetPasswordCommand` + handler (validates hash+expiry+used, password strength ≥10+uppercase+digit+special, marks token used, revokes all refresh tokens); `IIdentityService.ResetPasswordAsync` + impl; `AuditActions.PasswordResetRequested/Completed`; `auth-password-reset` IP-partitioned rate limiter (3 req/5 min); 2 new endpoints (`POST /api/auth/request-password-reset`, `POST /api/auth/reset-password`); migration `B2_PasswordResetTokens` applied. Frontend: `AuthService.requestPasswordReset/resetPassword()` methods; `forgot-password` component (email form → success state with lock icon); `reset-password` component (reads userId+token from URL, password+confirm form, invalid-link state); `auth.routes.ts` extended; `login.component` gains `passwordResetSuccess` signal + banner + "Forgot password?" link; i18n EN+ES+PL complete (`FORGOT_PASSWORD.*`, `RESET_PASSWORD.*`, `AUTH.FORGOT_PASSWORD`, `AUTH.PASSWORD_RESET_SUCCESS`, `AUTH.BACK_TO_LOGIN`). Tests: 8 new unit tests in `PasswordResetHandlerTests.cs` (anti-enumeration, cooldown, cap, valid flow, expired/used/notfound tokens, previous-token invalidation). Unit total: 519→527 (+8). Backend build: 0 errors. Frontend build: 0 errors (pre-existing bundle budget warning). Next: Payout Engine A.4.
**Last updated:** 2026-06-15 — WI-PAYMENT-SUBSCRIPTION (cont.) + WI-EMAIL-ACTIVATION COMPLETE. (1) SVG empty-state icons replaced with Tabler icons (plans→vocabulary, quota→businessplan, assignments→reorder, payees→users, transactions→receipt-euro); CSS controls size (removed hardcoded w/h attributes). (2) Dev rate limiter disabled: `GlobalLimiter` now wrapped in `if (!builder.Environment.IsDevelopment())` — fixes 429 on SPA navigation. (3) Full Resend + activation funnel: FASE 1 — `IEmailService` abstraction + `ResendEmailService` (direct HTTP, named client, empty ApiKey → graceful skip); HTML email templates EN/ES/PL. FASE 2 — Registration → email confirmation (real 32-byte random token, SHA256-hashed storage, 48h expiry, single-use, invalidated on use; dev: raw URL logged at Info level; `EmailConfirmationToken` domain entity). FASE 3 — Mandatory qualification form (Country, Phone, HowHeard, SalesVolume, CurrentSystem, LegalAcceptance; `Tenant.Qualify()` domain method; `CompleteQualificationCommandHandler` idempotent). FASE 4 — `ActivationEnforcementMiddleware` hard-gates in order: email confirmed → qualified → full access (runs before SubscriptionEnforcementMiddleware). Login returns `EMAIL_NOT_CONFIRMED` error code when unconfirmed. `CurrentUserDto` extended with `EmailConfirmed` + `IsQualified`. Frontend: `confirm-email-pending` page, `confirm-email` callback page (auto-confirm on load, success → /onboarding/qualify), `qualification` form page (2-col WsFormGrid, legal checkbox), `qualificationGuard`, updated `planGuard`/`onboardingGuard`. Login shows unconfirmed warning + resend link. Register redirects to `/auth/confirm-email-pending`. Migration `20260615000000_B1_EmailConfirmationAndQualification` (9 qualification cols on Tenants + EmailConfirmationTokens table). ApiKey in `appsettings.Development.json` (empty — founder fills manually). i18n EN/ES/PL complete (CONFIRM_EMAIL.* + QUALIFY.* + AUTH.EMAIL_NOT_CONFIRMED + AUTH.RESEND_CONFIRMATION). Backend build: 0 errors. Frontend build: 0 errors (1 pre-existing bundle budget warning). Next: backend tests for email confirmation flow + activation middleware + qualification command.
**Last updated:** 2026-06-13 — WI-WIZARD-CAPABILITIES: 9 capacidades añadidas al wizard de sign-up (paso 2 — selección de plan). Sección "Incluido en todos los planes" entre trust-bar y tabla de precios: grid 3×3 con checkmark brand, nombre y descripción corta. i18n EN/ES/PL completo (20 claves nuevas en ONBOARDING). Build production limpio. Rep Portal marcado ⛔ FUERA DE ALCANCE en inventario.
**Last updated:** 2026-06-13 — WI-MONEY-TESTS COMPLETE. Closed Critical Twelve #2 debt. Extracted 13 private static methods from `CreditAllocationService` into new `internal static CommissionCalculator` class (same logic, `internal` visibility enables unit testing without DB). `InternalsVisibleTo("Wasnie.UnitTests")` was already configured. Wrote 63 new unit tests in `Wasnie.UnitTests/Calculation/CommissionCalculatorTests.cs` covering: Flat rate (6), Tiered (7 + boundary conditions), AttainmentBased (7 + LastOrDefault boundary), Modifier (5), Cap (8 inc. currency mismatch + deferred scopes), Floor (5), Trigger evaluation (13 inc. AND/OR/date/string/In/NotIn/unknown-field), banker's rounding Theory (4 cases), multi-currency isolation (3), determinism (2), pipeline integration (3). Total unit tests: 454→517 (+63). Build: 0 errors, 0 warnings.
**Last updated:** 2026-06-13 — WI-FEATURE-INVENTORY + WI-WIZARD-FEATURES-SECTION: Inventario real de módulos (desde el código) añadido como sección consolidada. Sección de features insertada en el wizard de onboarding (entre hero y tabla de precios). Ver "Feature Inventory" más abajo.
**Last updated:** 2026-06-12 — WI-TEST-PAYMENT-CYCLE BUG FIXES APPLIED. 3 integration test failures fixed: (1) `PaymentFailedCycleTests.ReadAuditByResourceId` missing `.IgnoreQueryFilters()` → added (same pattern as `ReadSubscription`); (2) `AssignmentsEndpointsTests.DisposeAsync` was `Task.CompletedTask` → now calls `ResetCompensationDataAsync()` to prevent EMP-coded payee leakage into subsequent test classes; (3) `SubscriptionEnforcementTests.GetPlans_CanceledTenant_Returns200` used shared factory with no Stripe mock → now uses `WithWebHostBuilder` + local `StubPlanService`. Build: 0 errors, 1 pre-existing warning. Pre-existing failures (`ListAssignmentsByPayee`, `Dashboard.*PendingByPlanItems*`) unrelated to this WI.
**Last updated:** 2026-06-12 — WI-TEST-PAYMENT-CYCLE COMPLETE. Nuevo test de integración `PaymentFailedCycleTests.cs` (6 tests) que cubre el ciclo completo de pago fallido via webhooks sintéticos (Approach A). Casos cubiertos: (1) payment_failed → PastDue: tier intacto + audit SUBSCRIPTION_PAST_DUE; (2) PastDue no bloquea enforcement (solo Canceled bloquea); (3) payment_succeeded → Active: audit SUBSCRIPTION_RECOVERED; (4) subscription.deleted → Canceled: CanceledAt set, CancelAtPeriodEnd=false, CancelAt=null, audit SUBSCRIPTION_CANCELED, enforcement 402; (5) idempotency: mismo event ID → ProcessedStripeEvents.Count=1; (6) multi-tenant: payment_failed para TenantA no afecta TenantB. Código compila limpio (unit tests 0 errores); build de IntegrationTests bloqueado por lock del API en ejecución — requiere API detenida para compilar y ejecutar. Comando: `dotnet test tests\Wasnie.IntegrationTests\... --filter "FullyQualifiedName~PaymentFailedCycleTests"`.
**Last updated:** 2026-06-12 — WI-MODAL-CONFIRM-PLAN-CHANGE COMPLETE. Intercept de doble confirmación antes de cualquier cambio de plan (paid→paid). Frontend only. Flujo: botón en tabla de planes → `requestPlanChange(plan)` → modal `WsModal` → confirmar/cancelar → `confirmPlanChange()` → API → polling. Free→paid sigue yendo directo a Stripe Checkout (sin modal). Modal upgrade: caja de cargo destacada (€X en font grande, etiqueta uppercase) + párrafo de qué cambia + botón "Confirm and pay €X". Modal downgrade: banner verde "Nothing charged today" + párrafo de límites + bloque warning amber con periodo pagado hasta + nota de próxima factura + botón "Confirm switch to X". Loading state en botón confirm, `[closable]="!confirmingPlan()"` previene cierre durante llamada API. 409 blocked → cierra modal y muestra `blockedInfo` alert existente (sin cambios). upgrade_payment_failed → cierra modal y muestra toast. Señales: `confirmModalOpen`, `confirmPlan`, `confirmingPlan`. i18n EN/ES/PL: 10 nuevas claves por locale (`CONFIRM_MODAL_TITLE`, `CONFIRM_UPGRADE_CHARGE_LABEL`, `CONFIRM_UPGRADE_DETAIL`, `CONFIRM_UPGRADE_BTN`, `CONFIRM_DOWNGRADE_NO_CHARGE`, `CONFIRM_DOWNGRADE_IMMEDIATE`, `CONFIRM_DOWNGRADE_WARNING`, `CONFIRM_DOWNGRADE_NEXT`, `CONFIRM_DOWNGRADE_BTN`). `WsModalComponent` añadido a imports del componente. `ng build --configuration production` limpio (0 errores, warnings preexistentes sin cambio). No hay cambios backend.
**Last updated:** 2026-06-12 — WI-FIX-STALE-TIER-UPGRADE-ROUTING COMPLETE. Root cause: `ChangePlanCommandHandler` leía `currentTier` de la DB (solo actualizada por webhook `customer.subscription.updated`, que llega 1-2s después de un cambio). En cambios rápidos consecutivos la DB estaba stale → `isUpgrade = targetTier > currentTier` tomaba la rama equivocada → upgrade enrutado como downgrade (`create_prorations`) → cobro omitido. Fix (Opción B): antes de calcular `isUpgrade`, el handler llama `IStripeSubscriptionManagementService.GetCurrentTierFromStripeAsync` para leer el tier autoritativo de Stripe. Si Stripe falla o no puede mapear el precio → `Failure("plan_change_unavailable")`, no se cobra nada. Si la DB diverge → `UserSubscription.SyncTier()` la corrige + audit `SUBSCRIPTION_TIER_SYNCED_FROM_STRIPE` (SYSTEM actor). `isUpgrade` se calcula siempre con el tier de Stripe. Archivos modificados: `IStripeSubscriptionManagementService` (+método `GetCurrentTierFromStripeAsync` → `Tier?`); `StripeSubscriptionManagementService` (+ILogger inyectado, implementación que expande `items.data.price.product` y llama `StripeSubscriptionPlanService.ResolveTier`); `UserSubscription` (+método dominio `SyncTier(tier, now)`); `AuditActions` (+`SUBSCRIPTION_TIER_SYNCED_FROM_STRIPE`); `ChangePlanCommandHandler` (+IClock + IAuditService en constructor, nueva lógica de lectura Stripe + sync + abort seguro). Tests: +6 unit tests nuevos (stale DB → upgrade path correcto, DB sincronizada, upgrades consecutivos, Stripe falla → abort limpio ×3). 454/454 unit tests pasan. Build 0 errores, 0 warnings. Dev data fix sugerido (confirmar contra Stripe antes de ejecutar): `UPDATE UserSubscriptions SET Tier = <tier_real_en_Stripe> WHERE StripeSubscriptionId = '<sub_id_del_tenant>'`.
**Last updated:** 2026-06-12 — WI-PAYMENT-UPGRADE-DOWNGRADE COMPLETE. Upgrade ahora cobra el precio completo inmediatamente y resetea el ciclo de facturación; downgrade no cobra nada, aplica crédito al próximo ciclo. Backend: `IStripeSubscriptionManagementService` extendida con `UpgradeSubscriptionAsync` (Stripe params: `ProrationBehavior="none"`, `BillingCycleAnchor=SubscriptionBillingCycleAnchor.Now`, `PaymentBehavior="error_if_incomplete"`); `UpdateSubscriptionAsync` renombrado semánticamente como "downgrade" (conserva `create_prorations`); `ChangePlanCommandHandler` detecta `targetTier > currentTier` para rutear upgrade vs downgrade; en caso de pago declinado en upgrade, `StripeException` es capturada y devuelve `Failure("upgrade_payment_failed")`; `HandleSubscriptionUpdatedAsync` en `StripeWebhookService` ya no hardcodea `status=Active` — mapea `fullSubscription.Status` a `SubscriptionStatus` enum con switch expression. Frontend: `manage-subscription.component.ts` distingue error de pago (`err.error.message === 'upgrade_payment_failed'`) y muestra toast específico; `manage-subscription.component.html` añade notas de facturación bajo cada botón (upgrade: "€X cobrado inmediatamente, nuevo ciclo hoy"; downgrade: "efectivo ahora, próxima factura al nuevo precio"). i18n EN/ES/PL: 3 nuevas claves (`UPGRADE_BILLING_NOTE`, `DOWNGRADE_BILLING_NOTE`, `UPGRADE_PAYMENT_FAILED`). Tests: 15 nuevos unit tests en `ChangePlanCommandHandlerTests.cs` (upgrade llama UpgradeSubscriptionAsync, downgrade llama UpdateSubscriptionAsync, pago rechazado → Failure("upgrade_payment_failed"), no llama stripe cuando bloqueado, multi-tenant isolation); 3 nuevos integration tests (`ChangePlan_ValidDowngrade_Returns200Pending`, `ChangePlan_UpgradePaymentFailed_Returns400WithUpgradePaymentFailedCode`); stubs actualizados con `UpgradeSubscriptionAsync`. Build: 0 errores. Unit tests: 448/448. Angular build production limpio.

**Last updated:** 2026-06-12 — WI-FIX-STALE-CANCEL-FIELDS-ON-REACTIVATION COMPLETE. Step 0 audit: 4 activation paths found — `checkout.session.completed`, `subscription.updated`, `subscription.deleted`, `invoice.payment_succeeded`. Root cause: `UserSubscription.UpdateFromStripe()` updated tier/status/period but never cleared `CancelAtPeriodEnd`, `CancelAt`, `CanceledAt`. Fix 1 (domain): `UpdateFromStripe` now always resets all 3 fields (`CancelAtPeriodEnd=false, CancelAt=null, CanceledAt=null`) — invariant: "freshly activated subscription has no pending cancellation and is not canceled." `HandleSubscriptionUpdatedAsync` is safe: it reads `wasCancelScheduled` BEFORE calling `UpdateFromStripe`, then repopulates via `ScheduleCancellation` if Stripe signals it. Fix 2 (domain): `Recover()` now also clears `CanceledAt` (defensive). Fix 3 (frontend): `isCancelScheduled` already guarded by `status === 'Active'` — confirmed correct, no change. Dev data fix (corrupted row cus_UgWEoo6pTQLm7j): run `UPDATE UserSubscriptions SET CancelAtPeriodEnd=0, CancelAt=NULL, CanceledAt=NULL WHERE StripeCustomerId='cus_UgWEoo6pTQLm7j'` against dev DB. Backend unit: 433/433 (13 new — 4 `UpdateFromStripe` clears, 1 `Recover`, 8 existing unchanged). Integration test: `ReactivationCancellationFieldsTests.cs` (8 tests: CancelAtPeriodEnd, CancelAt, CanceledAt, Status, SubscriptionId, Period, ScheduledThenCanceled, multi-tenant isolation) — run with API stopped.
**Last updated:** 2026-06-12 — WI-FIX-REACTIVATION-TIER-LIMITS COMPLETE. Reactivation screen now enforces tier limits before allowing plan selection. Backend: `CreateCheckoutSessionHandler` validates payee+plan usage against `TierLimits.Limits[targetTier]` before creating Stripe session — returns `CheckoutResultDto(Blocked:true, BlockedReason:"payees"|"plans", Current, Limit, TargetTier)` → controller returns 409 Conflict; new `GET /api/subscription/usage` endpoint (`GetSubscriptionUsageQuery/Handler`) returns `{payeeCount, planCount}`, exempt from `SubscriptionEnforcementMiddleware`; new DTOs: `CheckoutResultDto`, `SubscriptionUsageDto`; 5 new integration tests (`CheckoutTierLimitTests`). Frontend: `SubscriptionService.getUsage()`; `SubscriptionReactivationComponent` loads plans+usage via `forkJoin`; `isPlanBlocked/ByPayees/ByPlans` methods; blocked plan cards show `--blocked` SCSS modifier (dimmed, sunken bg), warning badge, and usage note; `Reactivate` button disabled when plan is blocked; 3 new i18n keys per locale (`REACTIVATION_PLAN_UNAVAILABLE`, `REACTIVATION_PLAN_OVER_PAYEE_LIMIT`, `REACTIVATION_PLAN_OVER_PLAN_LIMIT`) EN+ES+PL; 7 new unit tests (isPlanBlocked*, unlimited plans, null usage guard). Build: ng build production clean, 401/406 frontend (5 pre-existing ProcessPendingComponent). Backend unit build clean.
**Last updated:** 2026-06-12 — WI-FIX-CANCELED-FIELDS COMPLETE. Bug 1 (dominio): `UserSubscription.Cancel()` ahora limpia `CancelAtPeriodEnd=false, CancelAt=null` (invariante de dominio: cancelación definitiva implica no hay schedule pendiente). Handler `HandleSubscriptionDeletedAsync` sin cambios — hereda la corrección. Bug 2 (UX): `ManageSubscriptionComponent.load()` detecta `status=Canceled` → `router.navigate(['/subscription/reactivate'])` inmediato; `isCancelScheduled` guardado con `status === 'Active'` para no mostrar banner amber con datos sucios. Backend: 10/10 unit tests (2 nuevos). Frontend: 4/4 nuevos tests. Integración test añadido (ejecutar con API detenida).
**Last updated:** 2026-06-12 — WI-MANAGE-SUBSCRIPTION-UI COMPLETE. `manage-subscription` migrado a `ws-page-layout` con `icon="brand-stripe"`; espaciado título/subtítulo de sección Upgrade corregido (4px `--space-1` via `.upgrade-section__header-group`). Build production limpio.
**Last updated:** 2026-06-12 — WI-CANCELED-SCREEN COMPLETE. Pantalla de cuenta cancelada + bloqueo total server-side + reactivación vía checkout + aviso GDPR. Backend: `SubscriptionEnforcementMiddleware` bloqueando HTTP 402 para tenants con `Status=Canceled` en todas las rutas funcionales; exento: `/api/subscription/webhook`, `/current`, `/checkout`, `/config`, `/plans`, `/api/auth/`, `/health`; structured logging de accesos bloqueados. Frontend: `SubscriptionReactivationComponent` reemplazado (era stub) por pantalla completa fuera del app shell: estado cancelado + fecha `CanceledAt` + plan anterior + tarjetas de planes para seleccionar y reactivar (checkout vía `createCheckout(priceId)`) + aviso GDPR informativo (correo placeholder: **privacy@wasnie.io** — reemplazar por el correo real). i18n EN/ES/PL: 14 nuevas claves (`REACTIVATION_CANCELED_ON`, `REACTIVATION_PREVIOUS_PLAN`, `REACTIVATION_DATA_PRESERVED`, `REACTIVATION_PLANS_TITLE`, `REACTIVATION_PAYEES_UP_TO`, `REACTIVATION_PLANS_UP_TO`, `REACTIVATION_CHECKOUT_ERROR`, `REACTIVATION_GDPR_TITLE/BODY/CONTACT/EMAIL`). Tests: 8 nuevos frontend (`subscription-reactivation.component.spec.ts`, 8/8 pass); 9 nuevos integration (`SubscriptionEnforcementTests.cs` — 402 para Canceled en GET payees/plans/transactions/credits/quotas/assignments, body con `code:"subscription_canceled"`, 200 en rutas exentas, 401 no-auth no bloqueado, multi-tenant aislamiento). Build: 0 errores backend, ng build production limpio. Frontend: 396 total (391 pass, 5 pre-existing ProcessPendingComponent). Backend unit: 418/418.
**Last updated:** 2026-06-12 — WI-FIX-CANCEL-AT-PERIOD-END v2 COMPLETE. Segunda causa raíz confirmada: billing_mode=flexible (dahlia) señaliza cancelación con `cancel_at` (timestamp) y deja `cancel_at_period_end=false`. Fix: `HandleSubscriptionUpdatedAsync` ahora usa `isCancelScheduled = stripeSubscription.CancelAtPeriodEnd || stripeSubscription.CancelAt.HasValue`. Soporta ambos modos (classic + flexible) sin break. Structured logging añadido (registra ambos campos en cada evento). Auditoria de `CurrentPeriodEnd/Start`: todos los usos en el handler leen de `item` (nivel item), no de `subscription` (deprecado en dahlia) — ningún cambio adicional necesario. Build: 0 errores. 426/426 unit tests. Smoke: Dar "Don't cancel" → volver a cancelar → nuevo evento → verificar `CancelAtPeriodEnd=1`, `CancelAt=2026-07-11` en DB, banner en UI.
**Last updated:** 2026-06-12 — WI-FIX-CANCEL-AT-PERIOD-END COMPLETE. Root cause found: `HandleSubscriptionUpdatedAsync` read `cancel_at_period_end` from `fullSubscription` (a re-fetch via Stripe GET), but Stripe sends the webhook *before* the GET endpoint reflects the new state — race condition caused `fullSubscription.CancelAtPeriodEnd` to return false. Fix: line 338 of `StripeWebhookService.cs` now reads `stripeSubscription.CancelAtPeriodEnd` (event payload) instead of `fullSubscription.CancelAtPeriodEnd` (re-fetch); same for `CancelAt`. `fullSubscription` is still fetched for product/tier resolution. Two pre-existing integration-test build errors also fixed: `StubStripeManagement.RevertCancellationAsync` stub added; `PayeeImportExecutionServiceTests.CreateSut` updated with `ITierLimitChecker` substitute. Build clean (0 errors). 426/426 backend unit tests.
**Last updated:** 2026-06-12 — WI-PAYMENT-SUBSCRIPTION COMPLETE. `cancel_at_period_end` (cancelación programada al final del periodo) manejado end-to-end. Backend: 2 nuevos campos de dominio en `UserSubscription` (`CancelAtPeriodEnd: bool`, `CancelAt: DateTimeOffset?`) con métodos `ScheduleCancellation(cancelAt, now)` y `ClearCancellationSchedule(now)`; 2 nuevas audit actions (`SUBSCRIPTION_CANCEL_SCHEDULED`, `SUBSCRIPTION_CANCEL_REVERTED`); `HandleSubscriptionUpdatedAsync` en `StripeWebhookService` ahora lee `fullSubscription.CancelAtPeriodEnd`/`CancelAt` y llama los métodos de dominio correctos (toggle en ambos sentidos — schedules/reverts); `UserSubscriptionDto` + `GetCurrentSubscriptionHandler` exponen los 2 nuevos campos; `IStripeSubscriptionManagementService.RevertCancellationAsync` + implementación (`SubscriptionUpdateOptions { CancelAtPeriodEnd = false }`); `RevertSubscriptionCancellationCommand` + handler (idempotente — no-op si ya no está programada, audita con `ICurrentUserService`); `POST /api/subscription/revert-cancellation` en controller; migración EF `A10_AddCancelAtPeriodEnd` escrita manualmente y aplicada (`dotnet ef database update --configuration Release`). Frontend: `CurrentSubscription` interface +2 campos; `SubscriptionService.revertCancellation()`; `ManageSubscriptionComponent` + getter `isCancelScheduled` + signal `revertingCancellation` + método `revertCancellation()`; banner amber `cancel-scheduled-banner` con fecha, aviso informativo, y botón "Mantener mi suscripción" (visible solo cuando `cancelAtPeriodEnd=true`); i18n EN/ES/PL: 4 nuevas claves (`CANCEL_SCHEDULED_TITLE`, `CANCEL_SCHEDULED_DESC`, `KEEP_SUBSCRIPTION_BTN`, `REVERT_CANCEL_ERROR`). Tests: 8 nuevos unit tests de dominio (`UserSubscriptionCancellationTests.cs` — `ScheduleCancellation` + `ClearCancellationSchedule`, idempotencia, Status permanece Active); test fixture de `SubscriptionSuccessComponent.spec.ts` actualizado con los 2 nuevos campos. Backend unit: 418/418. Frontend: 383/388 (5 pre-existing `ProcessPendingComponent` failures sin cambio). Build production ✔. Botón "Mantener suscripción" incluido (llama directamente a Stripe via `RevertCancellationAsync`).
**Last updated:** 2026-06-11 — WI-IMPORT-TIER-LIMIT COMPLETE. Excel payee import now enforces tier limits. Backend: `ITierLimitChecker` extended with `CheckPayeeImportLimitAsync(incomingCount)` returning structured `PayeeImportLimitCheck` record; `TierLimitChecker` implementation counts `current + incomingCount > MaxPayees` and returns blocked result (with audit log) without throwing; `PayeeImportResult` extended with `Blocked`, `BlockedCurrent`, `BlockedIncoming`, `BlockedLimit`, `BlockedTier`; `PayeeImportExecutionService` runs pre-flight check after building `toImport` list but before any DB writes — returns all-or-nothing blocked result; `ImportsController.ExecutePayees` returns `409 { blocked, reason, current, incoming, limit, tier }` on block. Frontend: `PayeeImportResult` model extended with blocked fields; `TierLimitInfo` extended with `incomingCount?`; `TierLimitModalComponent` handles import-specific message (entityKey=payees + incomingCount); `PayeeImportingStepComponent` catches 409/blocked and shows `TierLimitModal` then emits `retryRequested`; `PayeeImportWizardComponent` injects `PayeesStore`+`SubscriptionStateService`+`TIER_LIMITS`, adds `atPayeesLimit` computed; `PreviewStepComponent` gets `atPayeesLimit` input, Import button disabled when at limit; EN/ES/PL `TIER_LIMIT.MESSAGE_PAYEES_IMPORT` added (with tier/limit/current/incoming params). Build: `ng build --configuration production` clean.
**Last updated:** 2026-06-11 — WI-TIER-LIMIT-PREVENTIVE COMPLETE. Preventive tier-limit blocking for Plans and Payees create buttons. New `tier-limits.ts` constants (Free: 1p/5payee, Starter: 5/25, Growth: 15/75, Scale: ∞/150, Enterprise: ∞/∞). `PlansStore` + `PayeesStore` gained `unfilteredTotal` signal (updated on unfiltered loads). `PlansListComponent` + `PayeesListComponent` gained `atPlansLimit`/`atPayeesLimit` computed signals; create buttons route via `onCreatePlan()`/`onCreatePayee()` — show modal when at limit, navigate when not. `TierLimitInfo` extended with optional `entityKey: 'plans' | 'payees'`. `TierLimitModalComponent` redesigned: icon, entity-specific message, usage bar with percentage fill, usage numbers, upgrade navigates to `/subscription`. EN/ES/PL i18n: `MESSAGE_PLANS` + `MESSAGE_PAYEES` keys added. Build clean.
**Last updated:** 2026-06-11 — WI-STRIPE-FASE-3 COMPLETE. Upgrade/downgrade con proration inmediata, Stripe Billing Portal, ciclo completo PastDue→grace→bloqueo→reactivación. Backend: `ChangePlanCommandHandler` (valida tier, bloquea downgrade si payees/planes exceden `TierLimits[target]`, llama Stripe `UpdateAsync` con `create_prorations`, devuelve `{ pending:true }` o `{ blocked:true, reason, current, limit }`); `CreateBillingPortalSessionCommandHandler` (genera URL Stripe Billing Portal); 4 nuevos handlers webhook (`customer.subscription.updated` sync tier, `customer.subscription.deleted` → Canceled, `invoice.payment_failed` → PastDue, `invoice.payment_succeeded` → recover); `IStripeSubscriptionManagementService` limpiado (solo 2 métodos — Application layer no puede referenciar Stripe.net); `DI` registrado. Frontend: `SubscriptionStateService` (signals: isPastDue, isCanceled, load/refresh); `subscription.guard.ts` (redirige Canceled → /subscription/reactivate); `PastDueBannerComponent` (banner en AppShell cuando isPastDue); `SubscriptionReactivationComponent` (full-page sin app-shell); `ManageSubscriptionComponent` actualizado con botones upgrade/downgrade/portal y mensaje de bloqueo; `app.routes.ts` con `subscriptionGuard` en todas las rutas protegidas; `subscription.routes.ts` + ruta `/subscription/reactivate`; i18n EN/ES/PL completos (16 nuevas claves). Tests: `ChangePlanEndpointsTests` (auth, tiers inválidos, downgrade bloqueado por payees, upgrade → 200 pending); `WebhookPhase3Tests` (payment_failed→PastDue, payment_succeeded→recover, subscription.deleted→Canceled, idempotencia). Ambas builds limpias. Test count backend unit: 418. Frontend build production ✔.
**Last updated:** 2026-06-11 — WI-STRIPE-FASE-3 IN PROGRESS (Step 0 aprobado). Diseño de upgrade/downgrade con proration inmediata (`create_prorations`) aprobado. Downgrade bloqueado si el tenant excede los límites del tier destino (cuenta `db.Payees.CountAsync()` + `db.CompensationPlans.CountAsync()`, igual que `TierLimitChecker`). Nuevos webhooks: `customer.subscription.updated` (sync tier/status) → `customer.subscription.deleted` (Canceled + Free) → `invoice.payment_failed` (PastDue, sin cambio de tier). Nuevo método de dominio `UserSubscription.MarkPastDue(now)`. Nuevos endpoints: `POST /api/subscription/change-plan { targetTier }` + `POST /api/subscription/billing-portal`. Frontend: botones upgrade/downgrade + mensaje bloqueado + botón portal en `ManageSubscriptionComponent`. Stripe Customer Portal debe configurarse con plan-changes + cancellation **OFF**; invoices + payment method **ON**. Implementación en curso.
**Last updated:** 2026-06-11 — WI-STRIPE-FASE-2 COMPLETE. Stripe Checkout hosted + webhook confirmation + tier activation. Backend: `ProcessedStripeEvent` idempotency table (M2 — `A10_AddProcessedStripeEvents` migration); `IStripeCheckoutService` + `StripeCheckoutService` (creates Stripe Checkout Session with `Metadata["tenantId"]` server-side, SuccessUrl/CancelUrl from `FrontendBaseUrl` config); `IStripeWebhookService` + `StripeWebhookService` (`EventUtility.ConstructEvent` signature verification first, then idempotency check, then `EventTypes.CheckoutSessionCompleted` handler updates `UserSubscription` + `Tenant.SelectPlan` atomically, audit logged post-save); `CreateCheckoutSessionCommand` + handler; `POST /checkout` (Authorize) + `POST /webhook` (AllowAnonymous, raw body) in `SubscriptionController`. `WebhookSecret` + `FrontendBaseUrl` added to `StripeOptions` (gitignored dev config). 7 new unit tests (HMAC-SHA256 manual sig in v52). Frontend: `SubscriptionService.createCheckout(priceId)`. Wizard `selectPaid()` calls checkout API → redirects via `DOCUMENT`-injected `redirect()` method (testable). New `/onboarding/success` page (`SubscriptionSuccessComponent`): polls `GET /api/subscription/current` every 2s (max 15 attempts / 30s), sets `confirmed` signal on `Active+paid`, `timedOut` after max. Route added to `subscriptionRoutes` with `authGuard` only. i18n EN/ES/PL complete (7 new keys). Tests: 418 backend unit (408 pre-existing + 7 new webhook + fix of 3 pre-existing); 29/29 frontend subscription tests. Karma DISCONNECTED root causes fixed: (1) `window.location.href` in wizard test caused Chrome Headless to navigate away — extracted to protected `redirect()` method + spy in test; (2) success spec `setInterval`+1500ms `setTimeout` hung fakeAsync — used `StubComponent` for router routes + `flush()` for macrotasks. Both builds clean. User must: add real Stripe ProductIds to `appsettings.Development.json` `Stripe:ProductTierMap`; run `stripe listen --forward-to https://localhost:5001/api/subscription/webhook` and paste `whsec_...` into `Stripe:WebhookSecret`.
**Last updated:** 2026-06-11 — WI-STRIPE-TIER-MAP COMPLETE. `GET /api/subscription/plans` now returns all 3 paid plans from Stripe (Starter/Growth/Scale). Root cause: `StripeSubscriptionPlanService` mapped products by `product.Metadata["tier"]` but Stripe products have empty metadata `{}`. Fix: `StripeOptions.ProductTierMap: Dictionary<string,string>` (ProductId→TierName). Precedence: metadata["tier"] first → `ProductTierMap[productId]` → WARNING + discard. `ResolveTier()` extracted as `internal static` for testability. `InternalsVisibleTo("Wasnie.UnitTests")` added to Infrastructure. 6 new unit tests (6/6 pass). User must add 3 real ProductIds to `Stripe:ProductTierMap` in `appsettings.Development.json`.
**Last updated:** 2026-06-11 — WI-WIZARD-SUBSCRIPTION + UserSubscriptions COMPLETE. Table `UserSubscriptions` added (Domain entity `UserSubscription` with `SubscriptionStatus` enum; fields: TenantId FK unique, Tier, Status, BillingEmail, StripeSubscriptionId/CustomerId/PriceId/ProductId nullable, CurrentPeriodStart/End, NextBillingDate, CanceledAt, CreatedAt/UpdatedAt). Source of truth for subscription state; Stripe fields null for Free and populated by webhooks in Fase 3. `HasSelectedPlan` on Tenant stays as cached flag (set atomically when UserSubscription row is created). `SelectFreePlanHandler` updated: creates UserSubscription row (idempotent — skips if already exists) + calls `tenant.SelectPlan(Tier.Free)`. Migration `A9_AddUserSubscriptions` generated and applied to DB. `GET /api/subscription/current` endpoint + handler + `UserSubscriptionDto`. Frontend: `SubscriptionService.getCurrent()` added; `ManageSubscriptionComponent` page (`/subscription`): shows current tier, status badge, billing email; for Free plan shows "Free note" + 3 upgrade cards ("Coming soon", disabled). Sidebar nav item "Subscription" (credit-card icon, `Subscription.Manage` permission). Route `/subscription` in `app.routes.ts`. i18n EN/ES/PL complete (19 new keys each). Tests: 10 new backend integration tests (auth gates, creates row, sets flag, idempotent, get current, 404 before plan, multi-tenant isolation, guard scenario before/after); 566 total backend (561 pass, 3 pre-existing failures unchanged); 378 frontend (373 pass, 5 pre-existing). Both builds clean. Test isolation fix: `InitializeAsync`+`DisposeAsync` both reset Tier to Enterprise after each test (preventing leaked Tier=Free from polluting other test classes).
**Last updated:** 2026-06-11 — WI-WIZARD-SUBSCRIPTION COMPLETE. Mandatory post-sign-up subscription wizard. `HasSelectedPlan: bool` added to `Tenant` domain entity (defaults false; `Tenant.Create()` starts at `Tier.Free`). `Tenant.SelectPlan(Tier)` sets both `Tier` and `HasSelectedPlan` atomically. EF migration `A8_AddTenantHasSelectedPlan` generated. `SelectFreePlanCommand` + `SelectFreePlanHandler` (sets plan, logs `PLAN_SELECTED` audit). `POST /api/subscription/select-free` endpoint. `CurrentUserDto.HasSelectedPlan` returned from `GET /auth/me`. Frontend: `planGuard` (unauthenticated → /auth/login; no plan → /onboarding/plan; has plan → allow), `onboardingGuard` (has plan → /dashboard; no auth → /auth/login; no plan → allow). All app routes gated by `planGuard` (replaced `authGuard`). `/onboarding/plan` route with `SubscriptionWizardComponent`: 4-card grid (Free fully interactive; Starter/Growth/Scale "Coming soon"/disabled), loading skeleton, error state with retry, signal-based. Post-registration redirect updated to `/onboarding/plan`. i18n EN/ES/PL complete (12 new keys each). `SubscriptionService.selectFree()` API call. Tests: 17 new frontend unit tests (component + planGuard + onboardingGuard, all pass); 8 Stripe integration tests from WI-STRIPE-FASE-1 all pass. Build: `dotnet build` 0 errors, 0 warnings; `ng build --configuration production` clean. Frontend: 378 total (373 pass, 5 pre-existing `ProcessPendingComponent` failures). Backend: 556 total (551 pass, 3 pre-existing Dashboard/Assignments failures). noAuthGuard also added this session: authenticated users on /auth/login or /auth/register are redirected to /dashboard.
**Last updated:** 2026-06-11 — WI-STRIPE-FASE-1 COMPLETE. Stripe integration Phase 1: secure credentials setup + plans endpoint. `Stripe.net 52.0.0` added to Infrastructure. `StripeOptions` (SecretKey + PublishableKey) validated at startup via `IOptions<T>` pattern — keys live in gitignored `appsettings.Development.json` (dev) / Azure App Service settings (prod), never committed. `StripeSubscriptionPlanService`: fetches active recurring Stripe prices (with expanded `product`), maps `product.Metadata["tier"]` → `Tier` enum → `TierLimits.Limits[tier]`, prepends synthetic Free plan (no Stripe product). `GET /api/subscription/plans` returns `[Authorize]` list of 4 plans (Free hardcoded + Starter/Growth/Scale from Stripe) with `MaxPayees/MaxPlans` from domain. `GET /api/subscription/config` returns publishable key only — secret key NEVER in any response. `StripeUnavailableException` → middleware → 503. `Tenant.Create()` now starts at `Tier.Free` (was `Tier.Growth`). 8 new integration tests (auth, shape, secret-key-absent × 2, 503, publishable key). Build requires server restart to resolve obj file locks (confirmed no code errors from partial build with `--no-dependencies`).
**Last updated:** 2026-06-11 — WI-EXCEL-MONEY-FORMAT COMPLETE. Fixed scientific notation (`9.88407E+11`) on monetary columns in all four Excel export services. Root cause: ClosedXML writes numeric `decimal` values without a cell number format; Excel defaults to scientific notation for large values. Fix: `cell.Style.NumberFormat.Format = "#,##0.00"` applied to every monetary cell — `TotalCommissionAmount` in `PayoutExcelExportService`, `Amount` in `TransactionExcelExportService`, `OriginalAmount`/`CreditedAmount`/`SplitPercentage` in `CreditExcelExportService`, and dynamic `Amount_{CURRENCY}` columns in `PayRunExcelExportService`. `Wasnie.Infrastructure` build clean (0 errors, 0 warnings). Full solution build blocked only by API DLL file-lock (running server process 31068) — not a code error.
**Last updated:** 2026-06-11 — WI-TAB-SESSION-SYNC COMPLETE. Cross-tab session synchronisation via BroadcastChannel (same-origin scoped by spec; no auth tokens in messages). Three new signal types: `'activity'` (user active → reset idle timer in all tabs), `'logout'` (voluntary → silent redirect, no toast), `'session-expired'` (timer/401 → expiry toast + redirect). Token storage confirmed: `localStorage` key `wasnie_session` (shared across tabs — no sessionStorage concern). New `TabSyncService` (`src/app/core/services/tab-sync.service.ts`): thin BroadcastChannel wrapper with localStorage storage-event fallback; `NgZone.run()` ensures Angular CD fires on incoming messages; `broadcast()` never includes the token. `AuthService` changes: `clearSessionSilent()` new public method (clears state, no broadcast); `logout()` = `clearSessionSilent()` + broadcasts `'logout'`; `forceLogout()` no longer delegates to `logout()` — independently calls `clearSessionSilent()` + broadcasts `'session-expired'`; broadcast guarded by `wasAuthenticated` to prevent re-broadcast from already-cleared state. `InactivityService` changes: injects `TabSyncService`; `resetTimer()` calls `_broadcastActivity()` (throttled to 1 broadcast per 5 s to avoid channel flooding from mousemove); `start()` subscribes to `tabSync.messages$`; `stop()` unsubscribes; `_handleTabSync()` handles all three signal types; `_applyRemoteLogout()` calls `clearSessionSilent()` (not `logout()`) to prevent re-broadcast loop. 2 new spec files: `tab-sync.service.spec.ts` (11 tests: BroadcastChannel path + localStorage fallback path) + `cross-tab-session.spec.ts` (16 tests: sync reactions, timer reactions with fakeAsync+start-inside-it, throttle, AuthService broadcast). Build clean. Tests: 359 total (354 pass, 5 fail — all 5 pre-existing ProcessPendingComponent failures, unchanged).
**Last updated:** 2026-06-11 — WI-PAYOUTS-MENU-AND-STALE COMPLETE. Two fixes: (1) Stale data on /payouts re-navigation — root cause: `PayoutsStore` is `providedIn: 'root'` singleton; its constructor `effect()` only fires when a signal changes, so re-navigation with the same filter date silently shows stale data. Fix: explicit `void this.store.reload()` at the end of `ngOnInit()` in `PayoutsListComponent` — one fresh load per route activation, no reactive loop possible. 6 existing specs updated (`storeMock.reload.calls.reset()` after `detectChanges()` in poll-loop and bulk-paid `beforeEach` blocks) + 1 new test verifying reload called once on activation. Note: same stale pattern exists in `pay-runs` and `credits` list components (not fixed by this WI). (2) Sidebar expandable Pay Runs group — "Pay Runs" flat nav item converted to collapsible group with two sub-items: "Pay Runs" → `/pay-runs` (icon: coin) and "Payouts" → `/payouts` (icon: layers). `NavGroupEntry` interface + `NavEntry` union type added to `sidebar.component.ts`; `expandedGroups` signal + `constructor effect()` auto-expands on active route; `toggleGroup/isGroupExpanded/isGroupActive/isNavGroup` methods; template updated with `@if (isNavGroup(entry))` branching (collapsed sidebar: flat icon-only children; expanded: toggle button + indented sub-items with chevron); SCSS: `.sidebar__nav-group-toggle`, `.sidebar__nav-chevron`, `.sidebar__nav--sub`, `.sidebar__nav-link--sub`. i18n: `NAV.PAYOUTS` + `NAV.PAY_RUNS` already existed in EN/ES/PL — no i18n changes needed. Build: `ng build --configuration production` clean (0 new errors, 0 new warnings). Tests: 332 total (327 pass, 5 fail — all 5 pre-existing `ProcessPendingComponent` HTTP-mock teardown failures, unchanged from prior WIs).
**Last updated:** 2026-06-11 — WI-AUDIT-CANCELLED-EXCLUSION COMPLETE. Audited all 14 points where transactions are queried or aggregated. Found 1 bug: `GetDashboardSummaryHandler.BuildPeriodBandAsync` counted/summed ALL transactions (including Cancelled) in the period KPIs (`TransactionsCount` and `TransactionsVolumeByCurrency`). Fix: one-line `Status != Cancelled` filter on base query. All other calculation surfaces (CreditAllocationService, ProcessPendingJobHandler, GetPendingTransactionsCountHandler, GetEligiblePendingTransactionsHandler, CalculatePayoutsForPeriodHandler, QuotaAttainmentService) already excluded Cancelled either explicitly or by structural guarantee (Cancel() requires Pending; credits only created for Pending→Calculated; therefore no Cancelled tx has credits). Views/export retain Cancelled rows for audit visibility — correct. 1 new integration test: €1k valid + €9k Cancelled seeded → Count=1, Volume=€1k. Build clean: 404/404 unit; 1/1 new test passes.
**Last updated:** 2026-06-10 — WI-2 CHECKBOX "CALCULAR AL REGISTRAR" COMPLETE. `processImmediately` boolean flag (default=true) added to Record Transaction. Backend: `IngestTransactionCommand` record gets `bool ProcessImmediately = true` (backward-compatible default); `IngestTransactionHandler` wraps credit-allocation + `MarkCalculated` in `if (request.ProcessImmediately)` block. Frontend: native checkbox (design system has no WsCheckbox) styled with `accent-color: var(--color-brand)`, hint text, informative note about immutability post-calculation; `transaction-form.component.ts` adds `processImmediately: [true]` to FormGroup and passes it in `createTransaction`. EN/ES/PL i18n complete (3 new keys each). Tests: 5 new integration tests (all pass after fixing `CreatePayeeAsync` helper to include `hireDate` — was missing from helper, required by TenantA field-requirement settings). 2 new frontend unit tests (default-true, sends-false-when-unchecked). Total: 404/404 unit, 543/548 integration (3 pre-existing Dashboard/Assignments failures unchanged). Build clean.
**Last updated:** 2026-06-10 — WI-A7 VOID TRANSACTIONS COMPLETE. Anular (void) Pending transactions with reason + audit trail. Backend: `TransactionsVoid` permission added to TenantAdmin/CompManager roles; `CompensationTransaction.Cancel()` signature updated to include `reason` (min 3 chars), `cancelledBy`, `now`, `eventId` — only Pending status allowed; 3 nullable columns migrated (`CancelledAt/By/Reason`); `VoidTransactionCommand` + `VoidTransactionHandler` (auth gate `TransactionsVoid`, active-credits guard, domain cancel, save); `POST /api/transactions/{id}/void` endpoint (Conflict for domain blocks). Frontend: `Transaction` model extended with cancellation fields; `VoidTransactionRequest` interface; `void()` API method; `voidTransaction()` store action; new `app-void-transaction-modal` (mirrors ReassignPayeeModal: context block, reason textarea, danger button, min 3 chars); `canVoid()` predicate (Pending only); void button hidden via `hasPermission` pipe (`Transactions.Void`); cancelled reason shown in list row; EN/ES/PL i18n complete (10 new keys each). Also fixed pre-existing TransactionsListComponent test mock (missing `referenceNumbers`/`currencies` filter fields). Tests: backend 404/404 unit pass; frontend 324/329 (5 pre-existing ProcessPendingComponent failures unchanged, 10 new tests added: 6 void modal + 4 canVoid).
**Last updated:** 2026-06-10 — WI-DASHBOARD BUG: PENDING APPROVAL COUNT 15 VS 1 FIXED. Root cause: `BuildActionBandAsync` counted ALL `Status==Calculated` payouts including $0-amount ones (14 × €0 + 1 × €422.58 = count=15, amount=€422.58). The payouts list defaults to `hideZero=true` → `ExcludeZero=true`, so it shows only 1. Bug was NOT a multi-tenant isolation issue — `CompensationPayout` has `HasQueryFilter(e => e.TenantId == CurrentTenantId)` confirmed. Fix: added `&& p.TotalCommission.Amount > 0` to both action band payout queries (Calculated + Approved) so dashboard count matches what the user sees when clicking through to the list. 2 new backend integration tests: (1) count excludes $0 payouts — seeds 2 payees, 1 with €500 payout and 1 with $0, asserts count=1 and amount=€500; (2) action band count is tenant-scoped. Note: 2 pre-existing PendingByPlanItems integration tests fail (unrelated to this WI, not regressed by this fix). 402/402 unit tests pass.
**Last updated:** 2026-06-10 — WI-DASHBOARD RELATIVE TIME TIMEZONE BUG FIXED. Root cause: C# `DateTime` serialized without `Z` suffix (e.g. `"2026-06-10T14:50:00"`) — JavaScript's `new Date()` treats such strings as local time, not UTC. For a user in CEST (UTC+2) an event at 14:50 UTC was read as 12:50 UTC, showing "2h" instead of "0m". Fix: one-line regex guard in `relativeTime()` appends `Z` if no timezone marker present. 1 new spec test (no-Z ISO string). 53/53 dashboard tests pass.
**Last updated:** 2026-06-10 — WI-DASHBOARD PAYOUT CARD LINKS FIXED. Root cause: `payouts.routes.ts` redirigía `path:''` a `/pay-runs` en vez de cargar `PayoutsListComponent` — los query params (`status=Calculated/Approved`, `pFrom`/`pTo`) se perdían en la redirección. Fix: restaurada la ruta `path:''` → `PayoutsListComponent` (el componente ya tenía `loadFromQueryParams` leyendo `status` y `pFrom`/`pTo`). Los links del dashboard ya eran correctos (`/payouts?status=Calculated`, `/payouts?status=Approved`, `/payouts?period=...&pFrom=...&pTo=...`); el único cambio fue la ruta. 3 tests de regresión de template añadidos (verifican href de las 3 cards payout). Build limpio; 52/52 dashboard tests pasan.
**Last updated:** 2026-06-10 — WI-DASHBOARD PENDING-BY-PLAN CARD COMPLETE. New action-band card "Pending to Process by Plan" shows a scrollable list of plans that have Pending transactions eligible for ProcessPending (ByPlan scope). Step 0 audit: eligibility = `Status==Pending + payee has Active assignment with EffectivePeriod containing TransactionDate + currency matches plan`. Anti-Cartesian via 3 separate queries + HashSet<Guid> deduplication per plan in-memory. Plan detail route = `/plans/:planId` — added `?tab=assignments` reading in plan-detail.component.ts ngOnInit (calls `setTab()`). Data added to `DashboardActionBandDto.PendingByPlanItems`. Frontend: scrollable `ws-scroll-thin` list, each row = plan name + currency + WsBadge count, opens `/plans/:planId?tab=assignments` in new tab; `pendingByPlanTotalCount()` for card badge; `pendingByPlanItems.length > 0` wired into `hasPendingActions`; `pendingActionCount()` includes total pending tx count. 3 backend integration tests (empty=empty, tenant isolation, anti-Cartesian count). 5 new frontend spec tests (pendingByPlanTotalCount + updated pendingActionCount). EN/ES/PL i18n complete. `anyComponentStyle` budget bumped 12→14kB (dashboard.component.scss was already at limit pre-WI). Build clean; 49/49 dashboard tests pass; 305 total frontend pass (10 pre-existing TransactionsListComponent failures unchanged).
**Last updated:** 2026-06-10 — WI-DASHBOARD LINK AUDIT COMPLETE. Audited all dashboard card links; found and fixed 7 bugs: (1) Draft Pay Runs → /pay-runs?status=Draft was silently ignored (PayRunsListComponent never read URL params); (2–4) Period band cards (Payouts, Transactions, Credits) linked without period params — destination defaulted to this-month regardless of dashboard period, causing amount mismatches (e.g. €7,900,022.40 vs €7,901,357.69); (5–7) Active Plans/Payees/Quotas cards linked without status filter — destinations showed all records, not just Active ones. Fixes: dashboard.component.ts now computes period-aware queryParams (payoutsLinkParams/transactionsLinkParams/creditsLinkParams computed signals, _periodDates helper mirrors PeriodHelper.ComputeDateRange on backend); HTML updated with [queryParams] on all 6 period cards; pay-runs-list.component.ts, plans-list.component.ts, payees-list.component.ts, quotas-list.component.ts each gained ActivatedRoute injection + URL param reading in ngOnInit. Action band cards (Pending Approval / Approved Not Paid) confirmed correct. 12 new dashboard tests (period link params suite). Build clean; 312 total tests (302 ✅, 10 ❌ pre-existing ProcessPending failures unrelated to this WI).
**Last updated:** 2026-06-10 — WI-DASHBOARD VISUAL REDESIGN + i18n FIX COMPLETE. Root cause of raw i18n keys on screen (DASHBOARD.TITLE etc.) fixed: duplicate `"DASHBOARD"` JSON objects in EN/ES/PL files — second block overwrote first; merged into single block with all 23 admin keys + preserved payee-dashboard keys. Trend edge case fixed: `trendIsNoBase()` guard (threshold >500%) prevents absurd percentages (e.g. +38,043%) when prior amount near zero. UI rebuilt: 3-band layout (action band with count badges + warning accents, period band with financial-grid + stats-row + gauge, trend band per-currency with ws-bar-chart), activity feed with text helpers (actorShortName strips @domain, formatActivityAction snake_case→words, shortResource truncates at 28 chars + CSS text-overflow ellipsis + min-width:0 flex fix). Stats row uses custom ws-card > stat-card pattern (font-size-32/weight-800) for visual parity with action cards. Zero hard-coded values; all design-system tokens. `ng build --configuration production` clean; 34/34 frontend tests pass.
**Last updated:** 2026-06-10 — WI-DASHBOARD COMPLETE. Admin Dashboard `/dashboard` fully rebuilt from hardcoded mockup to real backend KPIs. Single `GET /api/dashboard?period=` endpoint; 3 bandas: Banda 1 action (draft pay runs, payouts pending approval, approved-unpaid), Banda 2 period metrics (transactions, payouts, credits, avg quota attainment, active plans/quotas/payees), Banda 3 trend (per-currency current vs prior period change%); activity feed from real AuditLog. Anti-Cartesian attainment (mandatory test: 2 quotas 50%+100%=75%); Pattern B per-currency throughout; multi-tenant isolation; `Permission.ReportsViewAll` auth gate. Frontend: signals store + period selector (`WsSegmentedControl`); EN/ES/PL i18n replaced; 15/15 frontend tests pass; `ng build --configuration production` clean. Decision: Plans pending approval KPI removed (Plan.Status has no PendingApproval value).
**Last updated:** 2026-06-10 — A.6 Pay Run COMPLETE — all 6 phases done and browser smoke-validated. Fase 5 UI: `/pay-runs` (PayRunListComponent) + `/pay-runs/:id` (PayRunDetailComponent); sidebar `/payouts`→`/pay-runs` redirect; `PayRunsStore` (global, `_lastLoadedFilter` race guard) + `PayRunDetailStore` (component-scoped, `providers: []`); calculate SYNC (no polling loop); action modals — Approve/Reopen (reversible, no irreversibility warning per Decision #69), MarkPaid (irreversible: 5 mandatory elements — count, per-currency totals, payee list, irreversibility warning, skip warning); Pattern B per-currency roll-ups in run header; `payeeCount` + `paidPayeeCount` as distinct counters; audit fields (created/approved/paid + actor). Fase 6: list manual date range pickers (from/to, `_lastLoadedFilter` race guard, segment resets to "All time" on manual date) + Export to Excel (`Payouts.Export` gate, ClosedXML, two-pass dynamic currency columns); detail collapsible filter bar (status, period from/to, amountMin/max, payee+plan chips via `WsSelect searchFn`, hide-$0 toggle) + Export to Excel. Fix: list date pickers default to current month (first→last day) instead of last month. Backend: `PayoutFilterQuery` extended (`PayRunId`, `AmountMin`, `AmountMax`); `ListPayoutsHandler.BuildQuery()` `static internal` (shared by list/detail/export — zero duplicated filter logic); `ExportPayRunsHandler` + `PayRunExcelExportService`; `ExportPayoutsHandler` gains `PayRunId`. Tests: +43 frontend unit (pay-runs feature, 43/43 pass); +11 backend integration (`PayRunExportTests.cs`); `ng build` production clean. Smoke: calculate→Draft→Approve→Approved→Reopen→Draft→Approve→MarkPaid→Paid verified; Pattern B (€ + PLN separate lines); filter+export aligned (Agnieszka filter → 2 exported rows); confirmed no infinite loops.
**Last updated:** 2026-06-09 — A.6 Fase 4 done. PayRunEngineTests: 20 new integration tests covering idempotency, state machine (valid+invalid), roll-ups (anti-Cartesian, zero-payout), multi-tenant isolation, and permission gates for all 5 engine operations. Total: 387 unit + 134 integration = 521 tests (518 pass; 1 pre-existing Assignments failure; 2 skip). Build clean. Pending: Fase 5 UI (PayRunListComponent, PayRunDetailComponent, sidebar update).
**Last updated:** 2026-06-09 (continuation) — Calculate Payouts result modal: warnings and conflicts lists now scrollable (applied existing `payouts-list__payee-scroll` pattern; title/desc/button always visible). Credits filter bar: Status `<ws-select>` alignment fixed — external `<label>` removed, `[label]` prop added directly (matches ws-input internal rendering). Visual-only fixes; 31/31 frontend tests pass; `tsc --noEmit` 0 errors. Known pending bugs: (a) "This month" filter boundary shows 1st→today instead of 1st→last-day-of-month; (b) Excel decimal precision uses raw DB 4-decimal values instead of 2-decimal display values; (c) `_lastLoadedFilter` race latent in `CreditsStore` and `TransactionsStore` if they share the same singleton + reconstructed-filter pattern (audit before A.6); (d) legacy $0/USD payouts in dev tenant need period recalculation; (e) ~11 pre-existing red tests (10 frontend: ProcessPendingComponent×5, TransactionsListComponent×5; 1 backend: AssignmentsEndpointsTests). Docs index: `docs/Pay_Run_Model.md` added (Pay Run model, 6 decisions, APPROVED 2026-06-09).
**Last updated:** 2026-06-09 — A.5.6 Excel export race-condition fixed. Root cause: `PayoutsStore` is a singleton; on navigating back to the payouts page `ngOnInit` calls `setFilter({ periodFrom: ... })` synchronously (filter signal updated) before the Angular effect re-runs `_loadList` (pagedResult still shows old data). If the user clicked Export in that window, `toExportParams()` read the new (stale-for-display) filter → backend returned 0 rows → xlsx headers only. Fix: `_lastLoadedFilter` signal set on every successful `_loadList` completion; `toExportParams()` reads from `_lastLoadedFilter() ?? filter()`. Export button also disabled while `store.loading()` (belt-and-suspenders). `pageSize:1` noise removed from export params. Unit tests: 2 new race-condition specs in `payouts.store.spec.ts`. Integration test: `ExportPayouts_RowCountMatchesListTotalCount_ForSameFilter` parses the xlsx with ClosedXML and asserts `ws.RowsUsed().Count() - 1 == list.TotalCount` (replaces the misleading byte-size proxy). 31/31 frontend unit tests pass. Backend builds clean. Next: WI-CALC-A.6 (Pay Run implementation). Known pending: ~11 pre-existing red tests (10 frontend: ProcessPendingComponent×5, TransactionsListComponent×5; 1 backend: AssignmentsEndpointsTests); "This month" filter boundary shows 1st→today instead of 1st→last-day-of-month; legacy $0/USD payouts in dev tenant need recalculation (fix only affects future payouts).
**Last updated (A5.5):** 2026-06-09 — WI-A5.5-BULK-MODAL-PERIOD-AMOUNT Done. Bulk confirmation modals (approve + mark-paid) now show period + amount per payee row, disambiguating payees who appear in multiple periods. Each payee row is a 3-column grid: name link | Jan 1, 2026 – Mar 31, 2026 | €15,934.60. `CurrencyFormatPipe` global fix: removed `minimumFractionDigits: 0` override — now EUR/USD/PLN show 2 decimals and JPY shows 0, matching each currency's CLDR standard. Store `bulkApproveSummary.payees[]` and `bulkMarkPaidSummary.payees[]` extended with `periodStart`, `periodEnd`, `amount`, `currency` (all from in-memory items, no extra fetch). SCSS `__payee-scroll-entry` changed from flex to grid (`minmax(0,2fr) minmax(0,2fr) minmax(0,1fr)`) with mobile breakpoint. 12/12 CurrencyFormatPipe tests pass (3 new: trailing-zero, always-2-decimal, JPY 0-decimal). 8/8 payouts component + store tests pass. Build clean, TypeScript 0 errors.
**Last updated (A5.3/A5.4):** 2026-06-09 — WI-A5.3-BULK-MARK-PAID Done. `POST /api/payouts/bulk-mark-paid` endpoint + `BulkMarkPaidHandler` (mirrors BulkApprovePayoutsHandler; uses IClock; catches DomainException per item). Frontend: `selectedApprovedIds` + `bulkMarkPaidSummary` computeds in PayoutsStore (totals by currency, payee names, skipped count — all from in-memory items, no extra fetch). Rich WsModal with 5 mandatory elements: (1) explicit count in title + body, (2) totals grouped by currency, (3) payee list, (4) irreversibility warning, (5) skip warning when non-Approved payouts are selected. Button hidden via `*hasPermission="Payouts.MarkPaid"`. Reload uses `store.reload()` direct — no polling loop. EN/ES/PL i18n complete. 3 backend integration tests (happy path, mixed statuses, 401). 3 frontend component tests (success, no-op on empty, no reload on error). 6/6 tests pass. TypeScript 0 errors. Application + Infrastructure compile clean. Also fixes from WI-A4/A5 session: View Statement opens new tab; Plan name shown on detail page as link (new tab); poll-loop bug fixed; PDF actor GUID resolved to email.\n**Last updated (previous):** 2026-06-08 — WI-A5-PAYOUTS-UI Design fixes: icon corrections (money→receipt, play→zap), token fixes (--spacing-X→--space-X, font/weight/color literals), filter chip pattern (brand pills inside each filter field), viewChild+effect mutual exclusion for date pickers, raised-surface modal form wrapper for input visibility in dark mode. Build clean. WI-CALC-A.3-FIX-4 Done. Quota attainment semantic changed from Earnings Quota (SUM CreditedAmount = commission) to Sales Quota (SUM Transaction.Amount = gross sales). Industry standard: comp managers think "Anna should sell €25k" not "Anna should earn €1,250 commission". 3 backend paths updated: QuotaAttainmentService.ComputeRevenueAchievedAsync, GetPayeeAttainmentHandler Revenue branch, GetPayeeDashboardHandler attainment gauges. Sales Trend chart (renamed from Earnings Trend) also updated to SUM Transaction.Amount per month. DTO renamed EarningsTrend→SalesTrend, frontend model EarningsTrendPoint→SalesTrendPoint, earningsTrend→salesTrend throughout. i18n EN/ES/PL updated. 4 QuotaAttainmentService tests updated to expect transaction sums (e.g. €37,714 not €1,886 for two credits). Sales Trend integration test flipped: expects €1,000 (Transaction.Amount) not €50 (CreditedAmount). Test counts: 10/10 QuotaAttainmentService pass, 13/13 PayeeDashboard pass. New forbidden-pattern rule: Revenue attainment MUST use Transaction.Amount; to add Earnings Quota basis, add explicit AttainmentBasis field on Quota. Aggregation audit checklist updated. Both builds clean (pre-existing warnings unchanged). Decision: if a future customer demands Earnings Quota, add `AttainmentBasis enum { SalesRevenue, EarnedCommission }` to Quota — never silently change meaning. WI-PROD-MEASURETYPE-FILTER-RULES Done. Create Rule / Edit Rule `measurementTypeOptions` filtered to Revenue + Units only (was full 5-value enum). Same pattern as Create Quota (`quota-create.component.ts`). 2 new component tests (13/13 pass). New forbidden-pattern rule in `14-forbidden-patterns.md`: every MeasurementType picker surface must filter to Revenue + Units with the surfaces list kept current. Audit: payee-detail and quota-detail are read-only displays (no pickers) — no other broken surface found. Backend permissive by design (enum values intact). Build clean (pre-existing warnings unchanged). WI-PROD-PAYEE-DASHBOARD-V3-FIX-4 Done. (1) Gauge orphan dot removed — `stroke-linecap="butt"` on fill path. (2) Bar tooltip right-anchored near right-edge bars; Y tracks bar top; CSS arrow connector; dashed column indicator in SVG. (3) Past bar opacity 0.35→0.22; brand-colored underline accent below current month label. 12 bar-chart specs. Both builds clean. WI-PROD-PAYEE-DASHBOARD-V3-FIX-3 Done. Earnings Trend card replaced with new `ws-bar-chart` SVG component (bars per month, current month highlighted in brand color, 35% opacity past months, tooltip preserved). Zero deps, CSS-variable themed. 10 new bar chart unit tests. Both builds clean. WI-PROD-PAYEE-DASHBOARD-V3-FIX-2 Done. Earnings Trend chart was using `OriginalAmount` (raw revenue) instead of `CreditedAmount` (earned commission), inflating by 20× for a 5% plan. 1-line fix. Full aggregation audit done — all other endpoints correct. New `docs/architecture/15-aggregation-audit-checklist.md` created. 350 unit + 455 integration = 807 tests. Both builds clean. WI-PROD-PAYEE-DASHBOARD-V3-FIX-1 Done. Root cause: `buildHttpParams` silently dropped `period` from paginated calls (extra property not in `PaginationParams` interface). Fix: added `period?: string` to `PaginationParams`, handled in `buildHttpParams`, removed `as any` casts. Quotas card and Assignments card now correctly use period-intersection filter. 7 new buildHttpParams unit tests. Pre-existing test fixture quantity fixes (5 mock objects). WI-CALC-A.3-FIX-3 Done. `IsCurrencyValid` flag added to `QuotaAttainmentDto` and `QuotaSummaryDto`. Dashboard and list handlers compute it from plan currency vs quota currency. UI: amber banner when mismatched quotas exist; warning chip with tooltip replacing temporal chip on invalid rows; faded gauge. Click-through to /quotas/:id. 350 unit + 455 integration = 807 tests. Both builds clean. WI-PROD-PAYEE-DASHBOARD-V3 Done. Active/All toggle replaced with 4-option Period selector (This Month / Last Month / Year to Date / All Time). All 5 bento cards now apply the same date range via `PeriodHelper.ComputeDateRange` — single source of temporal truth. Temporal chips (In Progress=green, Upcoming=blue, Closed=gray) replace binary Active/Closed chips in Quotas and Assignments cards. Contextual counters in card titles. Earnings Trend chart highlights the selected range. Empty states are period-aware. URL sync via `?period=this-month|last-month|ytd|all-time`. 342 unit tests (333+9 new PeriodHelper) + 455 integration + 2 skip = 799. Both builds clean. New forbidden-pattern: time-scoped UI controls must apply consistently to all cards in the same view. WI-CALC-A.3-FIX-2 Done. CRITICAL BUG FIXED: Revenue quota attainment was inflating 20× by summing OriginalAmount (revenue) instead of CreditedAmount (commission). Root cause: field mismatch, NOT Cartesian product. All 3 attainment paths fixed (QuotaAttainmentService + GetPayeeAttainmentHandler + GetPayeeDashboardHandler); CreditedCurrency == quota.Amount.Currency filter added. Prior test asserting 0.7543 corrected to 0.0377. 3 new regression-guard tests. 788 tests (333+455+2skip). Build clean. Bug caught by smoke: Agnieszka EMP301 Jun-Jul quota showed €6,855.22 instead of €342.76. WI-PROD-PAYEE-DASHBOARD-V2 Done. Tabs: Overview (default)|Profile|Activity. Assignments/Quotas/Attainment tabs removed. Virtual scroll (IntersectionObserver sentinel, zero deps). Pacing line in ws-gauge. Period filter Active/All. 5 bento cards with click-through navigation. 785 tests (333+452). Both builds clean. WI-PROD-PAYEE-DASHBOARD (V1) Done (earlier same session). Attainment tab transformed to 2×2 bento dashboard: SVG half-circle gauges (ws-gauge), SVG line chart (ws-line-chart), compact quotas list, compact assignments list. Composed `GET /api/payees/:id/dashboard` endpoint. Zero new dependencies. 784 tests (333 unit + 451 integration). Both builds clean. WI-CALC-A.3-FIX-1 Done (earlier same session). Quota-Plan currency invariant enforced: domain throws on mismatch, CreateQuotaHandler/UpdateQuotaHandler load plan and pass planCurrency, UI auto-populates currency from selected plan (disabled field). 333 unit + 448 integration = 781 tests. Both builds clean. WI-CALC-A.3 Done (earlier same session). `IQuotaAttainmentService` (scoped per-request cache, Revenue/Units), `AttainmentPercentage` VO (0–∞), `CreditAllocationService` wired to real attainment (short-circuit for Flat/Tiered), `GET /api/payees/:id/attainment` endpoint, Attainment tab on /payees/:id (quota cards + color-coded progress bars). Revenue sums `Credit.OriginalAmount`; Units sums `CompensationTransaction.Quantity`. Test pattern restored: 328 unit + 446 integration = 774 total (+7 net). Both builds clean. WI-PROD-QUANTITY-FIELD Done (earlier same day). `Quantity int NOT NULL DEFAULT 1` added to CompensationTransactions across all 17 backend + 10 frontend surfaces. Migration applied (all existing rows → 1). MeasurementType dropdown in Create Quota filtered to Revenue + Units only (V1 scope). Forbidden-patterns checklist rule added. Build clean. WI-PROD-CREDITS-EXPORT Done (earlier same day). GET /api/credits/export with shared predicate (ListCreditsHandler.BuildQuery), 50k cap, 17-column xlsx via ClosedXML. Export button on /credits view-toggle row (right-aligned); Transactions export button moved from above-filter to count-row above table. Both pages now symmetrical. Build clean. WI-PROD-FILTERS-CURRENCY-RULE-FIX-1 Done (earlier same day). Currency and Rule filters converted from inline toggle-chip rows to ws-select dropdown + removable chip pattern (matching Payee/Plan). Layout restored. Build clean. WI-PROD-FILTERS-CURRENCY-RULE Done (earlier same day). Currency multi-select filter (17 ISO chip-buttons, URL sync) added to /credits and /transactions filter panels. Rule multi-select (plan-linked chip picker) added to /credits — appears when ≥1 Plan is selected, loads active rules via PlansApiService, filters credits by c.RuleId. All 3 filters wired through FIX-11 8-location checklist; counter cards + By-Payee respect them automatically. No migrations. Build clean. WI-CALC-MULTIPLAN-CURRENCY-MATCH Done (also 2026-06-04): Pattern B adopted (Decision #65): plan selection by currency match. New `PlanAssignmentResolver.Resolve()` is single source of truth for which plan applies. All 4 surfaces updated: CreditAllocationService (both overloads), ProcessPendingJobHandler (LoadByPlan + LoadByAssignment), badge/eligible predicates (CountByPlan + CountByAssignment), import validator (Error → Warning). Currency mismatch no longer an error — it's a routing signal. Smoke bug fixed: PLN transactions for EMP301 now route to PLN plan, not EUR plan. Build clean. Three polish fixes on /credits: (1) counter cards → filter gap unified to --space-3; (2) Table button icon changed from unknown `table` to registered `list`; (3) toggle converted from custom pill CSS to `ws-button` with variant switching (primary=active / secondary=inactive). Build clean. New `/credits` page: list with 8 filters (payee, plan, status, date, amount, currency, reference), counter cards, By-Payee aggregate view. New `/credits/:id` detail page with 5 sections: Summary, Source Transaction, Plan & Rule, "How it was calculated" (Flat rate step-by-step trace + raw snapshot toggle), Superseded banner. New `Credits.Read` permission + nav entry. Backend: 3 endpoints (list, counters, by-payee, detail). Exposes 363 active + 1 superseded credits (54,589.15 EUR). Build clean. "Open in filter" on eligible table now uses `refs=ref1,ref2,...` (exact reference numbers, same as skip log pattern) instead of payeeIds — filter result matches badge count exactly. Removed obsolete `filterPayeeId` input. Truncation note shown when > 100 refs. Build clean. Eligible Pending transactions now visible before processing: new `GET /api/transactions/eligible-pending` endpoint (same predicates as badge, max 200 rows, full context), `ProcessPendingComponent` renders inline table (skip-log visual style) with show/hide toggle + "Open in transaction filter" button. Applies to all 3 surfaces (Plan detail, Assignment detail, Transactions list). New forbidden-pattern: count-only display before financial action is forbidden. Build clean. Critical filter bug fixed: `payeeIds` was missing from `_buildFilterRecord` in `TransactionsStore`, causing the transactions list API call to silently ignore the payee filter (returning any payee's transactions instead of the selected payee's). Fix: one-line addition of `payeeIds` to `_buildFilterRecord`. New forbidden-pattern rule added to `14-forbidden-patterns.md` documenting the 8-location checklist for new filter fields. Build clean (frontend + backend).
**Updated by:** Rodolfo Calvo (A.6 Pay Run COMPLETE — Phases 5+6 + browser smoke, 2026-06-10)
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
✅ B3 — Prioritized backlog (Audit_Backlog.md, done 2026-05-27)

✅ PHASE C — Critical Quality Gaps — OFFICIALLY CLOSED (2026-05-27)
✅ C1 — Server-side pagination (done in Phase A via prompts 39-43)
✅ Wave 1 (Security Hardening):
     WI-01 ✅ JWT lifetimes tightened
     WI-02 ⏭️ Email verification (deferred to Phase 5-6, scope documented)
     WI-03 ✅ Logout token invalidation + Refresh validator
✅ Wave 2 (Multi-tenant):
     WI-04 ✅ Tenant-scoped import cache
     WI-05 ✅ Multi-tenant defense hardening (global filters, IgnoreQueryFilters guard, TenantContext enforcement)
⏭️ Wave 3: WI-06 Clean Architecture fixes (F-001, F-002) — DEFERRED (architectural pragma; see decisions)
✅ Wave 4: WI-07 IClock + IGuidGenerator (F-003, F-004)
✅ Wave 5: WI-08 Audit trail foundation (F-014)
✅ Wave 6a: WI-09a RBAC + tier limits — backend (F-009)
✅ Wave 6b: WI-09b Frontend RBAC integration
✅ Wave 7: WI-10 Validators + cross-tenant test coverage (F-016, F-017, F-023)
✅ Wave 8: WI-11 Security middleware: headers + rate limiting + password policy (F-010, F-011, F-013, F-025)
✅ Wave 9: WI-12 Observability foundation (F-027)
✅ Wave 10: WI-13 Cleanup of low findings (F-020, F-021, F-024)

PHASE D — Phase 1 officially closed (~1 week)
⏭️ D1 — Backend coverage > 80%
⏭️ D2 — Frontend coverage > 60%
⏭️ D3 — E2E happy path tests (includes deferred A4)
⏭️ D4 — Master Spec v2.1 + docs update

PHASE 2+ — Transactions, Calculation Engine, Visibility, etc.
⏭️ Available to start (Phase D is optional polish, not a hard blocker)
```

For full plan details: `docs/Wasnie_Master_Plan_Phase_1_Closure.md`

---

## Feature Inventory (real state from code, 2026-06-13)

Estado honesto de cada módulo funcional basado en inspección del código (no en la spec de mayo). Leyenda: 🟢 construido y funcional / 🟡 parcial o con deuda crítica / 🔴 no empezado.

### Módulos de compensación (core)

| Módulo | Estado | Notas |
|---|---|---|
| **Payees (Sales Reps)** | 🟢 | CRUD, deactivate/activate, Excel import con tier limits, jerarquía manager, payee dashboard completo |
| **Compensation Plans** | 🟢 | Create/edit/activate/archive/clone; reglas Flat-rate y Tiered; multi-currency; endpoint list + detail + rules |
| **Plan Rules** | 🟢 | Add/remove/update reglas por plan; MeasurementType Revenue+Units; cap/floor en tiered |
| **Quotas** | 🟢 | Create/edit/activate/close; attainment Sales Revenue (Transaction.Amount); currency invariant enforced |
| **Assignments** | 🟢 | Assign payee→plan con effective period; deactivate; notes; list by plan/payee |
| **Transactions** | 🟢 | Ingest manual + import Excel (hasta 10k filas); reassign payee; void (Pending only); update-from-Excel; cancel + audit; multi-currency |
| **Credits** | 🟢 | Trail completo de cómo se calculó cada comisión; list + counters + by-payee + detail; export Excel |
| **Payout Engine** | 🟡 | Functionally complete + smoke-tested. **Deuda crítica: `CalculatePayoutsForPeriodHandler` sin unit tests de money math** — viola Critical Twelve #2. Prioridad alta antes de primer cliente real. |
| **Pay Runs** | 🟢 | Package payouts en ciclos; Draft→Approved→Paid state machine; Reopen; roll-ups per-currency; export Excel |
| **Payouts** | 🟢 | Calculate, approve (individual + bulk), mark paid (individual + bulk), export Excel; ExcludeZero toggle |
| **Process Pending** | 🟢 | Hangfire job; 3 scopes (ByPlanAssignment/ByPlan/ByPayeeAndPeriod); skip log; cancel support |

### Visibilidad y análisis

| Módulo | Estado | Notas |
|---|---|---|
| **Admin Dashboard** | 🟢 | 3 bandas: acción (pay runs/payouts pendientes), período (transacciones/payouts/créditos/quotas/KPIs), tendencia (per-currency); activity feed; period selector |
| **Payee Dashboard** | 🟢 | Vista individual: 5 bento cards (quota gauge SVG, sales trend bar chart, attainment, assignments, credits); period filter; click-through navigation |
| **Credits Detail** | 🟢 | "Cómo se calculó" step-by-step (Flat + trace de snapshot); banner Superseded |

### Infraestructura y plataforma

| Módulo | Estado | Notas |
|---|---|---|
| **Multi-tenant** | 🟢 | Global EF query filters en 11 entidades; BackgroundJobTenantContext; cross-tenant tests en todos los endpoints |
| **Auth + RBAC** | 🟢 | JWT + refresh + revocación; 4 roles (TenantAdmin/CompManager/Payee/Auditor); 29+ permisos; rate limiting; password policy; lockout |
| **Session Management** | 🟢 | Inactivity timeout + warning modal; cross-tab sync (BroadcastChannel + localStorage fallback); session expiry toast |
| **Audit Trail** | 🟢 | Inmutable (SQL trigger + EF), 7-year retention ready; audit en todas las operaciones destructivas y monetarias |
| **Billing / Stripe** | 🟢 | Planes Free/Starter/Growth/Scale; Checkout hosted; webhooks (checkout/subscription/invoice); upgrade/downgrade (pago inmediato/proration); PastDue→grace; Canceled→reactivación con checkout; Billing Portal; idempotencia eventos |
| **Tier Limits** | 🟢 | Enforced en create payee, create plan, import payees, checkout reactivación; UI modal con usage bar |
| **i18n EN/ES/PL** | 🟢 | Completo en todos los módulos; sin fallbacks en ES/PL |
| **Observability** | 🟢 | Structured JSON logging (Serilog); CorrelationId middleware; TenantId/UserId enrichers; frontend ErrorTrackingService + GlobalErrorHandler |
| **Security headers** | 🟢 | SecurityHeadersMiddleware; HSTS; CSP; CORS no wildcard |

### Import / Export

| Módulo | Estado | Notas |
|---|---|---|
| **Payee Import (Excel)** | 🟢 | 5-step wizard; column mapping; validación + preview; tier limit pre-flight; hasta 300 filas |
| **Transaction Import (Excel)** | 🟢 | 5-step wizard; hasta 10k filas (configurable); InvariantCulture date parsing |
| **Transaction Update (Excel)** | 🟢 | Re-upload con ReferenceNumber como key; diff preview; audit por fila |
| **Payouts Export (Excel)** | 🟢 | Filtros + 50k cap; ClosedXML; columnas de moneda dinámicas |
| **Credits Export (Excel)** | 🟢 | Filtros + 50k cap; 17 columnas |
| **Transactions Export (Excel)** | 🟢 | Filtros + 50k cap; 10 columnas; botón en lista |
| **Pay Runs Export (Excel)** | 🟢 | Por pay run; per-currency dynamic columns |

### Módulos parciales o no empezados

| Módulo | Estado | Notas |
|---|---|---|
| **Admin/Settings** | 🟡 | Appearance (dark/light/soft) + field requirements por payee. No hay user management UI ni role assignment UI |
| **Email Notifications** | 🔴 | `IEmailService` abstraction existe y está registrada como stub. Cero envíos reales. Deferred a primer cliente |
| **Clawbacks/Adjustments** | 🔴 | Explícitamente diferido (Decision #66). No empezado |
| **Rep Portal (acceso de vendedores)** | ⛔ FUERA DE ALCANCE | **Decisión de producto 2026-06-13:** los vendedores NO acceden a Wasnie. Wasnie es una herramienta para administradores de compensación. El Rep Portal descrito en la Product Master Spec (mayo) queda abandonado como dirección de producto. No reintroducir. |
| **Manager/Rep scoped data access** | 🔴 | RBAC roles existen; sin UI separada para manager/rep. Diferido — requiere Rep Portal que está fuera de alcance |
| **E2E tests** | 🔴 | Phase D, deferred |
| **Mobile responsive** | 🔴 | Phase 8, deferred. Target mínimo: 1280px |

### Deuda latente documentada (lleva seguimiento)

1. **`CalculatePayoutsForPeriodHandler` sin unit tests de money math** — Viola Critical Twelve #2. El handler funciona (smoke + integración), pero sin cobertura unitaria de los cálculos monetarios. Antes del primer cliente real esto es bloqueante.
2. **`_lastLoadedFilter` race latente** en `CreditsStore` + `TransactionsStore` — auditar antes del primer cliente.
3. **Excel export usa 4 decimales** de BD en lugar de redondear a precisión de display — cosmético.
4. **~11 pre-existing red tests** — 10 frontend (`ProcessPendingComponent×5`, `TransactionsListComponent×5`); 1 backend (`AssignmentsEndpointsTests.ListAssignmentsByPayee`).
5. **F-001/F-002** — Clean Architecture violations documentadas, diferidas con pragma.

---

## Audit findings summary (B2 results — Phase C closure state, 2026-05-27)

27 total findings. **23 closed as of Phase C closure.**

| Severity | Count | Open | Closed |
|---|---|---|---|
| 🔴 Critical | 8 | 1 | 7 (F-003, F-004, F-005, F-006, F-007, F-009, F-008 via deferral) |
| 🟠 High | 7 | 2 | 5 (F-010, F-011, F-012, F-014, F-015) |
| 🟡 Medium | 8 | 1 | 7 (F-013, F-015, F-016, F-017, F-018, F-019, F-023, F-025) |
| 🟢 Low | 4 | 0 | 4 (F-020, F-021, F-022, F-024) |

**23 closed:** F-003, F-004, F-005, F-006, F-007, F-008 (via deferral), F-009, F-010, F-011, F-012, F-013, F-014, F-015, F-016, F-017, F-018, F-019, F-020, F-021, F-022, F-023, F-024, F-025, F-027

**4 deferred with documented rationale:**
- **F-001 / F-002** — Clean Architecture violations (WI-06 deferred; pragmatic compromise documented in ARCHITECTURE.md §1.2; revisit when team grows or compliance demands)
- **F-008** — Email verification (WI-02 deferred to Phase 5-6; `IEmailService` abstraction ready for 1-line swap when paying customer arrives)
- **F-026** — Legacy Plan entity (`Wasnie.Domain.Entities.Plan`): reframed as intentional dual representation (EF persistence entity vs DDD domain entity); consolidation deferred; has active references throughout EF model
- **F-028** — Cross-tenant operations return 422 (UnprocessableEntity) instead of 404 for some endpoints; confirmed SYSTEMIC across Quotas/Assignments/PlanRules/Imports; deferred to future API contract standardization WI

### Phase C accomplishments

- **Tests grew from 210 → 339** (138 unit + 201 integration), zero regressions throughout
- **Security hardened end-to-end:** JWT lifetimes, token revocation, rate limiting, password policy, lockout, security headers, HSTS, CSP
- **Multi-tenant isolation production-grade:** global query filters on all 11 scoped entities, cross-tenant tests on every major endpoint
- **RBAC fully functional:** 4 roles, permission matrix, tier limits enforced, `*hasPermission` directive/pipe/guard, `/auth/me` endpoint
- **Audit trail in place:** immutable AuditLog entity with SQL trigger, 7 handlers retrofitted, 7-year retention ready
- **Observability foundation:** structured JSON logging, CorrelationId middleware, Serilog enrichment (TenantId, UserId, CorrelationId), frontend ErrorTrackingService + GlobalErrorHandler
- **Build clean throughout:** zero new warnings introduced in any WI

**Positive compliance areas** (no findings):
- All controllers are thin MediatR delegates
- Server-side pagination on every list endpoint
- EF Core global query filters on ALL 11 tenant-scoped entities (fully compliant after WI-05)
- No HttpClient in components (frontend HTTP architecture clean)
- No SQL injection (EF Core LINQ everywhere)
- Integration tests use real Testcontainers MSSQL
- No console.log in frontend
- CORS not wildcarded
- Multi-tenant isolation fully compliant (confirmed by WI-05 codebase audit)

For full audit: `docs/audit/Audit_Findings.md` | Backlog: `docs/audit/Audit_Backlog.md`

---

## Active work / current focus

**Right now we are:** 2026-06-11. **WI-STRIPE-FASE-3 COMPLETE.** Stripe Phase 3 (upgrade/downgrade + Billing Portal + ciclo PastDue) implementado y compilado. Test counts: 418 backend unit pass; integration tests (stale binary — API server running); frontend production build ✔. Next: correr integration tests en frío + configurar Stripe Customer Portal (plan-changes OFF, cancellation OFF, invoices ON, payment method ON). Latent debt: `CalculatePayoutsForPeriodHandler` sin tests de cálculo de dinero (ver nota en ARCHITECTURE.md).

**Sesión 2026-06-10/11 completada (A.6 Ph6 → Stripe Fase 2):**
- **A.6 Pay Run Fase 6** (filtros + export) — DONE, smoke GREEN. **A.6 COMPLETO — todas las 6 fases.**
- **Dashboard datos reales** — `GET /api/dashboard?period=` wired; 4 smoke bugs corregidos (links de cards, Pending Approval count, transacciones Cancelled en period band, params de período en links).
- **Void Pending** (`WI-A7`) + **Calculate-toggle** (`WI-2`) — DONE.
- **Nav Pay Runs group** (Pay Runs + Payouts sub-items) + **stale data fix** — DONE.
- **Cross-tab session sync** (`WI-TAB-SESSION-SYNC`, BroadcastChannel + localStorage fallback, 27 tests) — DONE.
- **Stripe Fase 1** (credenciales + endpoint de planes) + **wizard** + **tabla `UserSubscriptions`** + **Fase 2** (Checkout hosted + webhook, primer pago end-to-end smoke) — TODO DONE.
- **Stripe Fase 3 Step 0** — diseño aprobado, implementando.

**Deuda latente documentada:**
- `CalculatePayoutsForPeriodHandler.txIdsInPeriod` no filtra Cancelled explícitamente — seguro hoy (void solo Pending; no hay Credits para Cancelled), pero añadir filtro cuando se construya void-of-Calculated.
- Dashboard visual polish parkeado (cosmético, no bloquea venta).

**Next focus:**
- **INMEDIATO:** Completar `WI-STRIPE-FASE-3` — `POST /api/subscription/change-plan` (upgrade libre; downgrade bloqueado si excede límites), `POST /api/subscription/billing-portal`, webhooks `customer.subscription.updated` / `.deleted` / `invoice.payment_failed`, frontend botones + mensaje bloqueado + portal.
- Tras Fase 3: configurar Stripe Customer Portal (deshabilitar plan changes + cancelación; dejar facturas + método de pago ON) — instrucciones detalladas en el Step 0 report.
- Limpiar ~11 tests rojos pre-existentes antes del primer cliente.
- Exportación agregada de nómina (por pay run, para sistema de nóminas).
- Email notification al cerrar pay run (Resend infra → export adjunto → tier limits via `TierLimitChecker`).
- Clawbacks/ajustes WI (diferido post-A.6, Decision #66 item 6).
- Production hardening: WI-PROD-N (upload security), rate limit manual verification, Manager/Rep scoped data access.

**Known pending bugs (carry-forward):**
- ~11 pre-existing red tests (10 frontend: `ProcessPendingComponent×5`, `TransactionsListComponent×5`; 1 backend: `AssignmentsEndpointsTests`).
- Excel export usa valores de BD con 4 decimales en lugar de redondear a precisión de display (cosmético).
- `_lastLoadedFilter` race latente en `CreditsStore` + `TransactionsStore` — auditar antes del primer cliente.
- Legacy $0/USD payouts en dev tenant necesitan recalculación (issue de datos de dev únicamente).

**Right now we are (previous, 2026-06-08):** WI-A5-PAYOUTS-UI design fixes complete — payouts list + calculate modal consistent with design system. Build clean. WI-PROD-PAYEE-DASHBOARD-V3-FIX-4 DONE. Visual polish: gauge dot removed, bar tooltip tracks bar, current month visually distinct. All V3 fixes done. Next: WI-CALC-A.4 (Payout Engine). Quotas + Assignments cards now correctly respect the period filter (was silently defaulting to this-month for all periods due to buildHttpParams dropping the parameter). WI-CALC-A.3-FIX-3 DONE. WI-PROD-PAYEE-DASHBOARD-V3 DONE. Next: WI-CALC-A.4 (Payout Engine). /payees/:id Overview tab: period selector (This Month / Last Month / YTD / All Time) replaces Active/All toggle. All cards consistent. Temporal chips. Counters. Chart highlight. 342 unit + 455 integration tests. Both builds clean. Next: WI-CALC-A.4 (Payout Engine). WI-PROD-PAYEE-DASHBOARD-V2 DONE. /payees/:id completely redesigned: compact header, 3 tabs, Overview with 5 bento cards + virtual scroll + pacing gauges + period filter. 785 tests. Both builds clean. Next: WI-CALC-A.4 (Payout Engine). Bento dashboard live on /payees/:id → Attainment tab. ws-gauge + ws-line-chart SVG primitives added to shared UI. 784 tests. Both builds clean. Next: WI-CALC-A.4 (Payout Engine). Quota currency invariant enforced. 781 tests. Both builds clean. Next: WI-CALC-A.4 (Payout Engine). Audit query should be run manually against dev DB to find pre-existing mismatched Quotas. Quota Attainment Service live: real attainment replaces 1.0m stub in CreditAllocationService. Attainment tab on /payees/:id. 774 backend tests. Both builds clean. Next: WI-CALC-A.4 (Payout Engine).

**Most recent significant work (2026-06-09 — Payouts refinement A.5.1–A.5.6 + Pay Run design + full smoke):**
- **A.5.1 Interim Mitigation:** (1) Filter chip showed raw plan GUID on URL-param restore → resolved via `PlansApiService` lookup. (2) ROOT CAUSE: `CompensationPayout.Calculate()` had `Money.Zero("USD")` hardcoded → all $0 payouts saved as USD. Fixed with required `fallbackCurrency` parameter from handler's `planCurrency`; `DomainException` if empty; no silent default. (3) Payout list now starts filtered to current month. (4) `ExcludeZero` server-side toggle added to `ListPayoutsHandler`.
- **A.5.2:** Filter bar polish (replicating Dashboard V3 pattern). Bug fix: infinite API loop (`_pollJob` with no stop condition) → fixed with `takeWhile(inclusive=true)`; 3 regression tests.
- **A.5.3 Bulk Mark Paid:** `BulkMarkPaidCommand` + handler (mirrors BulkApprove, `IClock`, catches `DomainException` per item, skips non-Approved and reports conflicts). `POST /api/payouts/bulk-mark-paid`. "Mark as paid (N)" button with rich `WsModal` (5 mandatory elements: count, per-currency totals, scrollable payee list, irreversibility warning, skip warning). 3 backend + 3 frontend tests pass.
- **A.5.4:** Both bulk modals converted from `WsConfirmationModal` to rich `WsModal`; scrollable payee lists; payee names clickable → `/payees/:id` new tab. Bulk approve: reversible → no irreversibility warning. Mark-paid: irreversible → keeps warning. Tone differentiation preserved.
- **A.5.5:** Payee rows in both bulk modals now show name + period + amount (3-column `minmax` grid). `CurrencyFormatPipe` global fix: removed `minimumFractionDigits: 0` — EUR/USD/PLN→2 decimals, JPY→0, via CLDR. 3 new pipe tests (trailing-zero, always-2-decimal, JPY 0-decimal). 12/12 pipe tests pass.
- **A.5.6 Excel export (Payouts):** `GET /api/payouts/export` reusing `ListPayoutsHandler.BuildQuery`, 50k cap, `Permission.PayoutsExport`, ClosedXML 11-column xlsx. Export button positioned above table (matching Transactions pattern). Blob download via ephemeral anchor. 5 backend integration tests + 3 frontend unit tests.
- **Smoke test (full payout flow):** Calculate (EUR, 5% plan, line-by-line audit, total cuadra), Calculated→Approved→Paid, bulk approve (idempotent: protects Approved/Paid, reports conflicts), `ExcludeZero` toggle, infinite-loop bug confirmed fixed. All green.
- **Pay Run design:** `docs/Pay_Run_Model.md` approved with 6 closed decisions. See Decision #66. A.6 is next.
- **Test counts (last known):** Backend: 807 before session; A.5.3 added 3 integration, A.5.6 adds 5 integration (pending rebuild after DLL lock). Frontend: 10 pre-existing failures (ProcessPendingComponent×5, TransactionsListComponent×5) pre-date this session; A.5.3/A.5.5 added 6 new unit tests.

**Most recent significant work (2026-06-09 — A.6 Pay Run: Step 0 + Domain + Migration + Engine + Integration Tests):**
- **Step 0 / Reconciliation (Decisions #74–#79):** Spec reconciled against `docs/Pay_Run_Model.md` — 3 gaps, none blocking (PayRun missing from Spec domain model → add post-WI; "approved payout cannot be modified" compatible with Reopen because Reopen is pre-Paid; per-tenant serialization covered by the PayRun unique index). Six implementation decisions locked: UNIQUE index `(TenantId, PayRunId, PayeeId, PlanId) WHERE Status <> 'Paid' AND <> 'Disputed' AND PayRunId IS NOT NULL`; PayRunId nullable without backfill; `Permission.PayoutsReopen` (TenantAdmin + CompManager); PayRun unique `(TenantId, PeriodStart, PeriodEnd)`; `CalculatePayRunCommand` wraps A.4 engine via `ISender`; FK `ON DELETE RESTRICT` (not SET NULL).
- **Fase 1 — Domain:** `PayRunStatus` enum (Draft/Approved/Paid), `PayRun` aggregate (`Open/Approve/MarkPaid/Reopen/UpdateRollUps` + Cartesian guard), 3 domain events, `CompensationPayout` extended with `PayRunId`/`AssignToRun`/`RevertToCalculated`. 16 unit tests, all green.
- **Fase 2 — Migration:** `20260609132110_A6_AddPayRun` — `PayRuns` table, `CompensationPayouts.PayRunId` nullable FK, `ON DELETE RESTRICT`, rebuilt `IX_CompensationPayouts_Live`. Designer + snapshot consistent; applied to local DB.
- **Fase 3 — Engine + API:** 6 endpoints (GET /api/pay-runs, GET :id, POST calculate, POST :id/approve, POST :id/mark-paid, POST :id/reopen). `CalculatePayRunHandler` wraps A.4 per-payout engine via ISender; `UpdateRollUps` on every state transition; roll-ups = GROUP BY currency (no join, Cartesian-safe).
- **Fase 4 — Integration Tests:** 20 tests in 5 groups: idempotency (4), state machine valid (3) + invalid (3), roll-ups/anti-Cartesian (3), multi-tenant (2), permission gates (5). All 20 pass. Totals: 387 unit + 134 integration = **521** (518 pass; 1 pre-existing `AssignmentsEndpointsTests` failure; 2 skip). Build clean.
- **Fase 5 UI:** ✅ DONE (2026-06-09/10). `PayRunListComponent` at `/pay-runs` + `PayRunDetailComponent` at `/pay-runs/:id`; sidebar `/payouts`→`/pay-runs` redirect. `PayRunsStore` (global, `_lastLoadedFilter` race guard) + `PayRunDetailStore` (component-scoped via `providers: []`, no singleton). Calculate SYNC — no Hangfire job, no polling loop. Action modals with tone differentiation: Approve/Reopen (reversible, no irreversibility warning) vs. MarkPaid (irreversible, 5 mandatory elements per Decision #69). Pattern B per-currency roll-ups in header; `payeeCount` + `paidPayeeCount` distinct; audit fields (created/approved/paid + actor). i18n EN/ES/PL. Smoke-validated: full cycle calculate→Draft→Approve→Approved→Reopen→Draft→Approve→MarkPaid→Paid green.
- **Fase 6 — Filters + Export:** ✅ DONE (2026-06-10). Detail: collapsible filter bar (status, period from/to, amountMin/max, payee+plan chips via `WsSelect searchFn`, hide-$0 toggle) + Export to Excel button. List: manual date range pickers (from/to, segment resets to "All time" on manual date; default this-month first→last day) + Export to Excel button (`Payouts.Export` gate, ClosedXML, two-pass dynamic currency columns). Backend: `PayoutFilterQuery` extended (`PayRunId`, `AmountMin`, `AmountMax`); `ListPayoutsHandler.BuildQuery()` `static internal` (shared by list/detail/export — zero duplication); `ExportPayRunsHandler` + `PayRunExcelExportService`. `_lastLoadedFilter` race guard on both new stores. +43 frontend unit tests; +11 backend integration tests (`PayRunExportTests.cs`). Smoke: filter+export aligned (Agnieszka→2 rows); no loops.
- **Post-A.6 roadmap registered:** Email notification on run close (infra Resend → export attached → `/admin` recipient settings → tier limits Growth 2 / Scale 4 / Enterprise N via `TierLimitChecker` → trigger on PayRun Paid). GDPR concern: salary data by email requires strong recipient validation. Adjustments/clawbacks WI also deferred post-A.6.

**Most recent significant work (2026-06-03 — WI-PROD-T: Export + Re-upload + Process Pending skip fix):**
- **Part 1 — Process Pending skip:** `ProcessPendingTransactionsJobHandler` now catches `DomainException` per-transaction (currency mismatch etc.), skips the transaction (stays Pending), logs reason. Job completes normally with skip counts. `BackgroundJobRecord.ResultSummary` (nvarchar(max) nullable) added via migration `20260603093913_AddJobResultSummary`. `JobStatusDto` + `JobContext` extended. UI: `ProcessPendingComponent` shows skip count + expandable log of skipped transaction IDs + reasons. i18n EN/ES/PL.
- **Part 2 — Excel export:** `ExportTransactionsQuery` + `ExportTransactionsHandler` (same filters as ListTransactions, no pagination, 50K row cap). `TransactionExcelExportService` (ClosedXML): 10-column export, frozen header, ReferenceNumber marked as KEY. `POST /api/transactions/export`. `Transactions.Export` permission. Frontend: export button, blob download, >50K confirmation dialog.
- **Part 3 — Update-from-Excel wizard:** Full 5-step wizard at `/transactions/update-excel`. ReferenceNumber as fixed key. Per-row diff preview (old→new). Blocked on Paid, Credits superseded on Calculated. `CompensationTransaction.ApplyExcelUpdate()` domain method. `UpdateTransactionsFromExcelJobHandler` with per-transaction audit diffs. 3 new endpoints: `POST /api/imports/transactions/update/{parse,validate,execute}`. `Transactions.UpdateFromExcel` permission. "↻ Update from Excel" button in transactions list. i18n EN/ES/PL.
- **Test count: 752 backend + 159 frontend — unchanged. Both builds clean. Tests deferred per owner instruction.**

**Most recent significant work (2026-06-03 — WI-PROD-I.2: Advanced transaction filter):**
- **Backend:** `PaginationQuery` extended with 8 new filter fields: `Reference` (substring), `Statuses` (comma-separated multi-status), `PayeeIds` (comma-separated multi-payee), `IngestedFrom`/`IngestedTo`, `AmountMin`/`AmountMax`, `UnassignedOnly`, `AmountSort`. `ListTransactionsHandler` applies all filters. `PagedResult<T>` extended with `UnfilteredTotal?`. Migration `P3_TransactionPayeeIndex` adds `(TenantId, PayeeId)` index.
- **Frontend:** New `TransactionFilterComponent` (collapsible ws-card panel). Status multi-select toggle chips. Payee multi-select (ws-select async + removable chips). Date pickers × 4. Amount range inputs. Amount sort select. Debounced reference input (300ms). `TransactionsStore` rewritten with `TransactionFilter` composite object, `toQueryParams()`/`loadFromQueryParams()` URL sync. Legacy signal aliases kept for `ProcessPendingComponent` compat. Count header "Showing X of Y (Z total)". `ingestedAt` added to frontend model + shown as "Created" column.
- **Decision: Eligible tab removed.** `TransactionStatus.Eligible` is never set today — tab was always empty and confusing. Removed from status tabs and filter chips. Enum value preserved.
- **Tests: deferred per owner instruction.** See TODO_TESTS section.
- **Test count: 752 backend + 159 frontend — unchanged. Both builds clean.**

**Most recent significant work (2026-06-03 — WI-CALC-A.2.5: Procesar Pending — import warning + Hangfire job + UI):**
- **Decision #53:** `TransactionImportValidationService` now emits a `Warning` (IssueCategory.Required) when `payeeCode` is blank and `Transaction.PayeeId` is Optional. Message: "No Staff ID provided — this transaction will be imported as Unassigned and requires manual assignment for commission calculation." Row remains importable; comp manager decides whether to continue.
- **Decision #54 — Backend:** New `ProcessPendingTransactionsCommand` (IMoneyCriticalCommand) + `ProcessPendingScope` enum (ByPlanAssignment / ByPlan / ByPayeeAndPeriod). `ProcessPendingTransactionsJobHandler` Hangfire job: loads candidates by scope, applies skipping rule (skip transactions with non-superseded Credits from any plan), processes in chunks of 50, honors `CancellationToken` at chunk boundary, audit-logs the run. New permission: `Transactions.ProcessPending` (TenantAdmin + CompManager). New query: `GetPendingTransactionsCountQuery` for lightweight badge count.
- **Cancellation support:** `JobState` enum extended with `Cancelling` and `Cancelled` values (stored as string, no migration needed). `BackgroundJobRecord` gains `RequestCancellation()` and `MarkCancelled()`. `IBackgroundJobService` gains `CancelJobAsync()` and `MarkCancelledAsync()`. `HangfireJobDispatcher` catches `OperationCanceledException` → marks job Cancelled instead of Failed. `POST /api/jobs/{id}/cancel` endpoint added.
- **New endpoints:** `GET /api/transactions/pending-count`, `POST /api/transactions/process-pending`, `GET /api/assignments/{id}` (PlanAssignment detail page required a GetByIdQuery).
- **Decision #54 — Frontend:** `ProcessPendingComponent` (standalone) takes `scope`, `scopeId`, `periodStart`, `periodEnd` inputs; fetches candidate count on init; shows badge, volume notice when > 5,000, progress bar + Cancel button during job execution, terminal states. Added to: (a) new `AssignmentDetailComponent` at `/assignments/:assignmentId` (ByPlanAssignment scope); (b) `PlanDetailComponent` assignments tab (ByPlan scope); (c) `TransactionsListComponent` when payee+period filters are active (ByPayeeAndPeriod scope). `TransactionsStore` extended with `payeeIdFilter`, `dateFromFilter`, `dateToFilter` signals.
- **i18n:** `TRANSACTIONS.PROCESS_PENDING.*` + `ASSIGNMENTS.ERROR_LOAD` added in EN/ES/PL.
- **Pre-existing issue flagged:** Angular initial bundle 562.85KB (500KB warning budget). Pre-existing before this WI; new components are all lazy-loaded.
- **Test count: 743 → 752 backend (+9), 154 → 159 frontend (+5). Build clean.**

**Most recent significant work (2026-05-29 — WI-P2-04a-fix2: Excel native DateTime parsing):**
- **Root cause:** `cell.GetString()` on `XLDataType.DateTime` cells produced culture-dependent strings (`"4/1/2026 10:21:04 AM"`), rejected by the validator. All rows from real POS exports fail date validation.
- **Fix:** New `FileParserService.ReadCellAsString(cell)` — `DateTime` cells → ISO 8601 `"yyyy-MM-dd"` (InvariantCulture, time dropped); `Number` cells → `double.ToString(InvariantCulture)` (no currency formatting). Applies to all XLSX imports (payees and transactions).
- **Validator fix:** `TransactionImportValidationService.TryParseDate` now uses `CultureInfo.InvariantCulture` instead of `null` (thread culture) in `DateOnly.TryParseExact`.
- **Error message:** Bad date now shows the actual cell value: `"'hello' is not a recognisable date. Use YYYY-MM-DD."` (not the old misleading generic message).
- **Binding rule added:** `14-forbidden-patterns.md` — `cell.GetString()` on typed cells forbidden; `TryParseExact` must use InvariantCulture.
- **Tests:** +9 (3 parser: DateTime smoking-gun, culture independence pl-PL, Number invariant; 6 validator: formats theory, garbage message, culture independence, min boundary).
- **Test count: 561 passing (217 unit + 344 integration), 0 regressions.**

**Most recent significant work (2026-05-29 — WI-P2-04a-fix: row limit 300 → 10,000, configurable):**
- `MaxRows = 300` hardcoded constant removed from `FileParserService`. Added `ImportOptions` (Application) bound via `IOptions<ImportOptions>` from `appsettings.json` section `"Imports": { "TransactionMaxRows": 10000, "PayeeMaxRows": 300 }`.
- `IFileParserService.ParseAsync` signature updated: accepts `int maxRows` parameter. Controllers pass `Imports.TransactionMaxRows` or `Imports.PayeeMaxRows` per caller — parser stays stateless.
- Startup validation: `ValidateOnStart()` rejects limits outside [1, 100,000].
- **Payee import stays at 300** — synchronous path, Rule 3.2.5 applies.
- New endpoint `GET /api/imports/transactions/limits` returns `{ maxRows }`. Frontend upload-step fetches it on init; `CONSTRAINT_ROWS` i18n key now parameterised with `{{ count }}` in EN/ES/PL.
- **Tests:** +5 (boundary exact-at-limit CSV+XLSX, one-past-limit CSV+XLSX, transaction-limit-accepts-301-rows).
- **Test count: 552 passing (217 unit + 335 integration)** (before fix2).

**Most recent significant work (2026-05-29 — WI-P2-04b: Transaction import wizard UI):**
- 5-step wizard (`upload → map → preview → progress → complete`) at `/transactions/import`. Mirrors payee wizard; the Progress step is new.
- `TxProgressStepComponent`: polls `GET /api/jobs/{id}` every 3 s via `timer(0, 3000)` + `takeUntilDestroyed`. Stops on `Succeeded`/`Failed` (explicit `_polling.unsubscribe()`) AND on component destroy (`takeUntilDestroyed`). Transient network errors set `netError` signal without failing the job.
- Column auto-detect (`detectField()`) covers EN/ES/PL patterns for 6 transaction fields.
- Route `/transactions/import` gated to `Transactions.Create`. Sidebar entry added.
- Progress bar: LOCAL CSS only in `progress-step.component.scss` (not shared `WsProgressBar` — pending elevation to design system when 2+ features use it, per §10.3).
- **Frontend tests:** +40 (service HttpTestingController, progress step fakeAsync zombie-poll guard, mapping auto-detect).
- DESIGN_SYSTEM.md updated with 5-step async wizard variant and polling pattern.

**Most recent significant work (2026-05-28 session — WI-P2-BG-a):**
- **Architecture decision:** Hangfire 1.8.x (LGPLv3 — NOT MIT, correction from inspection) over hand-rolled SQL jobs. Azure F1 plan: no Always On, app unloads after ~20 min idle; Hangfire jobs survive recycle in SQL. B1 upgrade ($13/month, Always On) deferred to first paying customer.
- **New Domain:** `BackgroundJobRecord` entity + `JobState` enum (`Wasnie.Domain.BackgroundJobs`). EF migration `20260528135529_AddBackgroundJobs` (table + 2 indexes). Query filter tenant-isolates the table.
- **New Application interfaces:** `IBackgroundJobService`, `IJobHandler<TPayload>`, `IJobHandlerBase`, `JobHandlerBase<T>` abstract base, `JobStatusDto`, `JobContext`, `GetJobStatusQuery` (MediatR).
- **Critical DI change:** `ApplicationDbContext.CurrentTenantId` changed from eager (`{ get; } = tenantContext.TenantId`) to lazy (`=> tenantContext.TenantId`). EF evaluates query filters per-query, not at construction — allows background job scopes to set tenant before first query. **Regression discovered and fixed:** `AuthorizationService` catch-all was swallowing `UnauthorizedAccessException` from `tenantContext.TenantId`, turning expected 401s into 403s. Fixed by adding `catch (UnauthorizedAccessException) { throw; }`.
- **BackgroundJobTenantContext** (Infrastructure): Scoped, mutable, THROWS if `TenantId` is read before `SetTenant()`. Registered via factory: HTTP scope → `TenantContext`; no-HTTP scope → `BackgroundJobTenantContext`. `HangfireJobDispatcher` calls `SetTenant(tenantId)` from payload as FIRST action, before any DB access.
- **Hangfire wiring:** `HangfireJobDispatcher` (non-generic entry point), `HangfireBackgroundJobService`, `PingJobHandler` (smoke test), `HangfireDashboardAuthorizationFilter` (dev-only, blocked in Production until SystemAdmin role added — cross-tenant data risk).
- **API:** `GET /api/jobs/{id}` — authenticated, tenant-isolated (cross-tenant → 404). Dashboard at `/jobs` (dev-only, auth required).
- **Tests:** 217 unit (unchanged) + 277 integration (+34 vs 243 baseline). `BackgroundJobTenantContextTests` (5): throw-before-set, empty guid rejected, happy path, SetTenant twice. `PingJobIntegrationTests` (1): end-to-end enqueue → execute → Succeeded, cross-tenant isolation. All regression tests green.
- **TestWebApplicationFactory**: adds `ConnectionStrings:DefaultConnection` override so Hangfire uses the Testcontainers SQL Server in all integration tests.

**Most recent significant work (2026-05-28 session — WI-P2-FIX-select):**
- `ws-select.component.ts`: additive async mode — `searchFn` input (`(q: string) => Observable<SelectOption[]>`), `initialOption` input for edit-mode label resolution, `asyncOptions`/`asyncLoading` signals, `DestroyRef`-based `switchMap` pipeline with 300ms debounce + `takeUntilDestroyed`; `options` input changed from required to optional
- `ws-select.component.html`: search input shown when `searchable() || searchFn()`; animated 3-dot loading indicator (`asyncLoading()`); empty state guarded by `!asyncLoading()`
- `ws-select.component.scss`: `.ws-select__loading` (3-dot animated indicator using `@keyframes ws-select-dot`)
- `transaction-form.component.ts`: removed `PayeesStore` dependency, added `PayeesApiService` + `payeeSearchFn`; removed `OnInit`/`ngOnInit`/`payeeOptions` signal
- `payee-form.component.ts`: removed `managerOptions` signal + `_rebuildManagerOptions` + `ngOnInit`; added `PayeesApiService` + `managerSearchFn` + `managerInitialOption` computed (reads `payee().managerId/managerName/managerEmployeeCode`)
- `assignment-create.component.ts`: removed `PayeesStore`/`PlansStore`; added `PayeesApiService`/`PlansApiService` + `payeeSearchFn`/`planSearchFn`; added `preselectedPayeeOption`/`preselectedPlanOption` signals; constructor subscription for `planId.valueChanges → plansApi.getPlan() → patchValue(dateRange) + preselectedPlanOption`; `ngOnInit` handles queryParam preselection via `firstValueFrom`
- `quota-create.component.ts`: removed `PayeesStore`/`PlansStore`; added `payeeSearchFn`/`planSearchFn`; `preselectedPayeeOption` signal for payeeId queryParam
- All 4 HTML templates updated (`[searchFn]` + `[initialOption]` replacing `[options]` + `[searchable]`)
- `transaction-form.component.spec.ts`: updated to mock `PayeesApiService` instead of `PayeesStore`
- `ws-select.component.spec.ts` (NEW): 16 tests — client-side unchanged behavior (7), async debounce, loading state, empty state vs loading, initialOption fallback, asyncOptions priority over initialOption, initial load on open
- `DESIGN_SYSTEM.md`: WsSelect async mode subsection added
- **Frontend test count: 98/98 pass, build clean**

**Most recent significant work (2026-05-28 session — WI-P2-03c):**
- `transaction.model.ts`: `Transaction`, `TransactionStatus` (Pending/Eligible/Calculated/Paid/Cancelled), `TransactionSource`, `CreateTransactionRequest`
- `TransactionsApiService`: `list(params)`, `getById(id)`, `create(request)` — mirrors Payees service pattern
- `TransactionsStore`: signals-based store with `effect()` auto-reload, status filter, paginated state, `createTransaction()` + `loadTransactions()`
- `TransactionsListComponent`: status segmented-control filter, paginated table with skeleton rows, status badges (Pending=warning, Eligible=info, Calculated=brand, Paid=success, Cancelled=neutral), payee name lookup via injected `PayeesStore`, `*hasPermission="'Transactions.Create'"` gates New button
- `TransactionFormComponent`: Payee select (full row), Reference number input (full row), Transaction date (col1) + Amount+Currency amount-pair (col2); source shown as read-only info text; form validates required + amount > 0
- `TransactionCreateComponent`: thin wrapper navigates back to `/transactions` on save/cancel
- `transactions.routes.ts`: `''` → list, `'new'` → create
- **Bugs fixed:** `app.routes.ts` changed from `loadComponent` + `Reports.ViewAll` → `loadChildren` + `Transactions.Read`; `sidebar.component.ts` changed `Reports.ViewAll` → `Transactions.Read`
- **i18n:** TRANSACTIONS namespace added to EN/ES/PL (29 keys each)
- **§5b.8 disclaimer: NOT required** — raw sales transactions are objective facts, not estimated commission amounts
- **Payee name resolution:** client-side lookup via `PayeesStore.payees()` — tech debt; backend DTO enhancement (`PayeeName` field) would be cleaner
- **17 new frontend tests:** 4 service (HttpTestingController), 7 store (signals, filter, error), 6 form (validation, submit, cancel)
- **Frontend test count: 17 new (all pass)**

**Previous significant work (2026-05-28 session — WI-P2-03b):**
- `ListTransactionsQuery` + `GetTransactionByIdQuery` in Application, matching the Payees list pattern exactly
- `ListTransactionsHandler`: RBAC (`Permission.TransactionsRead`), sort whitelist (`transactionDate`/default, `amount`, `status`, `ingestedAt`, `referenceNumber`), unknown sort → safe fallback (no 500), filters: status (name-based `Enum.TryParse`), payeeId, source, dateFrom, dateTo; paginated via `ToPagedResultAsync`
- `GetTransactionByIdHandler`: RBAC, `FirstOrDefaultAsync` → global filter → cross-tenant null → 404
- `TransactionsController`: `GET /api/transactions` + `GET /api/transactions/{id}` added
- `PaginationQuery` extended with `Source`, `DateFrom`, `DateTo` (backward compatible; existing handlers ignore them)
- Migration `P2_TransactionReadIndexes`: 3 new read-path indexes — `(TenantId, TransactionDate)`, `(TenantId, Status)`, `(TenantId, IngestedAt)` per Rule 3.2.2
- **Amount sort flagged:** no index on `(TenantId, Amount)` — included in whitelist but may need index at Enterprise scale (10k+ txns). See decision #33.
- 27 integration tests in `TransactionReadEndpointsTests`: default pagination, page 2, pageSize>100 → 400, sort by each whitelisted field (asc/desc), invalid sort fallback, all 5 filters, combined filter+sort+pagination, 401, 403×3 (Manager/Rep/CompManager passthrough), cross-tenant list, cross-tenant get-by-id → 404, nonexistent → 404, happy path get-by-id
- **Test count: 488 passing (217 unit + 271 integration), 2 intentionally skipped**

**Previous significant work (2026-05-28 session — WI-P2-03a):**
- `IngestTransactionCommand` (implements `IMoneyCriticalCommand`, mutable `AuditResourceId` so handler sets it after SaveChanges)
- `IngestTransactionCommandValidator` (sync FluentValidation: ReferenceNumber not-empty/≤200, PayeeId not-empty, Amount > 0, Currency exactly 3 chars, TransactionDate ≥ 2000-01-01)
- `IngestTransactionHandler`: RBAC (`Permission.TransactionsCreate`), payee existence check (tenant-scoped), `Money.Of`, `TransactionSource.Manual`, `CompensationTransaction.Ingest`, `SaveChangesAsync`, sets `request.AuditResourceId`; no `IAuditService` injection — `AuditBehavior` handles audit atomically
- `TransactionsController`: thin MediatR delegate, `POST /api/transactions`, 201 + Location header on success
- `Permission.TransactionsCreate` + `Permission.TransactionsRead` added to Domain and granted to TenantAdmin + CompManager
- `AuditActions.TransactionIngested = "TRANSACTION_INGESTED"` added
- 16 unit tests (`IngestTransactionCommandValidatorTests`) + updated `RolePermissionsTests` (6 new inline cases)
- 14 integration tests (`TransactionsEndpointsTests`): happy path, auth, authz (3 roles), 4 validation rules, payee-not-found, 2 cross-tenant isolation, money-critical audit atomicity
- `TestDatabaseFixture.ResetTransactionsAsync()` added
- **Test count: 460 passing (217 unit + 243 integration), 2 intentionally skipped**

**Previous significant work (2026-05-28 session — WI-P2-02):**
- `CompensationTransactionStatus` enum replaced with full spec lifecycle: `Pending=0, Eligible=1, Calculated=2, Paid=3, Cancelled=4` — `Credited` removed (table was write-orphan; safe destructive replacement per §8.4.1)
- `ExternalReference` field renamed to `ExternalId` everywhere (entity, EF config, migration) — aligns code with spec §5.3.1 naming
- Migration `P2_TransactionDomainSurgery`: `sp_rename ExternalReference→ExternalId` + filtered unique index `(TenantId, Source, ExternalId) WHERE ExternalId IS NOT NULL`
- `Ingest` factory now enforces invariants: empty `TenantId`/`PayeeId` (Guid.Empty), null/blank `referenceNumber`, null/empty `ingestedBy`, `transactionDate < 2000-01-01` — all throw `DomainException`; no `DateTime.UtcNow` introduced (Rule 2.5.3)
- `MarkEligible(updatedBy, now, eventId)` added: Pending → Eligible, raises `TransactionMarkedEligibleEvent` (new event)
- `MarkCredited` removed; replaced by `MarkCalculated`/`MarkPaid` Phase 3 stubs (throw `NotSupportedException`)
- `Cancel` updated: Pending/Eligible → Cancelled allowed; Calculated/Paid → DomainException with Phase 3 clawback note
- §5b.7 gap closed: every state-change method now raises a domain event
- EF1002 warning eliminated: `MultiTenantDefenseTests.cs:47` switched from `ExecuteSqlRawAsync` (concatenated GUIDs) to `ExecuteSqlAsync(FormattableString)` (parameterized)
- 27 domain unit tests (`CompensationTransactionTests`) + 5 integration tests (`CompensationTransactionIdempotencyTests`: unique violation, null-ExternalId exemption, cross-tenant allow, global filter isolation)
- **Test count: 419 passing (190 unit + 229 integration), 2 intentionally skipped — promoted to 460 by WI-P2-03a**

**Previous significant work (2026-05-28 session — WI-P2-01b):**
- `[JsonConstructor]` removed from `Money.cs`; `using System.Text.Json.Serialization` removed — Rule 1.5 violation fully resolved
- `MoneyJsonConverter : JsonConverter<Money>` added to `Wasnie.Infrastructure.Persistence.Serialization` — wraps `DomainException` from `Money.Of()` as `JsonException` with inner exception; backward-compatible output format (`{"amount":<decimal>,"currency":"<ISO3>"}`)
- Registered in `PlanRuleConfiguration` and `PayoutLineConfiguration` via `BuildJsonOptions()` factory; registered globally in `Program.cs` via `AddControllers().AddJsonOptions(...)` (covers HTTP layer: `AddRuleToPlanCommand.Cap/Floor`)
- Two bugs fixed during the run: (1) `DomainException` not wrapped as `JsonException` inner exception — caught and re-thrown; (2) `[Trigger]` must be bracket-quoted in raw SQL — reserved keyword in SQL Server
- 17 serialization unit tests (`MoneyJsonConverterTests`) + 3 DB round-trip integration tests (`MoneyRoundTripTests`)
- Round-trip tests use `ExecuteSqlAsync(FormattableString)` — EF Core parameterizes each interpolation hole; JSON `{...}` content in variables is never treated as placeholder syntax
- **Test count: 387 passing (163 unit + 224 integration), 2 intentionally skipped**

**Previous significant work (2026-05-28 session — WI-P2-01):**
- `Money` value object refactored to full §5b.5 compliance:
  - 4-decimal internal normalization with banker's rounding (`MidpointRounding.ToEven`) in private constructor — every code path goes through it
  - `Negate()` and `Abs()` methods added
  - Four comparison operators (`>`, `<`, `>=`, `<=`) added — same-currency only, throws `DomainException` on currency mismatch
  - `GuardSameCurrency` changed to `private static` to support operator usage
- 25 new unit tests covering all new behaviors: normalization, banker's rounding midpoints, Negate, Abs, comparison operators (same + different currency), equality regression guard
- Data safety verified: grep across all .cs and .json found no persisted monetary value exceeding 4 decimals — normalization is safe
- **Test count: 367 passing (163 unit + 204 integration), 2 intentionally skipped**

**Previous significant work (2026-05-28 session — WI-P2-00):**
- `IMoneyCriticalCommand` marker interface introduced (`Wasnie.Application/Common/Interfaces`)
- `IApplicationDbContext.Database` property exposed (consistent with F-001 deferral)
- `AuditBehavior` extended with transactional money-critical path; non-money path byte-for-byte unchanged
- 3 new integration tests in `MoneyAuditTransactionTests` (own Testcontainers fixture, isolated from shared fixture)

**Previous significant work (2026-05-27 session — Phase C OFFICIAL CLOSURE):**
- WI-09a: Backend RBAC + tier limits — 4 roles, 29 handlers refactored, /auth/me endpoint, 50 new tests; 280/280 pass
- WI-09b: Frontend RBAC integration — CurrentUserService (signals), *hasPermission directive/pipe/guard, forbiddenResponseInterceptor, TierLimitModal, sidebar gating; 59/59 frontend tests pass
- WI-10: 3 new validators + 3 new integration test files (Quotas, Assignments, PlanRules), 44 tests; F-028 confirmed systemic; 324/324 pass
- WI-13: F-020 safety comments, F-021 token fix, F-024 confirmed + CONTRIBUTING.md, F-026 reframed
- WI-11: SecurityHeadersMiddleware, rate limiter, password/lockout hardening, HSTS; 7 tests
- WI-12: CorrelationIdMiddleware (first in pipeline), Serilog JSON config-driven, TenantUserCorrelationEnricher, frontend ErrorTrackingService/GlobalErrorHandler/correlationIdInterceptor; 14 tests
- **Final count: 339 tests pass (138 unit + 201 integration), 2 intentionally skipped; build clean**

**Not yet started:**
- Phase D (coverage > 80% backend / > 60% frontend, E2E tests, Master Spec v2.1) — optional
- WI-P2-03c — manual transaction entry UI (needs `DESIGN_SYSTEM.md`) ← next recommended
- Phase 2 Calculation Engine — core product IP
- Marketing / content strategy (planned for parallel work once Phase 1 fully closed)

---

## TODO_TESTS — Deferred test backfill

### WI-PROD-QUANTITY-FIELD — Quantity field

**Backend unit tests:**
- `TransactionFieldValidators.ValidateQuantity("")` → null, parsed=1
- `ValidateQuantity("3")` → null, parsed=3
- `ValidateQuantity("0")` → Error, Format
- `ValidateQuantity("-1")` → Error, Format
- `ValidateQuantity("abc")` → Error, Format
- `CompensationTransaction.Ingest(quantity: 0)` → throws DomainException
- `ApplyExcelUpdate(newQuantity: -1)` → throws DomainException

**Backend integration tests:**
- `IngestTransactionHandler`: Quantity=5 round-trips through API → stored and returned in DTO
- `TransactionImportJobHandler`: CSV row with Quantity=3 → transaction.Quantity=3; blank → 1
- `UpdateTransactionsFromExcelJobHandler`: Quantity change 1→5 → diff in preview, applied in execute
- `ExportTransactionsHandler`: Quantity column present in xlsx output

**Frontend:**
- `transaction-form`: Quantity field renders, min=1 validation rejects 0
- `TransactionsStore`: Quantity passed through createTransaction request

---

### WI-PROD-CREDITS-EXPORT — Credits Excel export

**Backend integration tests:**
- `ExportCreditsHandler`: seed 5 credits with 2 payees + 1 plan → export → verify row count and column values.
- `ExportCreditsHandler`: filter Status=Active → superseded credit excluded.
- `ExportCreditsHandler`: filter Currencies=PLN → only PLN credits included.
- `ExportCreditsHandler`: count > 50,000 → returns EXPORT_TOO_LARGE failure.
- `ExportCreditsHandler`: predicate alignment — same filter as list endpoint → same count.
- `GET /api/credits/export`: 401 (no auth), 403 (Manager role has no CreditsExport), 200 with blob (TenantAdmin).

**Frontend tests:**
- `CreditsStore.toExportParams()`: filter with payeeIds + currencies → correct PaginationParams.filters mapping.
- `CreditsListComponent.onExport()`: triggers API call, sets exporting true/false, triggers blob download.

---

These tests were explicitly deferred by owner instruction on 2026-06-03 (WI-PROD-I.2). Add in a dedicated test WI before the first paying customer.

### WI-PROD-FILTERS-CURRENCY-RULE — Currency + Rule filters

**Backend integration tests:**
- `ListTransactionsHandler`: `Currencies=EUR` → returns only EUR transactions; `Currencies=EUR,PLN` → returns both.
- `ExportTransactionsHandler`: same `Currencies` predicate produces same row count as list endpoint.
- `ListCreditsHandler.BuildQuery`: `RuleIds=<guid>` → returns only credits where `c.RuleId == guid`.
- `GetCreditCountersHandler` + `GetCreditsByPayeeHandler`: verify they respect `Currencies` and `RuleIds` (both go through `BuildQuery`).

**Frontend store tests:**
- `TransactionsStore._buildFilterRecord`: `currencies: ['EUR', 'PLN']` → `currencies: 'EUR,PLN'` in record.
- `TransactionsStore.toQueryParams`: `currencies: ['EUR']` → `currencies: 'EUR'` in URL params.
- `TransactionsStore.loadFromQueryParams`: `?currencies=EUR,PLN` → `f.currencies = ['EUR', 'PLN']`.
- `CreditsStore._buildFilterRecord`: `ruleIds: ['<id>']` → `ruleIds: '<id>'` in record.
- `CreditsStore.toQueryParams` + `loadFromQueryParams`: round-trip for `ruleIds`.

**Frontend component tests:**
- `TransactionFilterComponent`: currency chip toggles (add/remove, multi-select pattern).
- `CreditsListComponent`: currency chip toggles; rule chips appear when plan is selected; rule chips hidden when no plan.

---

### WI-PROD-T-FIX-9 — UPDATE wizard currency + field validation

**Backend unit tests for `TransactionFieldValidators`:**
- `ValidateCurrency("EUR")` → null (valid)
- `ValidateCurrency("3SD2F13SD")` → Error, field="currency", category=Format
- `ValidateCurrency("usd")` → Error (lowercase fails `^[A-Z]{3}$`)
- `ValidateCurrency("")` → Error
- `ValidateAmount("100", out decimal)` → null, parsed=100
- `ValidateAmount("-1", out decimal)` → Error (≤ 0)
- `ValidateAmount("abc", out decimal)` → Error (not a number)
- `ValidateTransactionDate("2026-05-15", today, out DateOnly)` → null, parsed correctly
- `ValidateTransactionDate("31/05/2026", today, out DateOnly)` → Error (not ISO 8601)
- `ValidateTransactionDate("1999-12-31", today, out DateOnly)` → Error (before min date)
- `ValidateTransactionDate(future date, today, out DateOnly)` → Error (future)

**Backend integration tests for `TransactionUpdateValidationService`:**
- Row with currency "3SD2F13SD" → `UpdateRowStatus.Error`, issue field="currency", category=Format
- Row with currency "EUR" (different from existing PLN) → `UpdateRowStatus.WillUpdate`, diff shows PLN→EUR
- Row with amount "abc" → `UpdateRowStatus.Error`, issue field="amount"
- Row with amount "-5" → `UpdateRowStatus.Error`, issue field="amount"
- Row with date "31/05/2026" → `UpdateRowStatus.Error`, issue field="transactionDate"
- Row with date "1999-01-01" → `UpdateRowStatus.Error`, issue field="transactionDate"
- Row with inactive payee code → `UpdateRowStatus.WillUpdate` (warning, not error) + diff shows payee change

### WI-PROD-T-FIX-5 — Enrich skip log + open-in-filter

**Backend integration tests:**
- `ProcessPendingTransactionsJobHandler`: seed payee + EUR plan assignment + PLN transaction; run job; verify `ResultSummary.skipDetails[0]` has `refNum`, `txDate`, `amount`, `currency`, `payeeName`, `payeeCode`, `reason` populated correctly.
- `ListTransactionsHandler`: add `ReferenceNumbers = "REF1,REF2"` to `PaginationQuery`; verify exact match — `REF1` and `REF2` returned, `REF3` not.
- `ListTransactionsHandler`: `ReferenceNumbers` with one valid + one non-existent ref → returns only the valid one.
- `ExportTransactionsHandler`: `ReferenceNumbers` filter produces the same rows as `ListTransactionsHandler` with the same filter.

**Frontend component tests:**
- `ProcessPendingComponent`: when `skipDetails` contains enriched entries, rendered rows show `refNum`, not `txId`.
- `ProcessPendingComponent`: `onOpenInFilter()` calls `router.navigate` with `{ queryParams: { refs: 'REF1,REF2' } }`.
- `TransactionsStore.loadFromQueryParams`: `refs=REF1,REF2` → `referenceNumbers: ['REF1', 'REF2']`.
- `TransactionsStore._buildFilterRecord`: `referenceNumbers: ['REF1']` → `referenceNumbers: 'REF1'` in the filter record.

---

### WI-PROD-T-FIX-4 — Strict ISO date validation

**Backend unit tests (MUST add before first paying customer):**
- `TransactionImportValidationService`: validate row with `31/05/2026` → `IssueSeverity.Error`, `IssueCategory.Format`, message contains "ISO 8601".
- `TransactionImportValidationService`: validate row with `05/31/2026` → same.
- `TransactionImportValidationService`: validate row with `15-05-2026` → same.
- `TransactionImportValidationService`: validate row with `2026-05-15` → no date error.
- `TransactionImportValidationService`: validate row with `2026/13/45` → error (invalid date, not merely wrong format).
- `TransactionImportJobHandler.TryParseDate` (or via integration): `"31/05/2026"` → returns `false`; `"2026-05-31"` → returns `true`.

---

### WI-PROD-T-FIX-3 — Currency mismatch per-row validation

**Backend integration tests (MUST add before first paying customer):**
- `TransactionImportValidationService`: seed payee with active EUR plan assignment; validate a batch with mixed EUR + PLN rows; verify PLN rows have `IssueSeverity.Error`, `IssueCategory.Reference`, field `"currency"`, and message contains plan name and currencies.
- `TransactionImportValidationService`: seed payee with NO active assignment for the transaction date; verify no currency error is emitted (row imports as Pending).
- `TransactionImportValidationService`: seed payee with active EUR assignment; validate row with valid EUR currency; verify no currency issue emitted.
- `TransactionImportJobHandler` (belt-and-suspenders): force a `DomainException` in `AllocateAsync` for one row in a batch; verify the batch completes, other rows are imported, `skippedByDomainValidation` is counted in `totalSkipped`.

---

### WI-PROD-T-FIX-2 — Update wizard i18n + merge

**TODO:** Test that `TransactionImportWizardComponent` correctly renders CREATE steps in default mode and UPDATE steps when `?mode=update` is passed. Specifically: step label translation, correct step component mounted per mode, and that UPDATE mode state signals are independent from CREATE mode state signals.

---

### WI-PROD-T-FIX-1 — Excel export filter alignment

**Regression guard (MUST add before first paying customer):**
- Test: applying filter combination F (payee + status + date range + amount range) to `GET /api/transactions` returns `totalCount = N`. The same F sent as body to `POST /api/transactions/export` must produce a file with exactly N data rows. Any divergence means `_buildFilterRecord` in the store and `ExportTransactionsHandler` are out of sync.

---

### WI-PROD-T — Export + Re-upload + Process Pending skip fix

**Backend integration tests:**
- `ProcessPendingTransactionsJobHandler`: seed mix of EUR + PLN transactions, run job, verify EUR are Calculated, PLN remain Pending, `ResultSummary.skippedByValidation` matches expected count.
- `POST /api/transactions/export`: with each filter combination, verify correct columns and row count.
- `POST /api/imports/transactions/update/validate`: diff computation per field (amount, currency, date, payeeId); Paid blocks; missing reference errors.
- `UpdateTransactionsFromExcelJobHandler`: Calculated transaction → Credits superseded + Status reset to Pending; audit log before/after JSON populated.

**Frontend component tests:**
- `ProcessPendingComponent`: skip log expand/collapse, `DONE_WITH_SKIPS` message rendered when `skippedByValidation > 0`.
- `TransactionsListComponent`: Export button shown only when `totalCount > 0`; `onExport()` triggers blob download.
- `TransactionUpdateWizardComponent`: step transitions upload→map→preview→progress→complete.

---

### WI-PROD-I.2 — Advanced transaction filter

**Backend unit tests:**
- `ListTransactionsHandler`: each of the 8 filter fields applied in isolation, verify SQL WHERE predicate.
- `ListTransactionsHandler`: multiple filters combined (AND logic).
- `ListTransactionsHandler`: `Statuses` comma-parsing, invalid values ignored.
- `ListTransactionsHandler`: `PayeeIds` comma-parsing, invalid GUIDs ignored.
- `ListTransactionsHandler`: `UnfilteredTotal` returned correctly even when filters reduce page to 0.

**Backend integration tests:**
- End-to-end: seed 50 transactions with mixed statuses, payees, amounts, dates → apply each filter → verify count + items.
- Count alignment: filter(Status=Pending, PayeeId=X, DateFrom, DateTo) count == GetPendingTransactionsCountQuery(ByPayeeAndPeriod, X, DateFrom, DateTo).

**Frontend component tests:**
- `TransactionFilterComponent`: status chip toggles (add/remove, multi-select).
- `TransactionFilterComponent`: payee chip add and removal.
- `TransactionFilterComponent`: clear all resets form and emits `cleared`.
- `TransactionFilterComponent`: debounce — reference input emits filterChange after 300ms.
- `TransactionsListComponent`: URL sync — loadFromQueryParams called on init with URL params.
- `TransactionsStore`: `toQueryParams()` serializes all active filters.
- `TransactionsStore`: `loadFromQueryParams()` deserializes and applies filters.

---

## Important decisions made

> **Note on numbering — 2026-06-02:** Decision numbers are assigned in order of WRITING to this log, not in the order in which decisions were conceptually made. Decisions #55–#64 were backfilled on 2026-06-02 from a chat conversation earlier that day; they were decided BEFORE #50–#54 (which document implementation WIs and follow-up decisions) but written AFTER. Each backfilled entry is marked accordingly.

1. **Document hierarchy:** ARCHITECTURE.md > Product Spec > DESIGN_SYSTEM > Master Plan. Conflicts resolved in this order.
2. **Strict architecture enforcement:** Claude (chat) acts as gatekeeper. Refuses prompts that violate ARCHITECTURE.md.
3. **All technical docs in English.** Chats in Spanish.
4. **Personal Trainer background NEVER mentioned** in Wasnie context (separate professional identity).
5. **Claude Code autonomy boundary:** auto-approve file/build/test, NEVER autonomous git operations.
6. **A4 (E2E tests) deferred to Phase 9** (Compliance & Enterprise readiness).
7. **Subscription tiers:** Free / Starter (€300) / Growth (€800) / Scale (€1,800) / Enterprise (€2,500+).
8. **Target markets in order:** Poland → Central & Eastern Europe → Iberian & LATAM markets.
9. **Mobile responsive deferred to Phase 8.** Desktop-only until then (1280px+).
10. **Email provider (WI-02) deferred to Phase 5-6.** Real email service (Postmark/SendGrid/AWS SES) integrated when first paying customer is identified. `IEmailService` abstraction + DI swap requires only infrastructure changes when ready. (2026-05-27)
11. **Multi-tenant isolation confirmed fully compliant** after WI-05. All 11 tenant-scoped entities have global query filters; only 1 `IgnoreQueryFilters()` in source (now guarded). (2026-05-27)
12. **TenantContext null-HttpContext behavior:** Returns `Guid.Empty` for null `HttpContext` (background services, test fixture cleanup scopes). Throws `UnauthorizedAccessException` only when authenticated request lacks valid `tenant_id` claim. Revisit when observability (WI-12) provides alerts on suspicious empty-result queries. (2026-05-27)
13. **WI-06 strict Clean Architecture refactor deferred:** EF Core in Application and MediatR in Domain remain as documented pragmatic compromises. Violations documented in ARCHITECTURE.md §1.2. Revisit when team grows or when external compliance requires stricter purity. (2026-05-27)
14. **Audit trail pattern is hybrid:** Explicit `IAuditService.LogAsync(...)` calls for handlers where before/after diff or post-save resource ID is needed (5 handlers); `IAuditableCommand` marker + `AuditBehavior` pipeline for commands where resource ID is in the command (2 handlers). Both patterns are valid and co-exist intentionally. (2026-05-27)
15. **`AuditLog.Id` uses BIGINT (long) instead of GUID:** Better for high-cardinality, write-only audit table — improved index performance, smaller storage. UUID distribution not needed for a sequential write table. (2026-05-27)
16. **Audit dispatcher swallows failures in Phase 1:** Acceptable per Rule 5.3.3 for non-money operations. When Phase 2 (Transactions) starts, audit failures on money operations MUST cause transactional rollback. Flagged for Phase 2 pre-work. (2026-05-27)
17. **RBAC refactored all 29 existing handlers:** All command/query handlers now call `RequireAsync(permission)`. New Phase 2 handlers must include RBAC checks from inception. (2026-05-27)
18. **Manager/Rep scoped data access deferred:** Currently they can view any payee/plan in their tenant (not scoped to direct reports). Acceptable for Phase 1 launch with small tenants. Future enhancement WI when tenant size justifies it. (2026-05-27)
19. **Tier limits are count-based only:** Max payees and max plans enforced per tier. Feature-gate architecture prepared (`IFeatureGate` interface) but no specific feature gates registered yet. (2026-05-27)
20. **F-028 (cross-tenant 422 vs 404) confirmed systemic:** Quotas, Assignments, PlanRules, and Imports endpoints all return 422 for cross-tenant mutations. Tests accept both 422 and 404 (`.Should().Contain()`). Deferred to a dedicated API contract standardization WI. (2026-05-27)
21. **F-026 (legacy Plan entity) reframed as intentional dual representation:** `Wasnie.Domain.Entities.Plan` (LegacyPlan alias in DbContext) is the EF persistence entity; `Wasnie.Domain.Compensation.Plans.Plan` is the DDD domain entity. Consolidation would require full EF model migration — not worth the cost now. NOT a duplicate to delete. (2026-05-27)
22. **Rate limit tests: 2 skipped intentionally:** Testing that limiter fires at low limits requires an isolated factory per test class with a small PermitLimit. Shared fixture with 10000 limit doesn't support this. Manual verification with curl/wrk required before first production customer. (2026-05-27)
27. **`IMoneyCriticalCommand` chosen for money-critical audit path (WI-P2-00, 2026-05-28):** Option A (marker interface extending `IAuditableCommand`) selected over Option B (flag on dispatcher). Rationale: visible at command definition site, consistent with existing `IAuditableCommand` pattern, avoids leaking the concept through `IAuditService`/`IAuditDispatcher` signatures. `AuditBehavior` wraps money-critical commands in `db.Database.BeginTransactionAsync()` — the handler's `SaveChangesAsync` and the dispatcher's `SaveChangesAsync` both participate in the same transaction; `CommitAsync()` commits both atomically. No message queue or external outbox required at current scale. `IApplicationDbContext` now exposes `DatabaseFacade Database { get; }` (consistent with F-001 deferral — EF Core already in Application). All Phase 2 Transactions/Payouts/Credits commands MUST implement `IMoneyCriticalCommand`. (2026-05-28)
28. **`Money` value object uses 4-decimal internal precision with banker's rounding (WI-P2-01, 2026-05-28):** All `Money` values are normalized to ≤4 decimal places at construction time via `Math.Round(amount, 4, MidpointRounding.ToEven)`. Every arithmetic method (Add, Subtract, Multiply, Divide, Negate, Abs) goes through the private constructor, so re-normalization is guaranteed. `ToString()` rounds the stored 4-decimal value to 2 decimal places for display only — it never mutates `Amount`. (2026-05-28)
30. **`CompensationTransactionStatus.Credited` replaced by full spec lifecycle (WI-P2-02, 2026-05-28):** `Credited` (a Credit entity was allocated) was not equivalent to spec's `Calculated` (calc engine ran). Since `CompensationTransactions` was a write-orphan with zero handlers and no stored data, a destructive replacement was safe per §8.4.1. New enum: `Pending=0, Eligible=1, Calculated=2, Paid=3, Cancelled=4`. `MarkCalculated` and `MarkPaid` are Phase 3 stubs that throw `NotSupportedException`. (2026-05-28)
31. **Idempotency for `CompensationTransaction`: filtered unique index on `(TenantId, Source, ExternalId)` WHERE `ExternalId IS NOT NULL` (WI-P2-02, 2026-05-28):** Manual transactions (null `ExternalId`) are exempt from external idempotency — the filter prevents false unique conflicts. Cross-tenant: same `(Source, ExternalId)` under different tenants is allowed (TenantId is in the key). The existing `(TenantId, ReferenceNumber)` unique index is preserved for internal reference uniqueness. (2026-05-28)
29. **`MoneyJsonConverter` in Infrastructure replaces `[JsonConstructor]` in Domain (WI-P2-01b, 2026-05-28):** Rule 1.5 (no serialization attributes in Domain) now fully compliant. `MoneyJsonConverter : JsonConverter<Money>` lives in `Wasnie.Infrastructure.Persistence.Serialization`. It reads both number and string JSON types for `amount` (backward compat), uses `OrdinalIgnoreCase` for property names, and wraps any `DomainException` from `Money.Of()` as a `JsonException` with the domain error as inner exception. Output format is identical to what `[JsonConstructor] + JsonSerializerDefaults.Web` produced — zero stored-data migration needed. Registered per EF config via `BuildJsonOptions()` factory (not a shared instance, to avoid cross-config pollution) and globally via `AddControllers().AddJsonOptions(...)` for the HTTP layer. (2026-05-28)
33. **`Transactions.Read` granted to TenantAdmin + CompManager only (WI-P2-03b, 2026-05-28):** Manager/Rep scoped access is deferred per decision #18. Granting globally now would be a security regression when scoping is added (reps would see all tenants' transactions instead of only their own). Grant widens when Manager/Rep scoped access WI is implemented. Sort-by-amount has no index — included in whitelist but a full scan at Enterprise scale (10k+ txns) may exceed the < 200ms P95 baseline. Defer adding `(TenantId, Amount)` index until customer data reaches Scale tier. (2026-05-28)
32. **`Permission.TransactionsCreate` granted to TenantAdmin + CompManager only (WI-P2-03a, 2026-05-28):** Manager and Rep cannot create transactions — consistent with all financial mutation operations across Phase 1 and Phase 2. `Permission.TransactionsRead` also added; currently not enforced on any GET endpoint (no GET endpoints yet), but reserved for WI-P2-03b so the grant/deny matrix is complete from inception. `IngestTransactionHandler` checks `Permission.TransactionsCreate` as the first operation before any DB access. (2026-05-28)
23. **TenantUserCorrelationEnricher placed in Wasnie.Api/Observability/:** Serilog package is only referenced in Wasnie.Api; adding it to Wasnie.Infrastructure would require a new package reference. Enricher registered in DI via `AddSingleton<ILogEventEnricher>(...)` and picked up by `ReadFrom.Services()`. (2026-05-27)
24. **Operational logging in handlers: zero `_logger.` calls in src/:** The audit trail (WI-08) covers all business events. Structured operational logging in individual handlers is a candidate for a future WI but is not blocking Phase 2. (2026-05-27)
25. **Serilog fully config-driven:** Hardcoded `.WriteTo.Console()` and `.WriteTo.File()` removed from `Program.cs`. All sinks, levels, and enrichers now read from `appsettings.json` / `appsettings.Production.json`. Adding Application Insights requires one sink entry in config — no code change. (2026-05-27)
26. **Phase C completed in one day instead of estimated 3-4 weeks:** Disciplined ARCHITECTURE.md + WI prompt workflow + Claude Code autonomous execution collapsed 50-70h estimate to ~10-12 effective hours. Zero regressions, only documented justified deviations. (2026-05-27)
34. **Real-data testing (2026-05-29) exposed retail SPM domain-model gaps.** A 3,183-row Reserved Polska / Galeria Katowice POS export surfaced multiple domain-model assumptions that hold for B2B-tech SPM but not for retail SPM, which is the actual target market: (a) `Payee.Email` required blocks seasonal/temporary workers with no corporate email; (b) `Payee.HireDate` required blocks migrating tenants who only have "period of plan activity," not historical hire date; (c) `CompensationTransaction.PayeeId` required blocks e-commerce / house-pool / system-processed-return rows. **WI-PROD-MODEL conversation is the next session's first topic.** Further import or transaction WIs must not proceed without resolving this — building on broken invariants multiplies rework. (2026-05-29)
35. **WI-PROD-MODEL design decisions — Part 1 (2026-05-30).** The Payee + Transaction domain model is being adjusted to fit the retail SPM target market (e.g. Reserved Polska, where store staff often lack corporate email and exact hire dates are unknown to the comp manager). Four firm decisions taken; remaining questions still open.

   **Decision A — Field-level requirement configuration system per tenant.** Wasnie will implement a tenant-level setting where the TenantAdmin marks specific fields as Required or Optional. Only the TenantAdmin can change these settings. Every change is recorded in the audit trail (Rule 5.1.5). Changing a setting does NOT invalidate existing data — only applies to future creations.

   **Decision B — Configurable fields list (initial scope of WI-PROD-A):** `Payee.Email`, `Payee.HireDate`, `Payee.Role`, `Payee.ManagerId`, `CompensationTransaction.PayeeId`. All five default to **Optional** for new tenants. Rationale: avoids the "valley of death" onboarding where a new comp manager hits a wall of required-field errors before they have configured anything. Admins can tighten later.

   **Decision C — Always-required fields (product law, not configurable):** `Payee.FullName`, `Payee.EmployeeCode`, `CompensationTransaction.ReferenceNumber`, `CompensationTransaction.Amount`, `CompensationTransaction.Currency`, `CompensationTransaction.TransactionDate`, `TenantId` on both. These are never presented as configurable in the settings UI.

   **Decision D — `CompensationTransaction.PayeeId` becomes nullable.** A transaction without an assigned payee is legitimate in the model (e-commerce self-service, system returns, sales pending later assignment). Users can assign a payee later. **Cross-phase dependency:** the Calculation Engine (Phase 3) MUST define its explicit policy on transactions with `PayeeId = null` (skip / house-pool / error). Engine design cannot defer this question — it must be decided as part of engine scoping.

   **Schema implications recorded for WI-PROD-A:** `Payee.Email`, `HireDate`, `Role`, `ManagerId` become nullable in the entity. The unique index on `(TenantId, Email)` becomes filtered: `WHERE Email IS NOT NULL` (same pattern as the existing `(TenantId, Source, ExternalId)` filtered index from WI-P2-02). `CompensationTransaction.PayeeId` becomes nullable; existing FK to Payee is preserved but allows null. Validation when value is present remains enforced (e.g. email must be a valid email format if provided; hire date must not be in the future if provided). (2026-05-30)
36. **WI-PROD-MODEL design decisions — Part 2 (2026-05-30).** Continuation of Part 1. Five firm decisions added to the model; three questions remain open (deferred to Part 3).

   **Decision E — User and Payee are separate but linkable entities.** Wasnie keeps a single identity system. `User` and `Payee` are distinct entities. A `User` MAY be linked to a `Payee` via a nullable `User.PayeeId`. Users without a Payee exist (e.g. CEO, finance manager, IT admin). Payees without a User are the common case — the vast majority of payees (store sales staff) will never have a login. The TenantAdmin invites users explicitly and assigns them permissions via the existing RBAC system. There is NO separate "rep portal" module; the same identity system serves all logged-in roles. Mass-onboarding of payees as users is explicitly out of scope. Sending invites by email is a deferred dependency (SendGrid or equivalent). For MVP, an admin can generate an invite link and share manually.

   **Decision F — `Payee.EmploymentType` added as a configurable optional field.** Values: full-time, part-time, temporary, contractor. Nullable. Joins the configurable-fields list (Decision B). Default Optional for new tenants. Used downstream by Phase 3 calculation rules that may treat employment categories differently.

   **Decision G — Payees are never deleted; activity state via `IsActive` + `DeactivatedAt`.** `Payee.IsActive` (boolean, default true) + `Payee.DeactivatedAt` (DateTimeOffset, nullable, set automatically when `IsActive` transitions to false). Inactive payees are preserved with full history; new transactions cannot be assigned to inactive payees. Re-activation clears `DeactivatedAt` and sets `IsActive = true`. All transitions audit-logged. Behavior on re-import when payee is inactive: OPEN — see Part 3.

   **Decision H — Location/CostCenter as an optional string dimension, NOT a rigid entity.** A `Payee.Location` (or `Payee.CostCenter` — final naming confirmed during implementation) optional string field on Payee, and likely on Transaction too (to be confirmed during WI-PROD-A scoping). NOT a separate `Store` entity. Rationale: rigid multi-tenant store hierarchy is overkill for boutique-style customers, but reporting and filtering by the dimension must work when the tenant uses the field. Sparse usage is fine. Joins the configurable-fields list as Optional.

   **Decision I — Tenant has an account currency; FX conversion is explicit and traceable.** The tenant has an "account currency" configured by the TenantAdmin (the single currency the company pays out in — e.g. Reserved Polska = PLN). Transactions are preserved in their native currency (Spec §5b.5 unchanged — no implicit conversion). For reports, payouts, and reconciliation in the account currency, the system performs **explicit** FX conversion using a traceable exchange-rate source. Both the original amount/currency and the converted amount/currency are preserved on the transaction record — never overwritten. This substantially expands the scope of WI-PROD-CURRENCY: (a) account-currency configuration field on Tenant; (b) exchange rate table (open sub-questions: rate source — ECB / fixer.io / manual entry; rate date — transaction date / period close / payout date; retroactive rate changes — disallowed vs. allowed with audit); (c) original-amount / converted-amount duality on every transaction (or equivalent representation preserving audit traceability); (d) conversion engine that respects §5b.5 (the conversion is the explicit module §5b.5 already permits). WI-PROD-CURRENCY is now a complete multi-currency handling system, not a styling fix. (2026-05-30)

37. **WI-PROD-MODEL design decisions — Part 3 (FINAL — closes the conversation, 2026-06-01).** The three remaining open questions from Parts 1 and 2 are fully resolved. WI-PROD-MODEL is now CLOSED. The 12 firm decisions (A–I from Parts 1+2; 10–12 from Part 3) constitute the complete retail-SPM domain model contract.

   **Decision 10 — Transaction status enum stays as-is; "Unassigned" is derived, not a status.** The `CompensationTransaction.Status` enum (`Pending=0, Eligible=1, Calculated=2, Paid=3, Cancelled=4`) stays unchanged. Default for new transactions (manual or import) remains `Pending`. The condition "transaction has no payee" is **derived** from `PayeeId IS NULL` — it is NOT a status value. The UI renders "Unassigned" when `PayeeId IS NULL` (already prepared defensively in WI-PROD-F). Status and assignment are two independent dimensions; filters and queries treat them separately. The Calculation Engine (Phase 3) will filter by `Status = 'Pending' AND PayeeId IS NOT NULL` to process only what is processable. (2026-06-01)

   **Decision 11 — Payee assignment and reassignment as distinct money-critical commands.** Two separate commands, both implementing `IMoneyCriticalCommand` and both audit-logged automatically through `AuditBehavior` (no new audit infrastructure needed):

   - **`AssignPayeeCommand`** — assigns a payee to a transaction where `PayeeId IS NULL`. No reason required. Allowed for CompManager and TenantAdmin. Audit log records the standard event (actor, timestamp, target payee). User MAY add a comment but it is not required.
   - **`ReassignPayeeCommand`** — changes a transaction's payee from A to B. Reason field is REQUIRED (minimum 10 characters). Allowed for CompManager and TenantAdmin. Audit log records the event WITH the reason persisted.

   State machine rules for both operations:
   - `Pending`: both allowed.
   - `Eligible`: both allowed; reassignment causes return to `Pending` for re-evaluation.
   - `Calculated`: reassignment allowed, but **invalidates the calculated commission line** (marks it obsolete) and the transaction returns to `Pending` for recalculation.
   - `Paid`: **BLOCKED**. The money already left the bank; corrections to a Paid transaction are an accounting problem, not a DB-field problem. Backend MUST throw a domain exception on attempted reassignment of a Paid transaction. Frontend MUST disable the reassign action for Paid rows.
   - `Cancelled`: reassignment allowed (administrative closure).

   (2026-06-01)

   **Decision 12 — Import against an inactive payee: accept with warning.** When a CSV/XLSX import row's `EmployeeCode` matches a payee whose `IsActive` is false, the validator emits `IssueSeverity.Warning` with message `"Payee X (code Y) is inactive — assignment will be historical"`. Rows are imported and assigned to the inactive payee. Rationale: historical assignments are a legitimate retail scenario — a transaction from April 28 can legitimately arrive in the May 5 import even if the payee was deactivated on April 30. The wizard's existing `skipRowsWithWarnings` toggle continues to work; the comp manager can exclude them if desired. (2026-06-01)

38. **WI-PROD-A.2 Done — Catalog extended to 6 configurable Payee fields (2026-06-01).** `EmploymentType` (enum: FullTime, PartTime, Temporary, Contractor) and `Location` (nullable string, max 200 chars) added to the Payee entity as nullable. `Role` and `ManagerId` pre-existed as entity fields but are now also represented in the `FieldRequirementSettings` catalog. All four new entries default Optional for both existing and new tenants — no existing data invalidated, no validation errors on records that omit these fields.

   Implementation completed across two sessions: the first session (crashed mid-flight due to Claude Code's 1M-context billing limit) delivered the full Application layer (entity, enum, DTO, commands, handlers, validators, constants). The second session resumed cleanly via a continuation prompt and added the EF migration, import service extensions, frontend form fields, i18n keys, and tests — without redoing any prior work. Smoke-tested in vivo with real tenant data: Settings UI renders six rows; Payee edit form accepts and persists both new fields correctly.

   Test count: 595 → 628 (+33 — 7 domain unit, 10 validator unit, 13 import integration, 3 additional). Build clean.

   **Architectural win:** A.1's data-driven Settings UI (renders one row per `FieldRequirementSetting` returned by the API) absorbed the four new catalog entries automatically. Zero UI template changes were needed — only new i18n keys for the field labels. This confirms the data-driven approach: future catalog additions follow the same pattern (constant in `PayeeFieldNames` + seed row in migration + i18n key).

   **Implements:** Decision B (extended to 6 fields), Decision F (EmploymentType), Decision H (Location as optional string dimension, named `Location` not `CostCenter`). (2026-06-01)

40. **WI-PROD-A.3 Done — Payee lifecycle + nullable PayeeId + Assign/Reassign commands (2026-06-01).** Three capabilities delivered:

   **Capability 1 (Payee lifecycle, Decision G):** `Payee.IsActive` (bool, default true) + `Payee.DeactivatedAt` (DateTimeOffset?). `Payee.Deactivate()` / `Payee.Activate()` domain methods. New commands `DeactivatePayeeCommand` and `ActivatePayeeCommand` (both `IAuditableCommand`, NOT `IMoneyCriticalCommand` — correct, as these are admin operations not money mutations). Permission: new `Payees.Deactivate` (separate from `Payees.Terminate`, consistent with existing finer-grain pattern). Granted to TenantAdmin + CompManager.

   **Capability 2 (nullable PayeeId, Decisions D + 12):** `CompensationTransaction.PayeeId` → `Guid?`. `Transaction.PayeeId` catalog entry added (default Optional for all tenants, including existing ones). `TransactionImportValidationService` extended: blank payeeCode accepted when Optional; inactive payee match emits `IssueSeverity.Warning` with `IssueCategory.Reference` and message "Payee X is inactive — assignment will be historical". `TransactionImportJobHandler` extended to pass `null` payeeId when payeeCode is blank and validation passed. `IngestTransactionHandler` extended with `IFieldRequirementService` check for PayeeId optionality; blocks manual entry to inactive payees.

   **Capability 3 (Assign/Reassign, Decision 11):** `AssignPayeeCommand` + `ReassignPayeeCommand`, both `IMoneyCriticalCommand`. State machine enforced in domain: Paid → `DomainException`; Eligible/Calculated → revert to `Pending`; Cancelled → allowed. Reason required on Reassign (≥ 10 chars, trimmed) — **hardcoded in the domain layer, NOT configurable via `FieldRequirementSettings`**. Making it optional would weaken the audit trail that distinguishes Wasnie from Excel; this was a deliberate architectural decision. Reason persisted in the audit event payload. Endpoints: `POST /api/transactions/{id}/assign-payee` and `/reassign-payee`. 409 Conflict for state-rule violations. Smoke test confirmed reason is enforced across BOTH states of the `Transaction.PayeeId` toggle — the two settings are orthogonal and independent.

   **Frontend:** Payee list — Deactivate/Activate row actions + "Inactive" badge (WsBadge warning). Transaction list — Assign button (unassigned rows), Reassign button (assigned non-Paid rows), disabled Reassign with tooltip for Paid. Assign modal (payee picker + optional comment). Reassign modal (payee picker + required reason with min-10 validation). EN/ES/PL complete.

   **Test count: 644 → 692 backend (+48); 143 frontend (unchanged).** Migration `P2_PayeeLifecycle` applied. Build clean. WI-PROD-A trilogy complete. WI-PROD-MODEL decisions A–I + 10–12 fully realized in code.

41. **WI-PROD-A trilogy + WI-PROD-MODEL milestone — fully realized in code (2026-06-01).** WI-PROD-A.1 (Email + HireDate optional + `FieldRequirementSettings` system), WI-PROD-A.2 (catalog extended to 6 configurable Payee fields: Role, ManagerId, EmploymentType, Location), and WI-PROD-A.3 (Payee lifecycle + nullable PayeeId + Assign/Reassign commands) are all Done and smoke-tested in vivo. Combined backend test growth across the trilogy: 561 → 692 (+131). All 12 WI-PROD-MODEL decisions (A–I + 10–12) implemented and smoke-tested with real Reserved Polska retail data across three coexisting stores (Galeria Katowice EMP001–EMP008, Galeria Mokotów EMP201–EMP209, Silesia City Center EMP301–EMP310; 26 payees, ~12,500 transactions plus today's smoke-test additions). Specific validations in today's session: EMP310 deactivated → "Inactive" badge ✓; re-import against EMP310 → Warning with Reference category ✓; 8 Unassigned transactions (PayeeId null) imported and listed ✓; AssignPayeeCommand on Unassigned → EMP301 assigned ✓; ReassignPayeeCommand rejected empty reason ✓, rejected <10 chars ✓, accepted 40+ chars with EMP302 ✓; Settings shows 7 catalog rows (Transaction.PayeeId Optional) ✓. Mid-market retail SPM model contract complete. Phase 3 (Calculation Engine) is now unblocked — the data model it will consume has stabilized. (2026-06-01)

42. **Architectural observations from the WI-PROD-A trilogy (2026-06-01).**

   **Data-driven Settings UI validated at scale:** The Settings UI introduced in A.1 renders one row per `FieldRequirementSetting` returned by the API. Across A.1 (2 entries), A.2 (+4 entries), and A.3 (+1 entry: Transaction.PayeeId), the catalog grew from 2 → 7 entries with zero template edits on each addition. Only i18n keys and migration seed rows were needed. The mechanism validated itself across three WIs.

   **State machine ownership:** The Assign/Reassign state rules (Paid=blocked, Calculated/Eligible=revert to Pending, Cancelled=allowed) live in `CompensationTransaction.Assign()` and `.Reassign()` domain methods, not in application handlers. Handlers call the domain method and catch `DomainException` — they never duplicate the rules. This is the pattern: business invariants in the domain, coordination in the application layer.

   **Audit trail as a product differentiator:** The reason-required-on-reassign rule is hardcoded in `CompensationTransaction.Reassign()`. It was NOT added to the `FieldRequirementSettings` catalog despite requests to make it configurable. Rationale: the ability to trace WHY a transaction was reassigned (money moved from payee A to B) is a core product promise — it's what makes Wasnie's audit trail more reliable than a spreadsheet. Making it opt-out would undermine the compliance value proposition. Any future request to make the reason optional should be escalated as a product-level decision, not implemented as a settings toggle.

   **`ws-select` in modals — portal pattern:** When `ws-select` is used inside a modal, its dropdown now uses `position: fixed` with coordinates from `getBoundingClientRect()`. This is the canonical solution for dropdowns in overflow-constrained containers, used by all major UI frameworks. The component detects `.ws-modal__dialog` and switches modes automatically — no consumer configuration required.

39. **Threat-model snapshot — upload security (2026-06-01).** Current state assessed: ClosedXML parsing rejects malformed OOXML; 5 MB file-size limit enforced before the parser is invoked; macros never executed (ClosedXML reads cells only); uploaded files are NOT persisted to disk and NOT served back to other users; tenant isolation enforced by the global EF query filter downstream. The surface area for malware propagation is therefore narrow.

   Documented gaps as of today: no magic-byte / file-signature validation; no per-user upload rate limiting beyond the global rate limiter; no exhaustive structured upload logging (actor, hash, detected MIME); no antivirus scanning; no internal security-documentation page for customer IT reviews.

   Risk assessment: **LOW** at current stage (pre-revenue, controlled test tenants). Becomes **MEDIUM** when the first paying customer's IT-security questionnaire is conducted — mid-market retail customers rarely require AV scanning, but any financial-services or healthcare vertical will. WI-PROD-N closes the defensive baseline (magic-byte check, per-user upload rate limit, structured upload logging, internal security doc) before signing the first customer. WI-PROD-O adds active antivirus scanning only when a customer contract explicitly requires it — do not implement speculatively. (2026-06-01)

50. **WI-CALC-A.0 Done — Schema preparation for Phase 3 Calculation Engine (2026-06-02).** Four new nullable columns added across three domain entities, plus a new enum. All additions are purely additive (nullable, backward-compatible); no existing data is affected; no behavior changes; all 704 non-skipped tests continue to pass.

   **Entity changes:** `Rule` gains `EffectivePeriod` (`DateRange?`, nullable owned type mapping to `EffectivePeriodStart`/`EffectivePeriodEnd` date columns) and `Tag` (`string?`, `nvarchar(50)` nullable). `Plan` gains `PeriodType` (`PlanPeriodType?`, mapped as `nvarchar(50)` nullable following the same `HasConversion<string>()` convention as `PlanStatus`; `CloneAsNewVersion` copies it to the clone). `Credit` gains `SupersededAt` (`DateTimeOffset?`) and `SupersededBy` (`string?`, `nvarchar(max)`). Tag validation: throws `DomainException("Rule tag must not exceed 50 characters.")` when tag (if non-null) is longer than 50 chars after trimming.

   **New enum:** `PlanPeriodType` (7 values: `Monthly=0, Quarterly=1, Annual=2, Semestral=3, Weekly=4, Biweekly=5, Custom=6`) in `Wasnie.Domain.Compensation.Plans`. Metadata only — the calculation engine does not consume it (Decision #42).

   **EF config:** `PlanRuleConfiguration` uses `OwnsOne(r => r.EffectivePeriod, ...)` + `Navigation(...).IsRequired(false)` for the nullable owned type (the correct EF Core 8 pattern — `DateOnly` is a struct so `.IsRequired(false)` on sub-properties is forbidden; nullability is expressed on the navigation). `CreditConfiguration` adds a filtered index `IX_Credits_TenantId_SupersededAt WHERE [SupersededAt] IS NULL` for the high-frequency "active credits" query pattern (Decision #46).

   **Migration:** `20260602082902_P3_SchemaPreparation` applied to local dev DB.

   **Binding rule added to `14-forbidden-patterns.md`:** nullable owned `DateRange?` requires `Navigation(...).IsRequired(false)` — do NOT call `.IsRequired(false)` on the `DateOnly` sub-properties.

   **Tests:** +12 (8 unit in `PlanTests.cs` + `CreditTests.cs`; 4 integration in `P3SchemaRoundTripTests.cs`). 692 → 704 non-skipped tests. (2026-06-02)

51. **WI-CALC-A.1 Done — Credit Engine V1: Credits created at ingest with RuleSnapshot frozen; Transaction.Status Pending→Calculated (2026-06-02).**

   **Core behavior:** `CreditAllocationService.AllocateAsync(transaction, ct)` runs inside every transaction ingest path (Hangfire import job + manual `IngestTransactionHandler`). For each `Pending` transaction with a non-null `PayeeId`:
   - Finds the active `PlanAssignment` covering `TransactionDate` (Decision #40).
   - Loads the `Plan` with rules eagerly (`Include(p => p.Rules)`).
   - Filters rules by `IsActive` and `EffectivePeriod` containment (Decision #41 runtime).
   - Evaluates each rule's `Trigger` (Trigger.Always, or condition set with And/Or logic).
   - Computes commission: Flat / Tiered / AttainmentBased (V1 stub: attainment=100%; TODO WI-CALC-A.2).
   - Applies Modifier (multiplicative), Cap (PerTransaction only), Floor.
   - Freezes `RuleSnapshot` and calls `Credit.Allocate(...)` with Role=Primary, SplitPercentage=1.0 (Decision #44).
   - Returns credits to caller; caller persists and calls `transaction.MarkCalculated(...)`.
   - No assignment / no active rules / trigger false → empty list, transaction stays Pending (not an error).
   - Plan/transaction currency mismatch → `DomainException` (WI-PROD-CURRENCY deferred).
   - Tenant mismatch → `InvalidOperationException` (data integrity guard).

   **Three bugs discovered and fixed during this WI:**
   1. `Entity.operator ==` and `ValueObject.operator ==` return `false` when BOTH operands are null — `if (x == null)` does NOT guard against null for domain types. Fixed throughout service with `is null` / `is not null`. **New binding rule added to `14-forbidden-patterns.md`.**
   2. `RateTable.Tiered` validation used `>=` boundary check, rejecting adjacent tiers where `To[i] == From[i+1]`. Fixed to `>` (strict overlap only). Standard adjacent tier layout (e.g. `[0-500)` and `[500-∞)`) now works.
   3. `CreditConfiguration.RuleSnapshot` stored as JSON but `RuleSnapshot` has only a private constructor — no parameterless constructor for System.Text.Json to use. Added `RuleSnapshotJsonConverter` in `Wasnie.Infrastructure.Persistence.Serialization`; `CreditConfiguration` now uses `BuildJsonOptions()` with both `MoneyJsonConverter` and `RuleSnapshotJsonConverter`.

   **AttainmentBased V1 stub:** `ComputeAttainmentCommission` uses `attainmentPct=1.0m` (100% fixed). WI-CALC-A.3 must replace this with `IQuotaAttainmentService`.

   **Tests:** +27 (5 unit `CompensationTransactionTests`, 16 integration `CreditAllocationServiceTests`, 1 integration `TransactionImportJobTests.WithPlanAndAssignment`, 1 integration `TransactionsEndpointsTests.Post_WithPlanAndAssignment`, plus new domain event test coverage). 704 → 731 non-skipped tests. (2026-06-02)

52. **WI-CALC-A.2 Done — Credit superseding on reassign; Decision #46 Case A (2026-06-02).** Orphaned-Credit bug fixed. When a Calculated transaction is reassigned, all non-superseded Credits for that transaction are marked superseded and the Credit Engine immediately re-allocates for the new payee. Sub-WI numbering shifted: original A.2 (quota attainment) → A.3. Decision #46 Cases B, C, D deferred (Payouts not built yet). Tests: +12 (6 unit `CreditTests`, 4 integration `CreditSupersedeIntegrationTests`). 731 → 743 non-skipped. (2026-06-02)

53. **Decision #53 — WI-CALC-MODEL Part 1.5: Validation issue on transactions without payee at import (2026-06-02).** When the import wizard processes a CSV row that has no Staff ID (and the tenant's `Transaction.PayeeId` setting is `Optional`, per Decision 10 of WI-PROD-MODEL), the system emits a `ValidationIssue` for that row using the existing IssueCategory infrastructure from WI-PROD-E. The issue appears inline in the wizard's validation table, alongside any other format/reference/required issues from the same import. The issue: Category: `IssueCategory.Required` (or a new category if a distinct visual treatment is preferred — to be decided in the implementation WI, not here). Message: clear, contextual — e.g. "No Staff ID provided — this transaction will be imported as Unassigned and requires manual assignment to a payee later for commission calculation." Severity: warning, not error — the row is still importable. The comp manager sees ALL such warnings inline in the wizard's existing validation table (same UX as Format / Reference / Required warnings from WI-PROD-E). The comp manager decides: Cancel the import and upload a corrected CSV. Continue with the import; transactions without Staff ID enter as `Unassigned` (`PayeeId = null`). No threshold, no modal, no separate alert mechanism. The existing validation table is the only surface. The comp manager has full visibility and decides based on what they see, not based on a system-imposed threshold. Coherent with Decision 10 of WI-PROD-MODEL: `Transaction.PayeeId Optional` remains valid for legitimately unassigned transactions (returns, anonymous sales, online sales without staff). This decision adds visibility, not enforcement. (2026-06-02)

54. **Decision #54 — WI-CALC-MODEL Part 1.5: Manual "Procesar Pending" button to trigger Credit allocation on existing Pending transactions (2026-06-02).** The Credit Engine (WI-CALC-A.1) runs continuously during ingest. Transactions ingested with `PayeeId` and an active `PlanAssignment` covering the `TransactionDate` immediately become Calculated. Transactions that fall outside this happy path — either because they had no PayeeId at ingest (Unassigned, later assigned), or because no PlanAssignment was configured at ingest time — remain Pending. These accumulate when clients upload CSVs before configuring plans, or when payees are assigned to plans after import. The system does NOT process these retroactively without explicit user action. No auto-backfill on PlanAssignment creation. No background job runs without trigger. Instead, the comp manager triggers backfill manually via a "Procesar Pending" button visible in surfaces where it is contextually relevant: the PlanAssignment detail page (processes that payee's Pending in the assignment's period); the Plan detail page (processes all assigned payees' Pending in the plan's period — for bulk); the transactions list when filtered by payee + period (processes the filtered subset). Above the button, the system shows an informative badge: "X transactions Pending elegibles para procesamiento". Click dispatches a Hangfire job `ProcessPendingTransactionsJob` with three technical specifications: (1) **Chunking obligatorio.** Process in chunks of 50-100 (mirroring TransactionImportJobHandler from Phase 2). Each chunk in its own DB transaction with its own commit. No single transaction processes more than one chunk. Idempotency: Credit's `(TransactionId, RuleId, PayeeId)` is unique; retried chunks finding existing Credits skip silently. (2) **Volume awareness UI.** When candidate query returns >5,000 transactions, show informative message: "Procesando N transactions, tiempo estimado ~M minutos. Puedes seguir trabajando." Threshold hardcoded at 5,000 for V1; configurable per tenant in the future. Estimate formula: `total / 100 = chunks × 3 seconds per chunk`. (3) **Cancelable.** The job MUST be cancelable from the UI. Hangfire's cancellation token is honored at chunk boundary — current chunk completes (avoiding partial-chunk corruption), then job stops cleanly. Already-committed chunks remain; remaining transactions stay Pending. Cancellation audit-logged with actor and timestamp. Skipping rule (consistent with Decision #46 Case B): the job skips transactions that already have non-superseded Credits from ANOTHER Plan whose `EffectivePeriod` overlaps. The job does NOT automatically supersede them. If the comp manager wants to replace an old plan's calculations, they must explicitly trigger a separate "Recalculate transactions for this payee" operation (future feature; out of A.2.5 scope). Audit trail: each processing run records trigger (user action), actor, timestamp, scope, processed count, created count, skipped count. The Credit Engine becomes reachable via three paths: (1) Synchronously during transaction ingest (WI-CALC-A.1). (2) Synchronously during reassign (WI-CALC-A.2). (3) Asynchronously via manual "Procesar Pending" trigger (WI-CALC-A.2.5, this decision). All paths converge on the same `ICreditAllocationService.AllocateAsync` contract — no duplication of allocation logic. No automatic trigger. This is the firm choice: control over convenience. Coherent with the project's broader principle that retroactive changes require explicit user confirmation. (2026-06-02)

55. **Decision #55 — WI-CALC-MODEL Part 1, Decision 1: One active PlanAssignment per payee per period; Rule.Tag for grouping (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* A Payee can have only one `PlanAssignment` whose `EffectivePeriod` overlaps with any other active `PlanAssignment` for the same Payee. The domain enforces this invariant in the `PlanAssignment` factory / creation command: if the Payee already has an active `PlanAssignment` whose `EffectivePeriod` overlaps with the new one, creation is rejected with a clear `DomainException`. To change a Payee's plan, the TenantAdmin deactivates the current `PlanAssignment` (the `Deactivate()` method already exists) and creates a new one. The transition is audit-logged automatically by existing `AuditBehavior` infrastructure. `Rule` gains an optional `Tag` property (string, nullable, max 50 characters). The Tag is not enforced — it is a free label that allows logical grouping of rules across plans (e.g. "Promo Spring 2026", "Base", "Q3 Campaign"). Used for reporting and bulk operations later. Tag is metadata only; the engine does not consume it for calculation. Open for later: aggregations across payees (e.g. "team bonus when store revenue > 500k"). This requires Triggers to support cross-payee aggregations, which is outside the single-payee plan model. Deferred to a future phase. Architectural implication: the join in the Credit Engine simplifies to `Transaction → PlanAssignment (single active, period contains TransactionDate) → Plan → Rules`. No disambiguation logic needed. One transaction → one Plan (or none, if Payee has no active assignment in that period). (2026-06-02)

56. **Decision #56 — WI-CALC-MODEL Part 1, Decision 2: Rule.EffectivePeriod for sub-plan temporal scoping (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* The `Rule` entity gains property `EffectivePeriod` of type `DateRange?` (nullable). Semantics: If null → the rule applies during the entire `Plan.EffectivePeriod` (preserves existing behavior; existing rules migrate with null). If present → the rule applies only within the specified date range. **Invariant A (containment):** If `Rule.EffectivePeriod` is set, it must be fully contained in `Plan.EffectivePeriod`. Validated in `Plan.AddRule(...)` and `Plan.UpdateRule(...)`. If a rule range falls outside the plan range, the operation is rejected with a `DomainException` whose message mentions both ranges (e.g. "Rule 'Spring Promo' period (2026-04-20 to 2026-08-15) is not within Plan period (2026-04-01 to 2026-06-30)"). **Invariant B (bidirectional):** When updating `Plan.EffectivePeriod` (in Draft state), the domain validates that no active rule with a specific `EffectivePeriod` would fall outside the new plan range. If any rule would be invalidated, the plan update is rejected with a clear message. **Invariant C (Draft only):** Both invariants apply only when `Plan.Status == Draft`. Once a plan is Active, the period is frozen — for changes, the existing `CloneAsNewVersion` pattern is used. The generic `Trigger.Condition` system (Field/Operator/Value with ConditionValueType.Date) remains intact for non-temporal scoping (amount thresholds, category matches, etc.). It is NOT used for date ranges going forward — `Rule.EffectivePeriod` is the first-class concept for "this rule applies during these dates". UX implication: the Rule form gains an optional date-range picker labeled "Applies only during (optional):". When empty, hint displays "During the entire plan period." Architectural rationale: date-range scoping at the rule level is needed to model real retail patterns — seasonal promotions, "3x1 campaigns", limited-time bonuses — without requiring multi-plan assignment per Payee. This decision enables Decision #55 (one plan per Payee) to remain practical: campaigns live as time-scoped rules inside the Payee's single plan, not as separate Plans. (2026-06-02)

57. **Decision #57 — WI-CALC-MODEL Part 1, Decision 3: PlanPeriodType as semantic label, not engine behavior (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* New enum `PlanPeriodType` with seven values: `Monthly`, `Quarterly`, `Annual`, `Semestral`, `Weekly`, `Biweekly`, `Custom`. New nullable property on `Plan`: `PlanPeriodType? PeriodType`. Interpretation chosen: the enum is metadata for reporting, UI, and UX. The calculation engine does NOT consume it for scheduling, period closing, or aggregation. The temporal contract remains `Plan.EffectivePeriod` (DateRange). Existing plans migrate with `PeriodType = null` — UI renders as "Custom" with no functional change. Future possibility (NOT decided today): if Phase 3 V2 introduces automatic period-close scheduling, `PeriodType` may be elevated to behavior. Today it does not commit to that. (2026-06-02)

58. **Decision #58 — WI-CALC-MODEL Part 1, Decision 4: Quota.Period must be contained in Plan.EffectivePeriod (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* `Quota.Period` must be fully contained in the `Plan.EffectivePeriod` of the Plan it belongs to. Validated in `Quota.Create(...)` and `Quota.UpdateDraft(...)`. If the invariant fails, a `DomainException` is thrown with a contextual message citing both ranges. **Bidirectional:** when updating `Plan.EffectivePeriod` (in Draft state), the domain validates that no active Quota would have its period orphaned outside the new range. If any would be orphaned, the update is rejected. **Draft-only enforcement** — consistent with Decision #56. **Permissive migration:** existing Quotas with inconsistent periods are NOT rejected at migration time; legacy data tolerated. Gradual cure: validation kicks in on the next `UpdateDraft`, forcing the user to correct the period before saving. Conceptual coherence: Plan is the temporal container for Rules (Decision #56) and Quotas (this decision). All temporal child entities are constrained to the plan's range. (2026-06-02)

59. **Decision #59 — WI-CALC-MODEL Part 1, Decision 5: Phase 3 V1 emits only Primary credits; Splits and Overlays deferred to V2 (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* Phase 3 V1 of the Calculation Engine emits Credits only with `Role = Primary` and `SplitPercentage = 1.0`. One transaction generates Credits only for the Payee assigned by its active `PlanAssignment` (single-plan-per-Payee per Decision #55). The Credit model retains intact support for `CreditRole.Overlay`, `CreditRole.Split`, and `SplitPercentage < 1.0`. The V1 engine does not produce them, but the model is ready. Phase 3 V2 (future) will enable Splits and Overlays without model redesign — only extending the engine logic to produce them. When the first real customer requires these features (likely in retail: store manager earning overlay on team sales), the feature is activated. (2026-06-02)

60. **Decision #60 — WI-CALC-MODEL Part 1, Decision 6: Hybrid calculation trigger; manual Payouts in V1 (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* Two distinct engines with different granularities: **Credit Engine (continuous):** when a `CompensationTransaction` is ingested (manual entry or import), the engine evaluates the Rules of the Payee's active Plan and creates one `Credit` per firing Rule. Runs synchronously in the ingest pipeline or inside the Hangfire import job. `CompensationTransaction.Status` transitions Pending → Calculated when its Credit is created. **Payout Engine (manual, monthly):** `PayoutLine` and `CompensationPayout` are NOT maintained live. They are computed when the comp manager clicks "Calculate period" on the Payouts screen, dispatching `CalculatePayoutsForPeriodCommand(tenantId, planId, period)` as a Hangfire job. The job aggregates Credits → PayoutLines → CompensationPayouts. Job state visible via polling (same pattern as the import wizard). Each calculation is audit-logged (actor, timestamp, period, payout count). Automatic scheduling deferred to V2. An optional Hangfire recurring job per tenant can be added when Wasnie has stable production customers. All triggering is manual in V1. (2026-06-02)

61. **Decision #61 — WI-CALC-MODEL Part 1, Decision 7: Retroactive recalculation via superseding and manual signal (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* Credits are immutable. Changes produce new Credits that supersede earlier ones. The comp manager decides when to recalculate; the system only signals. **Model changes:** `Credit` gains `SupersededAt` (DateTimeOffset, nullable) and `SupersededBy` (string, nullable). Superseded Credits remain in the DB; they are excluded by `WHERE SupersededAt IS NULL` in all calculations. A "recalculate suggested" signal on a Payout is derived by query — no new fields on `CompensationPayout`. **Four covered cases:** Case A — Transaction reassigned (Calculated status): prior payee's Credit marked superseded; new Credit created for the new payee with current RuleSnapshot. Case B — Reassign when Payout is Calculated/Approved: Payout NOT modified automatically. UI signals "pending changes — recalculate?". If chosen, existing Payout is marked superseded and a new one created. If Payout is Paid, recalculation is blocked. Case C — Plan updated after calculation: existing Payouts NOT recalculated automatically. Frozen RuleSnapshot protects historical calculation. Manual decision of the comp manager. Case D — Transaction cancelled: associated Credit marked superseded; same manual-signal flow. Blocked if Payout is Paid. Comp manager retains full control and traceability. Nothing changes behind their back after approval. Note: WI-CALC-A.2 implemented Case A on 2026-06-02. Cases B, C, D remain pending until their respective feature flows exist. (2026-06-02)

62. **Decision #62 — WI-CALC-MODEL Part 1, Decision 8: Period assignment by TransactionDate (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* The period a transaction belongs to is determined strictly by `CompensationTransaction.TransactionDate`. The engine aggregates Credits using `WHERE TransactionDate >= Period.Start AND TransactionDate <= Period.End AND SupersededAt IS NULL`. `IngestedAt` is operational metadata only — it does NOT participate in period assignment. Late-ingested transactions (e.g. April CSV with March sales) are correctly assigned to March. If the March Payout was already calculated, Decision #61 applies: a new Credit is created and the comp manager sees the recalculation signal. The RuleSnapshot is frozen with Rules active at `TransactionDate` — consistent with Decision #56 (`Rule.EffectivePeriod`). (2026-06-02)

63. **Decision #63 — WI-CALC-MODEL Part 1, Decision 9: Quota attainment via Domain Service with per-request cache (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* **Domain Service:** `IQuotaAttainmentService` in the Domain layer, with primary method: `QuotaAttainment ComputeAttainment(Quota quota, IEnumerable<Credit> credits)`. The service interprets `MeasurementType` (Revenue → sum `OriginalAmount`, Units → sum quantities, Margin → formula, etc.). Attainment semantics live in the domain because they depend on the model. **Per-request cache** in the implementation — same pattern as `IFieldRequirementService` from WI-PROD-A.1: lazy-load, scoped, computes the first time and reuses within the request. No snapshot table, no global cache invalidation. **Value Object `QuotaAttainment`** encapsulates: `AttainedAmount` (Money) — the total measured so far; `AttainmentPercentage` — can exceed 100% (overachievement); range 0–2.0+ (requires new VO `AttainmentPercentage` because the existing `Percentage` VO constrains to 0–1.0); `QuotaAmount` (Money) — the original target; `MeasurementType` (enum) — what was measured; `Period` (DateRange) — the period evaluated; `ComputedAt` (DateTimeOffset) — when this snapshot was computed. **Consumers:** Credit Engine (when a Rule uses `RateTable.AttainmentBased`, the engine asks running attainment before applying the rate); Quota UI / Payee Dashboard (displays "X is at 76% of their target"); Reporting / Payout view (includes attainment as context). **Computation filter:** the service considers only Credits that (a) reference the same `PlanId` as the Quota, (b) whose `TransactionDate` falls in `Quota.Period`, and (c) whose `SupersededAt IS NULL`. The `SupersededAt` filter connects to Decision #61 — attainment reflects current reality, not historical. (2026-06-02)

66. **Decision #66 — Pay Run model (A.6) approved; docs/Pay_Run_Model.md is the design reference (2026-06-09).** Six decisions closed during the design session: (1) Approve/Pay actions are at the PayRun level with drill-down to individual payouts. (2) One PayRun per period supports multi-currency with per-currency roll-ups. (3) Reopen is only allowed for the full run (Approved→Draft); individual payouts cannot be reopened independently. (4) Lock state fused into Paid — no separate intermediate Lock state. (5) Zero-amount payouts are generated but hidden by default with a toggle; `PayeeCount` and `PaidPayeeCount` are separate counters. (6) Clawbacks are a separate WI post-A.6 — not in V1 scope. UI navigation: master→detail in separate pages (see Decision #72). Implementation starts as WI-CALC-A.6 after Step 0 read-only + reconciliation against Product Master Spec. (2026-06-09)

67. **Decision #67 — `Money.Zero` must never use a hardcoded currency; `fallbackCurrency` is a required parameter (2026-06-09).** `CompensationPayout.Calculate()` had `Money.Zero("USD")` as a hardcoded temporary default, causing all $0 payouts to be saved in USD regardless of plan currency. Fix: `fallbackCurrency` is now required. If blank or empty, `DomainException` is thrown. No silent default is permitted. Rule: every call to `Money.Zero(currency)` must pass the currency derived from the plan or the domain context — never a string literal. (2026-06-09)

68. **Decision #68 — `CurrencyFormatPipe` uses CLDR native fraction digits; do NOT hardcode `minimumFractionDigits` (2026-06-09).** Removed `minimumFractionDigits: 0` and `maximumFractionDigits: 2` overrides. `Intl.NumberFormat` with `style:'currency'` uses the CLDR standard per currency — EUR/USD/PLN→2 decimals, JPY→0. Hardcoding `minimumFractionDigits: 2` would break JPY and other zero-decimal currencies. The pipe is the single formatting surface; all monetary display in the UI must go through it. Three new regression tests added (trailing-zero, always-2-decimal, JPY 0-decimal). (2026-06-09)

69. **Decision #69 — Bulk confirmation modals for irreversible actions require 5 mandatory elements; reversible actions omit the irreversibility warning (2026-06-09).** `BulkMarkPaid` modal: (1) explicit payee count in title + body; (2) per-currency totals grouped by currency; (3) scrollable payee list with clickable names → /payees/:id new tab; (4) irreversibility warning (mark-paid is permanent); (5) skip warning when non-Approved payouts are selected. `BulkApprove` modal: OMITS element (4) — Approved→Open reopen is possible; no irreversibility warning. This distinction must be preserved in all future bulk-action modals: reversible actions never get element (4). (2026-06-09)

70. **Decision #70 — Quotas are NOT required for flat-rate commission; only required for AccelerationBased/AttainmentBased rate tables (2026-06-09).** A payee can receive flat-rate commission with no Quota configured. Quotas are only required when the plan uses `AttainmentBased` rate tables (which need a target to compute attainment %). Any future validation that blocks payout calculation for missing quotas must be scoped to `AttainmentBased` plans only. (2026-06-09)

71. **Decision #71 — Payouts Excel export (`GET /api/payouts/export`) is the list-view export, NOT the aggregated payroll export (2026-06-09).** The endpoint exports the currently-filtered payout list — one row per payout, same filter predicates as `ListPayoutsHandler`, no pagination, 50k cap. A future "payroll export" (aggregated by payee for payroll system integration, payslips) is a separate WI post-A.6. The two are distinct in purpose: list export = visibility and audit; payroll export = operational output. Do not conflate them. (2026-06-09)

72. **Decision #72 — Pay Run UI navigation is master→detail in separate pages, NOT an expandable tree (2026-06-09).** The Pay Run list page links to a PayRun detail page (master→detail pattern, matching Payee/Plan/Assignment). No collapsible tree widget. Rationale: separate pages are deep-linkable, follow browser history naturally, and match the existing navigation paradigm. Expandable trees become unmanageable with many payees per run and add complex state management. (2026-06-09)

73. **Decision #73 — UI quality method: study and replicate existing canonical sections before building new UI (2026-06-09).** Before building any new UI feature, identify the canonical reference section (Payees for forms, Transactions for export button placement, Dashboard V3 for filter bars). Read and mirror its structure, CSS classes, and token usage. Do NOT improvise new patterns mid-implementation. If no canonical reference exists, escalate — adding to the design system is a separate decision (DESIGN_SYSTEM §10.3). (2026-06-09)

74. **Decisions #74–#79 — WI-CALC-A.6 Step 0: Pay Run schema and implementation choices (2026-06-09).** Reconciliation against Product Master Spec produced 3 non-blocking gaps: (a) PayRun missing from Spec domain model — add post-WI; (b) "approved payout cannot be modified" is compatible with Reopen because Reopen is pre-Paid (Approved→Draft); (c) per-tenant serialization covered by the PayRun unique index. Six implementation decisions locked before any code:

   - **#74 — UNIQUE index shape:** `(TenantId, PayRunId, PayeeId, PlanId) WHERE Status <> 'Paid' AND Status <> 'Disputed' AND PayRunId IS NOT NULL`. `TenantId` kept for multi-tenant Rule 1 compliance. `<>` chosen over `NOT IN` for SQL Server partial-index compatibility.
   - **#75 — PayRunId nullable without backfill:** Pre-A.6 payouts remain accessible via `/payouts/:id` directly; they are outside the new UNIQUE index. No migration of existing rows.
   - **#76 — `Permission.PayoutsReopen` added:** Granted to TenantAdmin + CompManager, mirroring `PayoutsApprove` and `PayoutsMarkPaid` in the permission matrix.
   - **#77 — PayRun unique per tenant per period:** UNIQUE index `(TenantId, PeriodStart, PeriodEnd)` on `PayRuns`. One run per period per tenant; recalculation reuses the existing Draft run rather than creating duplicates.
   - **#78 — `CalculatePayRunCommand` wraps A.4 via `ISender`:** `CalculatePayRunHandler` dispatches `CalculatePayoutsForPeriodCommand` internally. No rewrite of the per-payout engine. Roll-ups computed after payouts are assigned to the run.
   - **#79 — FK `CompensationPayouts → PayRuns ON DELETE RESTRICT`:** Changed from SET NULL (which would orphan Paid payouts and break the audit trail). Runs with associated payouts cannot be deleted. (2026-06-09)

80. **Decision #80 — Pay Run UI routes and sidebar redirect (WI-CALC-A.6 Fase 5, 2026-06-10).** New routes: `/pay-runs` (PayRunListComponent) and `/pay-runs/:id` (PayRunDetailComponent). `/payouts/:id` unchanged (statement deep-link accessible directly). `/payouts` (flat list page) redirects to `/pay-runs` — the flat payout list is not resurfaced as a standalone page. Sidebar entry renamed from `/payouts` to `/pay-runs` ("Pay Runs" / "Ciclos de pago" / "Cykle wypłat"). Historical payouts pre-A.6 remain accessible via direct `/payouts/:id` links. (2026-06-10)

81. **Decision #81 — Calculate Pay Run is SYNCHRONOUS; no Hangfire job or polling loop (WI-CALC-A.6 Fase 5, 2026-06-10).** `CalculatePayRunHandler` wraps `CalculatePayoutsForPeriodCommand` via `ISender` and returns synchronously (same HTTP request). The frontend `onCalculate()` awaits `firstValueFrom(api.calculate(...))` with no polling. This eliminates the infinite-loop risk that existed in A.5.2 (`_pollJob` without stop condition). Trade-off: for large tenants (many payees, many transactions) this could time out on the HTTP level — defer async approach to a future WI when customer data reaches that scale. For current dev tenant (≤30 payees) synchronous is correct. (2026-06-10)

82. **Decision #82 — Historical query capability lives WITHIN Pay Run screens; flat payout list NOT resurrected (WI-CALC-A.6 Fase 6, 2026-06-10).** The filter and export capability that was removed when `/payouts` (flat list) was replaced by `/pay-runs` is restored through: (1) the list screen's manual date pickers (segment shortcuts + from/to date range to reach e.g. January 2022); (2) the detail screen's collapsible filter bar (status, period, amount range, payee/plan chips). A separate standalone `/payouts` page with full filter capability is NOT needed — the information is accessible within the Pay Run context. This aligns with Decision #72 (master→detail navigation). (2026-06-10)

83. **Decision #83 — Step 0 calibration by risk level (2026-06-10).** Step 0 (read-only reconciliation + spec audit before code) is calibrated by risk, not applied uniformly: **SKIP for low-risk additive work** — filters, export, UI, copy changes, adding endpoints that don't change existing behavior. These are build-on-top additions with no backwards compatibility concerns. **MANDATORY for money/schema/state work** — migrations, payout engine logic, domain state transitions (Paid, Approved, etc.), financial calculation changes, Permission grants. The rule: if a bug would be invisible in automated tests and only surfaced by smoke or prod data (like the `Money.Zero("USD")` bug from Decision #67), Step 0 is warranted. If a bug would be caught by the test suite or is isolated to a single new surface, skip Step 0 and build. (2026-06-10)

64. **Decision #64 — WI-CALC-MODEL Part 1 milestone: Phase 3 domain model complete (2026-06-02).** *Backfilled from chat conversation 2026-06-02 — was discussed and decided but not written to disk at the time.* Nine decisions (#55–#63) define the complete conceptual domain model for the Phase 3 Calculation Engine. Architectural takeaways: The existing Phase 1 entity shells (`CompensationPayout`, `PayoutLine`, `Credit`) are richer than initially recognized. They form a three-level calculation chain: `Transaction → Credit → PayoutLine → CompensationPayout`. Phase 3 V1 will populate them. Three read-only inspections informed this conclusion (Plan/Quota/Rule/PlanAssignment inventory; Rule.Trigger model; CompensationPayout+PayoutLine+Credit shells). Two distinct engines: **Credit Engine** (high frequency, continuous, in ingest pipeline) and **Payout Engine** (low frequency, monthly, manual trigger). Audit trail is preserved through immutable Credits with superseding markers + frozen RuleSnapshots embedded in each Credit. Comp manager retains full control: nothing changes automatically after approving a Payout; system only signals pending changes. Implementation scoped into sub-WIs (WI-CALC-A.0 through A.5). (2026-06-02)

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

- ~~**WI-PROD-MODEL — IN PROGRESS.**~~ **CLOSED (2026-06-01).** All 12 decisions recorded (Decisions #35, #36, #37). WI-PROD-A is now UNBLOCKED.
- **Phase D or Phase 2 next?** Phase D adds coverage and docs polish but does not unblock new features. Phase 2 is the core product (Transactions + Calculation Engine). Recommended: Phase 2 directly; Phase D items can land incrementally alongside Phase 2.
- **WI-06 approach:** Strict purity refactor (remove MediatR from Domain, EF Core from Application) deferred. If revisited, must update ARCHITECTURE.md §1.2 and run full regression suite. This is a 6-8h investment with no user-visible value — justify against backlog priority at that time.
- **F-028 standardization timing:** Cross-tenant mutations return 422 (not 404) across Quotas/Assignments/PlanRules/Imports. Deferred to a dedicated API contract standardization WI. Decision needed: standardize on 404 (ARCHITECTURE.md §9.3.3) or document 422 as intentional? Recommend 404 when the WI runs.
- **Operational logging in handlers:** Zero `_logger.LogX` calls in src/. The observability infrastructure (WI-12) is in place but no operational log statements have been added to handlers yet. Candidate for a focused WI or as part of Phase 2 handler development.
- **Manager/Rep scoped data access:** Currently Managers and Reps can view all data in their tenant. Needs scoping to assigned payees/teams as tenant size grows. Future WI when justified by customer feedback.
- ~~**Phase 2 audit hardening:**~~ **RESOLVED (WI-P2-00, 2026-05-28).** `IMoneyCriticalCommand` marker interface introduced. `AuditBehavior` wraps money-critical commands in an explicit EF Core transaction — audit failure rolls back the business write and propagates the exception. Non-money behavior unchanged. 3 new integration tests (Testcontainers) prove both paths. See decision #27.
- **Rate limit manual verification:** 2 skipped rate limit tests require manual curl/wrk verification before first production customer. Run: `for i in {1..10}; do curl -s -o /dev/null -w "%{http_code}\n" -X POST /api/auth/login; done` and verify 429 after limit.

---

## Backlog — Discovered during real-data testing (2026-05-29)

Real POS export: Reserved Polska / Galeria Katowice, April 2026, 3,183 rows. After the two parser/date bugs were fixed, Upload + Map + Preview completed. Preview was blocked by expected "payee not found" (payees were not pre-loaded — intentional test). No further bugs in the wizard itself. The following items are **product/domain design decisions**, not code bugs.

> **Real-data validation status (as of 2026-06-01):** Three retail stores imported successfully into the same tenant — Galeria Katowice (EMP001-EMP008, 3,183 txns), Galeria Mokotów Warszawa (EMP201-EMP209, 4,232 txns), and Silesia City Center Katowice (EMP301-EMP310, 5,066 txns). Total: ~26 payees, ~12,500 transactions, all imported via the Hangfire async pipeline with the field-requirement system (email/hire date optional per WI-PROD-A.1) and server-side payee name resolution (WI-PROD-F) functioning end to end. The original blocker that motivated WI-PROD-A.1 is dead in real testing. **No regressions observed.**

---

### WI-CALC-MODEL — Calculation Engine domain model ✅ PART 1 CLOSED (2026-06-02)

**Status:** Part 1 CLOSED (2026-06-02). Nine decisions (#55–#63) define the complete domain model for the Phase 3 Calculation Engine. Implementation is in progress through sub-WIs.

**Purpose:** Multi-session design conversation defining the domain model for Phase 3 (Calculation Engine). Parallels WI-PROD-MODEL in structure. Closed the gap between the existing Plan/Quota/Rule entity shells and the calculation engine implementation.

**Sub-WI sequence:**
- WI-CALC-A.0 ✅ Done (Decision #50) — Schema preparation
- WI-CALC-A.1 ✅ Done (Decision #51) — Credit Engine V1 + RuleSnapshot
- WI-CALC-A.2 ✅ Done (Decision #52) — Credit superseding on reassign (Decision #61 Case A)
- WI-CALC-A.2.5 ✅ Done — Procesar Pending: import warning + manual trigger button + Hangfire job (Decisions #53 + #54)
- WI-CALC-A.3 ✅ Done — Quota attainment service (Decision #63)
- WI-CALC-A.4 ✅ Done — Payout Engine + manual trigger (Decision #60)
- WI-CALC-A.5 ✅ Done (2026-06-09) — Payouts UI: A.5.1 filter+currency fix, A.5.2 polish+loop fix, A.5.3 bulk mark-paid, A.5.4 rich modals, A.5.5 period+amount in modal rows, A.5.6 Excel export. Full smoke: green.
- WI-CALC-A.6 ✅ Done (2026-06-10) — Pay Run: all 6 phases complete and smoke-validated. Fases 1–4: Domain + Migration + Engine + 20 integration tests. Fase 5: PayRunListComponent + PayRunDetailComponent + stores + action modals + sidebar redirect. Fase 6: Filters + Excel Export on both screens + `_lastLoadedFilter` race guard. The entire A.4→A.6 payout subsystem is done.

**Part 1.5 follow-up decisions:** Decisions #53 and #54 emerged from real-world testing during A.1/A.2 implementation and formalize how Pending transactions are visualized (at import time via existing WI-PROD-E validation table) and processed (via explicit user action, no auto-backfill).

---

### WI-CALC-A.0 — Phase 3 schema preparation ✅ DONE (2026-06-02)

**Status:** DONE. Schema-only WI — no engine logic, no commands, no API, no UI.

**What shipped:** Four nullable additive columns across three domain entities; one new enum; three EF configurations updated; migration `P3_SchemaPreparation` (20260602082902) generated and applied; 12 new tests. Codebase behavior is unchanged — all 704 non-skipped tests pass.

- `Rule.EffectivePeriod` (`DateRange?`) — maps to `EffectivePeriodStart`/`EffectivePeriodEnd` (`date null`) via `OwnsOne` + `Navigation(...).IsRequired(false)`. Tag validation enforced.
- `Rule.Tag` (`string?`, `nvarchar(50)`) — free label for logical grouping of rules; validates max 50 chars.
- `Plan.PeriodType` (`PlanPeriodType?`, `nvarchar(50)`) — 7-value enum, metadata only; `CloneAsNewVersion` copies it.
- `Credit.SupersededAt` / `Credit.SupersededBy` — superseding markers; filtered index `IX_Credits_TenantId_SupersededAt WHERE [SupersededAt] IS NULL`.

**Next (at time of this entry):** WI-CALC-A.1 (Credit Engine V1 — ✅ DONE) and WI-CALC-A.2 (Credit superseding — ✅ DONE). Active next: WI-CALC-A.2.5 (Procesar Pending — see WI-CALC-MODEL entry above).

---

### WI-CALC-A.1 — Credit Engine V1 ✅ DONE (2026-06-02)

**Status:** DONE. Credit Engine V1 complete. Credits created at ingest; `TransactionCalculatedEvent` raised; `Transaction.Status` transitions Pending→Calculated; `RuleSnapshot` frozen at allocation time.

**What shipped:** `ICreditAllocationService` (Application) + `CreditAllocationService` (Infrastructure, fully implemented); `CompensationTransaction.MarkCalculated(...)` (was a stub, now real); `TransactionCalculatedEvent`; integration into `TransactionImportJobHandler` and `IngestTransactionHandler`; `RuleSnapshotJsonConverter` for Credit persistence; `RateTable.Tiered` adjacent-boundary fix; `is null`/`is not null` binding rule for domain null checks.

**New tests:** 27 new (5 unit `CompensationTransactionTests`, 16 `CreditAllocationServiceTests`, 1 import E2E, 1 manual creation E2E). 704 → 731 non-skipped.

**TODO WI-CALC-A.3:** Remove `// TODO WI-CALC-A.2` stub in `CreditAllocationService.ComputeAttainmentCommission` — replace with real `IQuotaAttainmentService` (original A.2 content, re-numbered to A.3 after this bug-fix WI was inserted).

---

### WI-FRONTEND-FIX-1 — View Rule page: form rehydration + Live Preview ✅ DONE (2026-06-02)

**Status:** DONE. Two pre-existing UI bugs fixed. Discovered during WI-CALC-A.2 smoke test.

**Root cause (shared):** Backend `Program.cs` adds `JsonStringEnumConverter` globally; enum values arrive from the API as string names (`"Revenue"`, `"Flat"`, `"Sum"`) rather than integers. `_loadExistingRule()` was patching the form with raw string values. `WsSelect` compares selected value with `===` against numeric option values → no match → dropdown blank. `rateTableType()` computed did `Number("Flat") = NaN` → Live Preview fell to `@else` and showed "Attainment · 0 tiers" regardless of actual type.

**Fix:** Added `_enumToNumber<T>(enumObj, value): number` private helper to `RuleFormComponent`. Applied at every enum field in `_loadExistingRule()`: `MeasurementType`, `MeasurementAggregation`, `RateTableType`, `LogicalOperator`, `ConditionOperator`, `ConditionValueType`, `ModifierType`, `CapScope`. Also caches `rateTableTypeNum` so the tiered/attainment branch check (which was also comparing strings to numeric enum values) uses the coerced integer.

**File changed:** `WasnieUi/src/app/features/plans/rule-form/rule-form.component.ts`

**Tests:** +11 new in `rule-form.component.spec.ts` (Flat/Tiered/AttainmentBased rehydration, form control numeric values, tiersArray population, modifier + cap scope coercion). 143 → 154 frontend tests, all pass. Backend unchanged.

---

### WI-CALC-A.2 — Credit superseding on reassign ✅ DONE (2026-06-02)

**Status:** DONE. Decision #46 Case A implemented.

**Bug fixed:** WI-CALC-A.1 left Credits orphaned when a Calculated transaction was reassigned — the Credit's PayeeId no longer matched the transaction's PayeeId, but SupersededAt was NULL so attainment queries would have aggregated stale data.

**What shipped:**
- `Credit.Supersede(string reason, DateTimeOffset now, Guid eventId)` domain method with invariants (not-already-superseded, reason required, reason ≤ 500 chars).
- `CreditSupersededEvent` domain event carrying creditId, transactionId, payeeId, tenantId, reason.
- `ReassignPayeeHandler` updated: before changing payee, loads all non-superseded Credits for the transaction, calls `Supersede()` on each with a structured reason (`"Reassigned from payee {old} to payee {new} by {user} at {ts}. Reason: {commandReason}"`). After reassign, calls `ICreditAllocationService.AllocateAsync(transaction, ct)` immediately (Option A) — if Credits returned, persists them and marks transaction Calculated; if empty (new payee has no plan), leaves Pending.
- All operations within the same money-critical `IMoneyCriticalCommand` scope (atomic with audit).
- Decision #46 Cases B, C, D deferred (Payouts and plan-update flows don't exist yet).

**New tests:** 12 new (6 unit `CreditTests`, 4 integration `CreditSupersedeIntegrationTests`). 731 → 743 non-skipped.

**Sub-WI re-numbering:** Original A.2 (IQuotaAttainmentService) → A.3. Original A.3 (now absorbed here). A.4 (Payout Engine) unchanged. A.5 (Payouts UI) unchanged.

---

### WI-CALC-A.2.5 — Procesar Pending: import warning + manual trigger + Hangfire job ✅ DONE (2026-06-02/2026-06-09)

**Status:** DONE. Combines Decisions #53 + #54. Delivered as part of the A.5.x session.

**Scope:**

1. **Import wizard: ValidationIssue for unassigned rows (Decision #53).** When a CSV/XLSX row has no Staff ID and `Transaction.PayeeId` is Optional, emit a `ValidationIssue` (warning severity) using the existing `IssueCategory` infrastructure from WI-PROD-E. Inline in the wizard's existing validation table — no modal, no threshold. The comp manager sees all such warnings paginated alongside other Format/Reference/Required issues and decides: cancel the import and upload a corrected CSV, OR continue (rows enter as `Unassigned`, `PayeeId = null`).

2. **"Procesar Pending" button (Decision #54).** Visible on: PlanAssignment detail page (payee + period scope); Plan detail page (all assigned payees in the plan — bulk); filtered transactions list (filtered subset scope). Above the button: informative badge with eligible Pending count.

3. **`ProcessPendingTransactionsJob` (Hangfire, Decision #54).** Technical invariants:
   - Chunked processing (50–100 per chunk); each chunk in its own DB transaction with its own commit.
   - Idempotency: `(TransactionId, RuleId, PayeeId)` unique tuple — existing Credits skipped silently on retry.
   - Cancelable at chunk boundary (completed chunks persist; remaining transactions stay `Pending`; cancellation audit-logged).
   - Volume awareness: >5,000 candidates shows estimated time message (`total / 100 chunks × 3 s/chunk`); threshold hardcoded for V1.
   - Skipping rule: skip transactions with non-superseded Credits from a DIFFERENT Plan whose EffectivePeriod overlaps — does NOT auto-supersede (consistent with Decision #54).

4. **Audit trail per run:** actor, timestamp, scope, processed count, created count, skipped count + skip reason per transaction.

**Technical note:** All three Credit Engine paths (ingest, reassign, backfill) converge on `ICreditAllocationService.AllocateAsync` — Decision #63.

---

### WI-CALC-A.6 — Pay Run model implementation ✅ DONE (2026-06-10)

**Status:** DONE — all 6 phases complete and browser smoke-validated.

**Design reference:** `docs/Pay_Run_Model.md` (approved 2026-06-09, 6 closed decisions — Decision #66). Step 0 reconciliation done (Decisions #74–#79).

**Completed (all phases):**
- **Domain (Fase 1):** `PayRun` aggregate + `PayRunStatus` (Draft/Approved/Paid) + 3 domain events + `CompensationPayout` extensions (`PayRunId`, `AssignToRun`, `RevertToCalculated`). 16 unit tests green.
- **Migration (Fase 2):** `20260609132110_A6_AddPayRun`. `PayRuns` table, `PayRunId` nullable FK `ON DELETE RESTRICT`, rebuilt `IX_CompensationPayouts_Live`. Applied to local DB.
- **Engine + API (Fase 3):** 6 endpoints. `CalculatePayRunHandler` wraps A.4 engine via ISender. `UpdateRollUps` on every state transition. Roll-ups = GROUP BY currency (Cartesian-safe). `Permission.PayoutsReopen` added.
- **Integration Tests (Fase 4):** 20 tests (idempotency ×4, state machine valid/invalid ×6, roll-ups/anti-Cartesian ×3, multi-tenant ×2, permission gates ×5). Backend: 387 unit + 134+11 integration = 532 total. Build clean.
- **UI (Fase 5):** `PayRunListComponent` at `/pay-runs` + `PayRunDetailComponent` at `/pay-runs/:id`. Sidebar `/payouts`→`/pay-runs` redirect. `PayRunsStore` (global, `_lastLoadedFilter` race guard) + `PayRunDetailStore` (component-scoped via `providers: []`). Calculate SYNC — no Hangfire job, no polling. Action modals: Approve/Reopen (reversible — no irreversibility warning per Decision #69); MarkPaid (irreversible — 5 mandatory elements). Pattern B per-currency roll-ups; `payeeCount` + `paidPayeeCount` distinct; audit fields (created/approved/paid + actor). i18n EN/ES/PL complete. Smoke: full cycle calculate→Draft→Approve→Reopen→MarkPaid→Paid green.
- **Filters + Export (Fase 6):** Detail: collapsible filter bar (status, period from/to, amountMin/max, payee+plan chips via `WsSelect searchFn`, hide-$0 toggle) + Export to Excel. List: manual date range pickers (segment resets to "All time" on manual date; default this-month first→last day) + Export to Excel. Backend: `PayoutFilterQuery` extended; `ListPayoutsHandler.BuildQuery()` `static internal` (shared). +43 frontend unit tests; +11 backend integration tests. Smoke: filter+export aligned; no loops.

**Post-A.6 roadmap (deferred — separate WIs):**
- Aggregated payroll export by run (for payroll system integration — distinct from per-payout export)
- Email notification on run close: Resend infra → export attached → `/admin` recipient settings → tier limits Growth 2 / Scale 4 / Enterprise N via `TierLimitChecker` → trigger on PayRun Paid. GDPR: salary data by email requires strong recipient validation.
- Adjustments/clawbacks WI
- WI-UX-GUIDANCE: commission flow self-explanatory (empty states with cause+action, Transaction→Credit→Payout stage indicator, onboarding wizard)

---

### WI-PROD-MODEL — Retail SPM domain model review ✅ CLOSED + FULLY REALIZED IN CODE (2026-06-01)

**Status:** CLOSED. All 12 firm decisions taken across three parts (Decisions #35, #36, #37). **As of 2026-06-01 end-of-day, all 12 decisions are implemented and smoke-tested in vivo — the conversation and the codebase are in sync.** The Calculation Engine (Phase 3) is now unblocked; the retail SPM data model it consumes has stabilized (see Decision #41).

**Problem:** Current domain model encodes B2B-tech SPM assumptions that conflict with retail SPM (the actual target market):

| Field | Current | Retail reality | Gap |
|---|---|---|---|
| `Payee.Email` | Required | Seasonal/temporary staff frequently have no corporate email | Blocks realistic payee import for retail |
| `Payee.HireDate` | Required | Migrating tenants often only have plan-activity dates, not historical hire dates | Blocks onboarding of existing workforce |
| `CompensationTransaction.PayeeId` | Required (FK, not nullable) | E-commerce self-service, house-pool sales, and system-processed returns are valid transactions with no salesperson | Blocks import of ~10-15% of a typical retail POS file |

**Firm decisions taken — Part 1 (2026-05-30) → Decision #35:**
- **A** — Field-level requirement configuration system per tenant (TenantAdmin only; audit-logged; no retroactive effect).
- **B** — Configurable fields: `Payee.Email`, `HireDate`, `Role`, `ManagerId`, `CompensationTransaction.PayeeId`. All default Optional for new tenants.
- **C** — Always-required (not configurable): `Payee.FullName`, `Payee.EmployeeCode`, `CompensationTransaction.ReferenceNumber / Amount / Currency / TransactionDate`, `TenantId` on both.
- **D** — `CompensationTransaction.PayeeId` becomes nullable. Calc Engine MUST define null-PayeeId policy before Phase 3 design starts.

**Firm decisions taken — Part 2 (2026-05-30) → Decision #36:**
- **E** — `User` and `Payee` are separate but linkable via nullable `User.PayeeId`. No rep portal; single RBAC identity system. MVP uses manual invite links (no email-send dependency yet).
- **F** — `Payee.EmploymentType` (full-time / part-time / temporary / contractor) added; nullable; joins configurable-fields list.
- **G** — Payees never deleted. `Payee.IsActive` + `Payee.DeactivatedAt`; inactive payees cannot receive new transactions; re-activation clears `DeactivatedAt`; audit-logged.
- **H** — `Payee.Location` (or `CostCenter` — naming TBD at implementation) optional string dimension, NOT a `Store` entity. Also likely on Transaction (to confirm during scoping). Joins configurable-fields list.
- **I** — Tenant account currency + explicit FX. Transactions preserved in native currency; explicit conversion with traceable exchange-rate source; original + converted amounts both persisted. Expands WI-PROD-CURRENCY to a full system (see that entry).

**Part 3 decisions — CLOSED (2026-06-01) → Decision #37:**
- **Decision 10** — Status enum unchanged; `Pending` is the correct default; "Unassigned" is derived from `PayeeId IS NULL`, not a status value. Phase 3 engine filters `Status = 'Pending' AND PayeeId IS NOT NULL`.
- **Decision 11** — `AssignPayeeCommand` (no reason required) and `ReassignPayeeCommand` (reason ≥ 10 chars, mandatory) as distinct `IMoneyCriticalCommand` commands. State machine: Paid → BLOCKED; Calculated → return to Pending + invalidate commission line; Cancelled → allowed.
- **Decision 12** — Import against inactive payee: accept with `IssueSeverity.Warning` (historical assignment is legitimate). `skipRowsWithWarnings` toggle available.

**Phase 3 cross-dependency RESOLVED:** Calculation Engine filters `PayeeId IS NOT NULL` — unassigned transactions are skipped silently (not attributed to a house-pool entity, not an error). Phase 3 design may proceed on this foundation.

---

### WI-PROD-A — Field-level requirement configuration system per tenant

**Status:** ✅ FULLY CLOSED (2026-06-01). WI-PROD-A.1 ✅, WI-PROD-A.2 ✅, WI-PROD-A.3 ✅. All WI-PROD-MODEL decisions (A–I, 10–12) are now live in code. WI-PROD-MODEL is CLOSED.

**Full scope (all 12 WI-PROD-MODEL decisions — #35, #36, #37):**

*Sub-WI A.1 ✅ DONE (2026-06-01) — Email + HireDate optional via FieldRequirementSettings:*
1. Implement the field-level requirement configuration system per tenant (TenantAdmin-only setting, audit-logged per Rule 5.1.5, no retroactive effect on existing data).
2. Make the five configurable fields nullable in the entities: `Payee.Email`, `Payee.HireDate`, `Payee.Role`, `Payee.ManagerId`, `CompensationTransaction.PayeeId`.
3. Convert the `(TenantId, Email)` unique index to a filtered index (`WHERE Email IS NOT NULL`) — same pattern as the `(TenantId, Source, ExternalId)` filtered index from WI-P2-02.
4. Build the TenantAdmin settings UI for the field-requirement toggles.
5. Audit-log every change to a requirement setting.
6. Update `IngestTransactionCommand` validator and `TransactionImportValidationService` to respect per-tenant field requirements.
7. `Payee.EmploymentType` nullable (Decision F); add to configurable-fields list with default Optional.
8. `Payee.IsActive` (default true) + `Payee.DeactivatedAt` (DateTimeOffset, nullable); `IsActive → false` automatically sets `DeactivatedAt`; re-activation clears it; all transitions audit-logged; ingest validator blocks assignment of new transactions to inactive payees (Decision G).
9. Import validator: `IssueSeverity.Warning` when `EmployeeCode` matches an inactive payee — message `"Payee X (code Y) is inactive — assignment will be historical"`. Row is imported; `skipRowsWithWarnings` toggle available (Decision 12).
10. `Payee.Location` (or `CostCenter` — finalize naming during scoping) nullable string; treated as filter/group dimension in reports; also add to `CompensationTransaction` if confirmed during scoping (Decision H); joins configurable-fields list with default Optional.

*Sub-WI A.2 ✅ DONE (2026-06-01) — EmploymentType + Location; Role + ManagerId catalog-configurable:*
7. `Payee.EmploymentType` nullable enum (FullTime/PartTime/Temporary/Contractor); joins configurable-fields catalog with default Optional. ✅
8–10 (partial — no IsActive yet). `Payee.Location` nullable string (up to 200 chars); joins configurable-fields catalog with default Optional. ✅ `Payee.Role` and `Payee.ManagerId` pre-existed in the entity; now added to the `FieldRequirementSettings` catalog (both default Optional). ✅ Shared `PayeeFieldNames` constants class introduced — validators reference constants, not string literals; future catalog additions require only a constant + a seed row. ✅ Settings UI absorbed the four new entries automatically (data-driven from A.1 — zero template changes). ✅ `PayeeImportValidationService` extended for EmploymentType (enum validation) and Location (max-length); Role is now catalog-driven (Error vs. Warning). ✅ `PayeeImportExecutionService` passes both new fields to `Payee.Create()`. ✅ Tests: 595 → 628 (+33). Smoke-tested in vivo — Settings shows 6 rows; Payee form accepts and persists both fields. ✅

*Sub-WI A.3 ✅ DONE (2026-06-01) — IsActive lifecycle + assignment commands + Frontend UI (Decisions G, 11, 12):*
8. `Payee.IsActive` (default true) + `Payee.DeactivatedAt` (DateTimeOffset, nullable); `IsActive → false` sets `DeactivatedAt`; re-activation clears it; all transitions audit-logged. Ingest validator blocks assignment to inactive payees. ✅
9. Import validator: `IssueSeverity.Warning` when `EmployeeCode` matches inactive payee — `"Payee X (code Y) is inactive — assignment will be historical"`. Row imported; `skipRowsWithWarnings` toggle available. ✅
11. `AssignPayeeCommand` (`IMoneyCriticalCommand`): assigns a payee to a transaction where `PayeeId IS NULL`. No reason required. Allowed for CompManager + TenantAdmin. Audit-logged via `AuditBehavior`. ✅
12. `ReassignPayeeCommand` (`IMoneyCriticalCommand`): changes payee from A to B. Reason field REQUIRED (≥ 10 chars), persisted in audit log event. Allowed for CompManager + TenantAdmin. Audit-logged via `AuditBehavior`. ✅
13. State machine enforcement in domain layer: Paid → domain exception; Calculated → return to Pending + mark commission line obsolete; Eligible → return to Pending; Cancelled → allowed. Backend throws on violation. ✅
15. Assign/Reassign UI on transaction list + detail; Deactivate/Activate in payee list and detail (with confirmation modals); IsActive badge; DeactivatedAt shown in profile tab. ✅
14. `User.PayeeId` nullable FK; TenantAdmin invite flow uses existing RBAC; manual invite-link mechanism for MVP (no email-send dependency — consistent with WI-02 deferral, Decision E).
15. Assign / Reassign UI on the transaction detail/list (action gated by status: Paid rows show disabled/hidden action per CLAUDE.md §5.8 RBAC rules).
16. Reason field modal for reassignment (required input, ≥ 10 chars, client-side validation).
17. "Unassigned" rendering already shipped (WI-PROD-F) — no new work, but verify it holds after nullable migration.

**⚠️ Do NOT attempt as a single WI.** This is a large, multi-layer change. Splitting into A1/A2/A3 lets each sub-WI close cleanly with tests. Scoping conversation recommended before any implementation starts.

---

### WI-PROD-B — Multi-sheet Excel: user-driven sheet selection

**Status:** Bug. Not yet implemented.

**Problem:** Both import wizards silently use `wb.Worksheet(1)` — the first sheet. The Reserved test file has three sheets; if the data sheet had not been first, the wizard would have silently imported junk.

**Proposed fix:** Between Upload and Map Columns, if the workbook has > 1 sheet, show a sheet-picker UI with the sheet names and a 3-row preview of each. Default to the first sheet but require explicit user confirmation. Applies to both Payee and Transaction wizards (same root — `FileParserService.ParseXlsx`).

---

### WI-PROD-C — First-import onboarding "valley of death"

**Status:** UX gap. Conversation pending.

**Problem:** A new comp manager uploads a transaction CSV before creating payees → 3,000+ "payee code not found" errors → product abandonment. The required order (payees first, transactions second) is invisible in the UI. During the Reserved test, the Import Transactions sidebar item was chosen instead of Import Payees; the wizard accepted the file and returned all errors.

**Three candidate directions (owner to choose):**
1. **Guided onboarding:** Detect empty-tenant state (zero payees). Redirect to payee wizard on first transaction import attempt with a clear message.
2. **Combined import:** Excel template with two named sheets — "Employees" and "Transactions" — imported in one wizard flow (parse both, create payees first, then transactions).
3. **"Create missing payees" in Preview:** At the transaction Preview step, if there are "payee not found" errors, offer an action to auto-create minimal payee records (code + name only) and re-validate. Simplest UX change; no pre-created payees required.

**Also needed:** Sidebar disambiguation. "Import Payees" and "Import Transactions" have equal visual weight — misleads first-time users into picking the wrong wizard. Consider grouping or labeling them.

---

### WI-PROD-D — Promote `WsProgressBar` to design system ✅ DONE (2026-06-01)

**Status:** DONE. The trigger condition fired: payee import needed a progress screen, making it the second consumer. Delivered as part of WI-PROD-L (see below).

**What shipped:** Shared `ImportProgressComponent` (`features/imports/shared/import-progress.component`) replaces the local progress-bar CSS that was in the transaction wizard. Both import wizards now use this shared component. Progress bar animation (indeterminate sweep for sync imports, determinate fill for polled jobs), error/retry state, and net-error indicator are all in one place. See WI-PROD-L for full details.

---

### WI-PROD-E — Actionable "payee not found" error message ✅ DONE (2026-06-01)

**Status:** DONE (2026-06-01). Scope expanded from the original 1-line fix to cover all 36 emit sites in both import validators, plus the `IssueCategory` model and UI visual distinction.

**Summary:** Validation issue messages now include offending value + corrective action; new `IssueCategory` field on `ValidationIssue` with visual distinction in preview (Reference→amber, Format→red, Required→blue, Other→default). 36 emit sites updated across payee + transaction import validators. 3 new binding rules added to `14-forbidden-patterns.md`. Test count: 628→644 (+16). Smoke-tested in vivo.

**Second real-world validation (2026-06-01, during WI-PROD-A.3 smoke test):** A payee CSV with `EmploymentType = "Full-time"` (human-readable, hyphenated) triggered the new contextual error format. The message included the offending value (`'Full-time'`) and the accepted values (`FullTime, PartTime, Temporary, Contractor`), letting the owner resolve the issue in seconds without support. This confirmed WI-PROD-E's design intent in a realistic accidental-entry scenario. The underlying tolerance gap (enum variants vs. canonical values) is tracked separately as WI-PROD-P.

---

### WI-PROD-CURRENCY — Multi-currency handling system

**Status:** Scope substantially expanded by Decision I (2026-05-30). Design conversation needed before code. This is now a system, not a styling fix.

**Original problem (still applies):** The transaction list mixes €, $, and PLN inconsistently in the same Amount column. Display formatting still needs standardizing: ISO-code prefix (`PLN 376.99`, `EUR 8,500.00`), always two decimal places, no cross-currency totals in the list footer (suppressed or per-currency breakdown).

**Expanded scope (Decision I — 2026-05-30):** Spec §5b.5 already prohibited implicit FX conversion. Decision I now defines the explicit, traceable conversion system Wasnie will use for reports, payouts, and reconciliation in the tenant's account currency:

**System components to implement:**
1. **Account-currency field on Tenant** — TenantAdmin-configured single payout currency (e.g. PLN for Reserved Polska).
2. **Exchange rate table** — stores rates per (from-currency, to-currency, date). Open sub-questions to resolve during scoping:
   - Rate source: ECB feed / fixer.io / manual TenantAdmin entry
   - Rate date: transaction date, period-close date, or payout date
   - Retroactive rate changes: disallowed (rates are immutable once set) vs. allowed (with full audit trail of what changed and when)
3. **Original + converted amounts on every transaction** — the native `(Amount, Currency)` is never overwritten. A separate `(ConvertedAmount, ConvertedCurrency, ExchangeRate, ExchangeRateDate)` tuple is added when conversion occurs. Both are preserved for audit traceability.
4. **Conversion engine** — applies explicit FX per Spec §5b.5. The conversion IS the explicit module that §5b.5 already permits; it does not violate the no-implicit-conversion rule.

**Display scope (unchanged from original):** `CurrencyDisplayPipe`, list column formatting, per-currency breakdown in footers. No cross-currency implicit totals anywhere in the UI.

**Dependencies:** WI-PROD-CURRENCY must precede WI-PROD-J (summary widget) and WI-PROD-K (reconciliation tool), since both display currency-converted aggregates.

---

### WI-PROD-F — Server-side payee name resolution in the transaction list ✅ DONE (2026-05-30)

**Status:** **Closed.** JOIN-side payee resolution implemented; transactions list no longer shows raw GUIDs.

**Fix:** `TransactionDto` extended with `PayeeName?` and `PayeeEmployeeCode?` (nullable, default null). `ListTransactionsHandler` batch-fetches payee names after pagination via `WHERE Id IN (pagePayeeIds)` — single additional query, no N+1. Frontend reads `tx.payeeName` directly from the DTO; null/empty renders localized "Unassigned" / "Sin asignar" / "Bez przypisania". `PayeesStore` dependency removed from `TransactionsListComponent`. New binding rule added to `14-forbidden-patterns.md`.

---

### WI-PROD-G — Tenant test-data reset mechanism

**Status:** Low priority. Developer convenience.

**Problem:** Manual testing accumulated noise rows — garbage transactions, GUIDs used as payee codes, mixed-currency amounts in the millions — that complicate clean re-tests and make screenshots unusable for demos.

**Proposed direction:** A developer/admin endpoint or seed-and-reset script scoped to a named test tenant. Options: (a) a `DELETE /api/dev/reset-tenant-data` endpoint (dev-only, behind environment guard), or (b) a standalone EF seeder script that wipes and re-seeds the test tenant to a known clean state. Not a migration — data-only operation.

**Scope:** Dev-only surface; zero Production impact. Can be a simple SQL script in `/scripts` rather than application code.

---

### WI-PROD-H — "New Transaction" button placement consistency ✅ DONE (2026-05-29)

**Status:** ~~Low complexity. Small UI polish.~~ **Closed.**

**Problem:** The "New Transaction" button on the transactions list page is positioned inconsistently relative to the "Add Payee" button on the payees list page. The Payees pattern is the canonical reference (CLAUDE.md §5.1).

**Fix:** Move the button to match the exact placement used on the Payees list. No UX design conversation needed — Payees is the model.

---

### WI-PROD-I — Search typeahead for the transactions list

**Status:** Medium priority.

**Problem:** The transactions list has no search input. Users cannot filter by reference number, payee name, or amount without scrolling through all rows. The payees list already implements the canonical pattern (debounced 300 ms, server-side filter, `WsInput` with `prefixIcon="search"`).

**Fix direction:** Mirror the payees list search pattern exactly. `ListTransactionsHandler` already accepts a `search` filter parameter (added in WI-P2-03b); the frontend just needs the input wired up. No backend changes required.

**Scope:** `TransactionsListComponent` — add search `WsInput`, debounce 300 ms via `Subject`, call `store.setSearch()`. `TransactionsStore` already has `setSearch`. Effort: ~1 h.

---

### WI-PROD-J — Transactions page summary widget: per-currency totals + time-series chart

**Status:** Higher complexity. Deferred until WI-PROD-CURRENCY is resolved.

**Problem:** The transactions list currently shows raw rows with no aggregate view. A comp manager needs to see at a glance: total revenue per currency in the selected period, and a trend over time.

**Proposed direction:** Add a summary section above the list with (a) per-currency total cards (no implicit cross-currency conversion — per Spec §5b.5 and WI-PROD-CURRENCY), and (b) a time-series chart of transaction volume by date with date-range filtering. Currency grouping and display format must follow the convention decided in WI-PROD-CURRENCY.

**Dependencies:** WI-PROD-CURRENCY (display convention must be decided first). Chart library not yet chosen — options include Chart.js (lightweight), Apache ECharts (richer), or ng2-charts wrapper. Library choice is a separate design decision.

**Scope when ready:** New `GET /api/transactions/summary` endpoint (aggregates server-side, grouped by currency + date bucket); summary component above the list; chart primitive decision.

---

### WI-PROD-K — Books reconciliation tool

**Status:** Backlog. Added 2026-05-30.

**Problem:** Without a reconciliation view, a comp manager who pays commissions on a wrong transaction base will not discover the discrepancy until an external auditor catches it — at which point trust and compliance credibility are both damaged. This is a critical trust gap for mid-market clients with formal audits.

**Proposed direction:** A dedicated screen letting the comp manager view transaction totals aggregated by period, currency, channel/source, and payee — designed explicitly so the totals can be compared against the client's General Ledger / accounting books.

**Relationship to WI-PROD-J:** WI-PROD-J covers a summary widget on the transactions list page (per-currency totals + time-series chart). WI-PROD-K is a dedicated reconciliation screen optimized for audit-readiness. There may be overlap; resolve the boundary between the two WIs during scoping.

**Scope when ready:** Reconciliation screen with aggregation by period / currency / source / payee; export capability (CSV/XLSX) so the comp manager can cross-check against GL; back-end aggregation endpoint (server-side, tenant-isolated). Exact field set to be confirmed during scoping.

---

### WI-PROD-L — Payee import wizard: progress indicator + result feedback ✅ DONE (2026-06-01)

**Status:** DONE. Discovered and delivered during the Silesia City Center real-data test session (2026-06-01).

**Problem observed:** The payee import wizard executed imports synchronously with no dedicated progress screen. While the import ran, the user remained on the preview step table with only a spinning button as feedback. No dedicated success or failure screen existed. In contrast, the transaction import wizard had a full "Importing…" progress step with an animated bar, a completion screen showing row counts, and a failure screen with error message and retry. This inconsistency was noticed during a real import of the Silesia City Center dataset.

**What shipped (all frontend, no backend changes):**

1. **Shared `ImportProgressComponent`** (`features/imports/shared/import-progress.component`) — pure visual component. Inputs: `title`, `subtitle`, `progress` (null = indeterminate sweep, 0–100 = determinate fill), `errorMessage`, `netError`. Output: `retry` event. Delivers WI-PROD-D (progress bar promoted to shared use when second consumer appeared). EN/ES/PL i18n in `IMPORTS.SHARED.*` namespace.

2. **`PayeeImportingStepComponent`** (`features/imports/payees/steps/importing-step.component`) — new wizard step. Fires the import HTTP call on `ngOnInit()`, shows indeterminate progress bar, emits `completed(result)` on success or shows error + retry on failure. Session storage never restores this step (the request is gone on page refresh — restored to preview instead).

3. **Transaction wizard refactored** — `TxProgressStepComponent` now delegates all visual rendering to `ImportProgressComponent`. Local progress-bar CSS (previously in `progress-step.component.scss`) moved to the shared component. Behaviour (polling, state machine) unchanged. All 6 existing progress-step tests pass.

4. **Payee wizard updated** — 4 steps (upload → map → preview → complete) extended to 5 (+ importing). Preview step no longer calls the import service directly; it emits `importRequested` to the wizard, which transitions to the importing step. Wizard stores `skipWarnings` signal.

**Validation:** Owner successfully imported the Silesia City Center dataset (10 payees, 5,066 transactions) via the updated wizard. No regressions in payee or transaction import flows. Test suite: 143/143 frontend tests pass.

---

### WI-PROD-R — UX polish: Unassigned visibility + Assign/Reassign modal overflow ✅ DONE (2026-06-01)

**Status:** DONE. Two-file frontend fix, no backend changes.

**Fix 1 — Unassigned visibility:** "Unassigned" in the transaction list payee column now renders as italic + `--color-text-tertiary` instead of the same `col-secondary` style as real payee names. A comp manager scanning 50 rows can identify Unassigned transactions at a glance without them being garish (Unassigned is a normal operational state, not an error). No new i18n keys — the existing `TRANSACTIONS.UNASSIGNED` key was reused.

**Fix 2 — Modal async dropdown overflow:** Two-phase fix. Phase 1 (height estimate): `openDropdown()` now uses `estimatedHeight = 280` in async mode so the direction/constraint algorithm fires with a realistic height before options load. Phase 2 (escape `overflow: hidden`): the final root cause was that `.ws-modal__dialog { overflow: hidden }` clips ALL absolutely-positioned descendants regardless of the ws-select calculation. The fix: when inside a `.ws-modal__dialog`, the dropdown uses `position: fixed` with coordinates set from `triggerRect.getBoundingClientRect()`. Fixed elements are positioned relative to the viewport and escape ALL overflow ancestors — the industry-standard approach used by Angular Material, React Select, and every other production-grade dropdown in a modal context. The existing modal-aware direction logic (`triggerEl.closest('.ws-modal__dialog')`) is preserved and now also sets the fixed coordinates.

**Fix 3 — Settings catalog label:** Seventh Settings row (Transaction → PayeeId) relabelled from "Payee / Transaction" to "Require payee on new transactions" with a descriptive subtitle "If Optional, transactions can be created or imported without a payee (shown as Unassigned)." EN/ES/PL updated. The toggle's purpose (PayeeId required on creation/import) was always correct; only the label was opaque.

---

### WI-PROD-P — Tolerant enum value parsing in CSV import

**Status:** Pending. **Priority: LOW.** Discovered during WI-PROD-A.3 smoke test (2026-06-01).

**Problem:** `PayeeImportValidationService` accepts only canonical enum values (`FullTime`, `PartTime`, `Temporary`, `Contractor`) for the `EmploymentType` column and rejects natural human-readable variants (`Full-time`, `Part-time`, etc.) that any user editing an Excel export would actually write. The WI-PROD-E contextual error message correctly surfaces the problem and the accepted values, so users CAN recover without support — but the friction is real.

**Scope:** Tolerant parsing layer in `PayeeImportValidationService` for enum-typed columns. Accept common variants (case-insensitive, hyphenated, spaced) and normalize to the canonical enum value before validation. Same approach for any future enum-typed import column. Canonical names remain as authored in the domain enum.

**Note:** WI-PROD-E's contextual error message is a sufficient workaround for now. Pick this up in the next minor UX polish pass.

---

### WI-PROD-Q — Frontend test coverage sweep

**Status:** Pending. **Priority: LOW-MEDIUM.** Accumulated across WI-PROD-A.1, A.2, A.3, and the payee-import UX fix.

**Problem:** Frontend test count has been static at 143 through four WIs that added significant new components: `FieldRequirements` toggles, EmploymentType select, Location input, `AssignPayeeModalComponent`, `ReassignPayeeModalComponent`, IsActive state badges, and validation-message category badges. This is measurable test debt.

**Scope:** Dedicated sweep adding unit/component tests for the above. Target: 60–70 new frontend tests, restoring a healthy coverage ratio. Priority before first paying customer.

---

### WI-PROD-N — File upload security hardening

**Status:** Pending. **Recommended timing: before signing first paying customer.** (~1 focused day of work; not urgent today.)

**Why:** Any mid-market customer IT-security questionnaire is likely to ask about file upload handling. Current gaps: no magic-byte validation, no per-user upload rate limiting, no exhaustive structured upload logging, no internal security documentation to show a reviewer. See decision #39 for the full threat-model snapshot.

**Scope:**

1. **Magic-byte / file-signature validation** — inspect the first bytes of every uploaded file BEFORE handing off to the parser library (ClosedXML for `.xlsx`, CsvHelper for `.csv`). Reject files whose declared extension (or MIME type) does not match their actual binary signature. Applies to both `POST /api/imports/payees/parse` and `POST /api/imports/transactions/parse`.

2. **Per-user rate limiting on upload endpoints** — the global rate limiter (from WI-11) applies at the IP level. Add a per-authenticated-user limit on the two parse endpoints (e.g. N uploads per user per minute) to close the case where an authenticated user floods the parser with large files. Return 429 with a clear message. Configure the limit in `appsettings.json` alongside the existing `RateLimiting` section.

3. **Structured upload logging** — on every upload attempt (success or rejection), emit a structured log event containing: actor `UserId`, `TenantId`, file size (bytes), declared MIME type, detected file signature, SHA-256 hash of the file content. Useful for forensics if a file is later flagged; required for an audit trail in regulated environments.

4. **Internal security documentation** — a short document in `docs/security/` (new subfolder) summarising exactly what the system does and does not do with uploaded files. This is what the owner shows during a customer IT-security review. It should cover: parsing library (ClosedXML / CsvHelper), no disk persistence, no serving back to users, size limit, signature check, rate limiting, antivirus status, tenant isolation.

**Out of scope:** Active antivirus scanning (that is WI-PROD-O).

---

### WI-PROD-O — Antivirus scanning integration

**Status:** Pending. **Recommended timing: when contractually required by a customer.** Do NOT implement speculatively.

**Why:** Active AV scanning is standard for financial-services and healthcare customers but rarely required by mid-market retail. Implementing it before a customer asks adds latency and operational cost for no current benefit. The backlog entry exists so the gap is visible and scoped when the time comes.

**Scope:**

1. **Provider selection** — three candidates to evaluate during scoping:
   - *Azure Defender for Storage* — native Azure integration, suits the existing App Service hosting; cost is per-GB scanned; data stays in Azure region (good for GDPR).
   - *Self-hosted ClamAV* — open source, zero per-scan cost, runs as a sidecar or separate container; operational overhead for keeping signatures updated.
   - *External API (e.g. VirusTotal)* — simplest integration but files leave the tenant's data boundary; likely unacceptable for any regulated customer.
   Decision on provider is deferred to scoping; record the choice in a new decision entry when made.

2. **Scan flow design** — choose synchronous or asynchronous during scoping:
   - *Synchronous* — block the upload response until scan completes; simpler error path but adds latency (typically 1–3 s for clean files, longer for flagged ones).
   - *Asynchronous* — accept the upload immediately, scan in a Hangfire background job, reject via a follow-up notification if positive; better UX but requires a quarantine state and a notification channel (the IEmailService abstraction is ready for this per decision #10 deferral).

3. **Quarantine workflow** — on positive detection: reject/delete the uploaded file, emit a `IssueSeverity.Error` validation result to the user, alert the TenantAdmin (via the notification channel once WI-02 / email is implemented), log the event at `LogLevel.Critical` with the file hash and actor details.

4. **Interaction with WI-PROD-N** — WI-PROD-N must ship first; the magic-byte check and structured upload logging provide the forensic baseline that AV scanning sits on top of.

**Out of scope:** General endpoint security hardening (already covered by WI-11 and WI-PROD-N).

---

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
├── Wasnie_Informe_Tecnico.docx                    ← original market analysis (Spanish, historical)
└── Pay_Run_Model.md                               ← Pay Run domain model design (A.6, approved 2026-06-09)

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
