import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PayeesStore } from '../state/payees.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { QuotaStatusVariantPipe, QuotaStatusLabelPipe } from '../../../shared/pipes/quota-status.pipe';
import { PayeesApiService } from '../services/payees.api.service';
import { QuotaMeasurementType, QuotaSummary } from '../../quotas/models/quota.model';
import { PayeeDashboard, SalesTrendPoint } from '../models/payee-dashboard.model';
import { Payee, PayeeStatus } from '../models/payee.model';
import { PayeeFormComponent } from '../form/payee-form.component';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { WsLoadMoreDirective } from '../../../shared/directives/ws-load-more.directive';
import { PagedResult } from '../../../shared/models/pagination.models';
import { Assignment } from '../../assignments/models/assignment.model';
import { CreditListItem } from '../../credits/models/credit.model';
import {
  WsBadgeComponent,
  WsButtonComponent,
  WsCardComponent,
  WsDatePickerComponent,
  WsModalComponent,
  WsConfirmationModalComponent,
  WsTableEmptyComponent,
  WsGaugeComponent,
  WsBarChartComponent,
  WsSegmentedControlComponent,
  WsTooltipDirective,
  type BarChartPoint,
  type SegOption,
  type BadgeVariant,
} from '../../../shared/ui';

type Tab = 'overview' | 'profile' | 'activity';
type PeriodKey = 'this-month' | 'last-month' | 'ytd' | 'all-time';

@Component({
  selector: 'app-payee-detail',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    ReactiveFormsModule,
    TranslateModule,
    DecimalPipe,
    DateFormatPipe,
    CurrencyFormatPipe,
    QuotaStatusVariantPipe,
    QuotaStatusLabelPipe,
    PayeeFormComponent,
    HasPermissionDirective,
    WsLoadMoreDirective,
    WsBadgeComponent,
    WsButtonComponent,
    WsCardComponent,
    WsDatePickerComponent,
    WsModalComponent,
    WsConfirmationModalComponent,
    WsTableEmptyComponent,
    WsGaugeComponent,
    WsBarChartComponent,
    WsSegmentedControlComponent,
    WsTooltipDirective,
  ],
  templateUrl: './payee-detail.component.html',
  styleUrl: './payee-detail.component.scss',
})
export class PayeeDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly payeesApi = inject(PayeesApiService);
  readonly store = inject(PayeesStore);
  private readonly toast = inject(ToastService);

  readonly PayeeStatus = PayeeStatus;
  readonly QuotaMeasurementType = QuotaMeasurementType;

  readonly payeeId = this.route.snapshot.paramMap.get('payeeId')!;
  // Stable timestamp for the component's lifetime — prevents NG0100 from
  // computePacing() returning slightly-different floats on each CD pass.
  private readonly _nowMs = Date.now();
  readonly activeTab = signal<Tab>('overview');
  readonly period = signal<PeriodKey>('this-month');

  readonly periodOptions: SegOption[] = [
    { value: 'this-month', label: 'DASHBOARD.PERIOD_THIS_MONTH' },
    { value: 'last-month', label: 'DASHBOARD.PERIOD_LAST_MONTH' },
    { value: 'ytd',        label: 'DASHBOARD.PERIOD_YTD' },
    { value: 'all-time',   label: 'DASHBOARD.PERIOD_ALL_TIME' },
  ];

  readonly periodCounterLabel = computed(() => {
    switch (this.period()) {
      case 'this-month': return 'DASHBOARD.COUNTER_THIS_MONTH';
      case 'last-month': return 'DASHBOARD.COUNTER_LAST_MONTH';
      case 'ytd':        return 'DASHBOARD.COUNTER_YTD';
      case 'all-time':   return 'DASHBOARD.COUNTER_ALL_TIME';
    }
  });

  readonly invalidQuotaCount = computed(() =>
    (this.dashboard()?.attainmentItems ?? []).filter(a => !a.isCurrencyValid).length
  );

  readonly editModalOpen = signal(false);
  readonly terminateModalOpen = signal(false);
  readonly deactivateModalOpen = signal(false);
  readonly saving = signal(false);
  readonly deactivateSaving = signal(false);

  // Dashboard (gauges + trend)
  readonly dashboard = signal<PayeeDashboard | null>(null);
  readonly dashboardLoading = signal(false);

  // Assignments virtual scroll
  readonly assignments = signal<Assignment[]>([]);
  readonly assignmentsPage = signal(1);
  readonly assignmentsTotal = signal(0);
  readonly assignmentsLoading = signal(false);
  readonly assignmentsHasMore = computed(() => this.assignments().length < this.assignmentsTotal());

  // Quotas virtual scroll
  readonly quotas = signal<QuotaSummary[]>([]);
  readonly quotasPage = signal(1);
  readonly quotasTotal = signal(0);
  readonly quotasLoading = signal(false);
  readonly quotasHasMore = computed(() => this.quotas().length < this.quotasTotal());

  // Credits virtual scroll
  readonly credits = signal<CreditListItem[]>([]);
  readonly creditsPage = signal(1);
  readonly creditsTotal = signal(0);
  readonly creditsLoading = signal(false);
  readonly creditsHasMore = computed(() => this.credits().length < this.creditsTotal());

  readonly terminateForm = this.fb.nonNullable.group({
    terminationDate: ['', Validators.required],
  });

  ngOnInit(): void {
    this.store.loadPayee(this.payeeId);

    // Read period from URL; default is this-month
    const p = this.route.snapshot.queryParamMap.get('period') as PeriodKey | null;
    if (p === 'last-month' || p === 'ytd' || p === 'all-time') this.period.set(p);

    this.loadOverview();
  }

  // ── Tab switching ────────────────────────────────────────────────────────

  setTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  // ── Period filter ─────────────────────────────────────────────────────────

  setPeriod(p: string): void {
    this.period.set(p as PeriodKey);
    this.router.navigate([], { queryParams: { period: p }, queryParamsHandling: 'merge', replaceUrl: true });
    this.resetAndLoad();
  }

  private resetAndLoad(): void {
    this.dashboard.set(null);
    this.assignments.set([]); this.assignmentsPage.set(1); this.assignmentsTotal.set(0);
    this.quotas.set([]); this.quotasPage.set(1); this.quotasTotal.set(0);
    this.credits.set([]); this.creditsPage.set(1); this.creditsTotal.set(0);
    this.loadOverview();
    this.loadMoreAssignments();
    this.loadMoreQuotas();
    this.loadMoreCredits();
  }

  // ── Dashboard (gauges + trend) ────────────────────────────────────────────

  private async loadOverview(): Promise<void> {
    this.dashboardLoading.set(true);
    try {
      const result = await firstValueFrom(this.payeesApi.getPayeeDashboard(this.payeeId, this.period()));
      this.dashboard.set(result);
    } catch {
      this.dashboard.set({ attainmentItems: [], salesTrend: [], recentQuotas: [], recentAssignments: [] });
    } finally {
      this.dashboardLoading.set(false);
      // Load list cards after dashboard
      this.loadMoreAssignments();
      this.loadMoreQuotas();
      this.loadMoreCredits();
    }
  }

  // ── Virtual scroll list cards ─────────────────────────────────────────────

  async loadMoreAssignments(): Promise<void> {
    if (this.assignmentsLoading() || (!this.assignmentsHasMore() && this.assignmentsPage() > 1)) return;
    this.assignmentsLoading.set(true);
    try {
      const result = await firstValueFrom(
        this.payeesApi.getPayeeAssignments(this.payeeId, {
          page: this.assignmentsPage(),
          pageSize: 10,
          sortBy: 'effectivestart',
          sortOrder: 'desc',
          period: this.period(),
        })
      );
      this.assignments.update(prev => [...prev, ...result.items]);
      this.assignmentsTotal.set(result.totalCount);
      this.assignmentsPage.update(p => p + 1);
    } catch { /* silent */ } finally {
      this.assignmentsLoading.set(false);
    }
  }

  async loadMoreQuotas(): Promise<void> {
    if (this.quotasLoading() || (!this.quotasHasMore() && this.quotasPage() > 1)) return;
    this.quotasLoading.set(true);
    try {
      const result = await firstValueFrom(
        this.payeesApi.getPayeeQuotas(this.payeeId, {
          page: this.quotasPage(),
          pageSize: 10,
          sortBy: 'periodstart',
          sortOrder: 'desc',
          period: this.period(),
        })
      );
      this.quotas.update(prev => [...prev, ...result.items]);
      this.quotasTotal.set(result.totalCount);
      this.quotasPage.update(p => p + 1);
    } catch { /* silent */ } finally {
      this.quotasLoading.set(false);
    }
  }

  async loadMoreCredits(): Promise<void> {
    if (this.creditsLoading() || (!this.creditsHasMore() && this.creditsPage() > 1)) return;
    this.creditsLoading.set(true);
    try {
      const result = await firstValueFrom(
        this.payeesApi.getPayeeCredits(this.payeeId, this.creditsPage(), this.period())
      );
      this.credits.update(prev => [...prev, ...result.items]);
      this.creditsTotal.set(result.totalCount);
      this.creditsPage.update(p => p + 1);
    } catch { /* silent */ } finally {
      this.creditsLoading.set(false);
    }
  }

  // ── Helpers ──────────────────────────────────────────────────────────────

  trendBarPoints(trend: SalesTrendPoint[]): BarChartPoint[] {
    if (!trend.length) return [];
    const today = new Date();
    const currentYear = today.getFullYear();
    const currentMonth = today.getMonth() + 1; // 1-based

    // V1: show only the dominant currency (highest total across all months).
    // This avoids mixing EUR + PLN amounts in the same bar axis.
    const totalByCurrency = new Map<string, number>();
    for (const p of trend) {
      totalByCurrency.set(p.currency, (totalByCurrency.get(p.currency) ?? 0) + p.amount);
    }
    const dominantCurrency = [...totalByCurrency.entries()]
      .reduce((a, b) => a[1] >= b[1] ? a : b)[0];

    const byMonth = new Map<string, SalesTrendPoint>();
    for (const p of trend) {
      if (p.currency === dominantCurrency) byMonth.set(`${p.year}-${p.month}`, p);
    }

    return [...byMonth.values()]
      .sort((a, b) => a.year !== b.year ? a.year - b.year : a.month - b.month)
      .map(p => ({
        label: p.monthLabel,
        value: p.amount,
        currency: p.currency,
        isCurrent: p.year === currentYear && p.month === currentMonth,
      }));
  }

  /** Compute pacing fraction for a currently-active quota period. */
  computePacing(periodStart: string, periodEnd: string): number | null {
    const start = new Date(periodStart).getTime();
    const end = new Date(periodEnd).getTime();
    if (this._nowMs < start || this._nowMs > end) return null;
    return Math.min(1, Math.max(0, (this._nowMs - start) / (end - start)));
  }

  gaugeColorClass(value: number): string {
    const pct = value * 100;
    if (pct >= 100) return 'bento-bar--blue';
    if (pct >= 80)  return 'bento-bar--green';
    if (pct >= 50)  return 'bento-bar--amber';
    return 'bento-bar--red';
  }

  // ── Profile + status helpers ──────────────────────────────────────────────

  openEdit(): void { this.editModalOpen.set(true); }
  onEditSaved(_payee: Payee): void { this.editModalOpen.set(false); }

  async onMarkActive(): Promise<void> {
    try { await this.store.markAsActive(this.payeeId); this.toast.show('PAYEES.TOAST_MARKED_ACTIVE', 'success'); }
    catch (err) { this.toast.show(extractApiError(err), 'error'); }
  }

  async onMarkOnLeave(): Promise<void> {
    try { await this.store.markAsOnLeave(this.payeeId); this.toast.show('PAYEES.TOAST_MARKED_ON_LEAVE', 'success'); }
    catch (err) { this.toast.show(extractApiError(err), 'error'); }
  }

  openDeactivate(): void { this.deactivateModalOpen.set(true); }

  async onConfirmDeactivate(): Promise<void> {
    this.deactivateSaving.set(true);
    try {
      await this.store.deactivate(this.payeeId);
      this.toast.show('PAYEES.TOAST_DEACTIVATED', 'success');
      this.deactivateModalOpen.set(false);
    } catch (err) { this.toast.show(extractApiError(err), 'error'); }
    finally { this.deactivateSaving.set(false); }
  }

  async onActivate(): Promise<void> {
    try { await this.store.activate(this.payeeId); this.toast.show('PAYEES.TOAST_ACTIVATED', 'success'); }
    catch (err) { this.toast.show(extractApiError(err), 'error'); }
  }

  openTerminate(): void {
    const today = new Date();
    this.terminateForm.reset({ terminationDate: today.toISOString().slice(0, 10) });
    this.terminateModalOpen.set(true);
  }

  async onConfirmTerminate(): Promise<void> {
    if (this.terminateForm.invalid) { this.terminateForm.markAllAsTouched(); return; }
    const { terminationDate } = this.terminateForm.getRawValue();
    this.saving.set(true);
    try {
      await this.store.markAsTerminated(this.payeeId, terminationDate);
      this.toast.show('PAYEES.TOAST_TERMINATED', 'success');
      this.terminateModalOpen.set(false);
    } catch (err) { this.toast.show(extractApiError(err), 'error'); }
    finally { this.saving.set(false); }
  }

  payeeStatusVariant(status: PayeeStatus): BadgeVariant {
    switch (status) {
      case PayeeStatus.Active: return 'success';
      case PayeeStatus.OnLeave: return 'warning';
      case PayeeStatus.Terminated: return 'neutral';
    }
  }

  payeeStatusKey(status: PayeeStatus): string {
    switch (status) {
      case PayeeStatus.Active: return 'PAYEES.STATUS_ACTIVE';
      case PayeeStatus.OnLeave: return 'PAYEES.STATUS_ON_LEAVE';
      case PayeeStatus.Terminated: return 'PAYEES.STATUS_TERMINATED';
    }
  }

  // Temporal chip: derives state from entity period vs today, not DB status.
  // If isCurrencyValid is false, returns the warning/invalid state regardless.
  temporalVariant(start: string, end: string, isCurrencyValid = true): BadgeVariant {
    if (!isCurrencyValid) return 'warning';
    const today = new Date(); today.setHours(0, 0, 0, 0);
    if (new Date(end) < today)   return 'neutral';  // Closed
    if (new Date(start) > today) return 'info';     // Upcoming
    return 'success';                               // In Progress
  }

  temporalKey(start: string, end: string, isCurrencyValid = true): string {
    if (!isCurrencyValid) return 'DASHBOARD.CHIP_INVALID';
    const today = new Date(); today.setHours(0, 0, 0, 0);
    if (new Date(end) < today)   return 'DASHBOARD.CHIP_CLOSED';
    if (new Date(start) > today) return 'DASHBOARD.CHIP_UPCOMING';
    return 'DASHBOARD.CHIP_IN_PROGRESS';
  }

  currencyMismatchTooltip(quotaCurrency: string, planCurrency: string): string {
    return `Currency mismatch: quota is ${quotaCurrency} but plan is ${planCurrency}. Close and recreate this quota to fix.`;
  }

  hasTerminateError(field: string, error: string): boolean {
    const ctrl = this.terminateForm.get(field);
    return !!(ctrl?.touched && ctrl.hasError(error));
  }
}
