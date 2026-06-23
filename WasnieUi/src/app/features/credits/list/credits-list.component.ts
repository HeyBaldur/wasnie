import { Component, computed, inject, OnInit, signal, DestroyRef } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged, map } from 'rxjs/operators';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { CreditsApiService } from '../services/credits.api.service';
import { CreditsStore, CreditFilter, CreditStatus, EMPTY_CREDIT_FILTER } from '../state/credits.store';
import { CreditByPayee } from '../models/credit.model';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsInputComponent,
  WsSelectComponent,
  WsDatePickerComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsEmptyStateComponent,
  WsPaginationComponent,
  type BadgeVariant,
  type SelectOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-credits-list',
  standalone: true,
  imports: [
    AppShellComponent, RefreshOnEnterDirective, RouterLink, ReactiveFormsModule, TranslateModule, DecimalPipe,
    IconComponent, DateFormatPipe, CurrencyFormatPipe, HasPermissionDirective,
    WsButtonComponent, WsBadgeComponent, WsCardComponent,
    WsInputComponent, WsSelectComponent, WsDatePickerComponent,
    WsPageLayoutComponent, WsTableComponent, WsTableEmptyComponent,
    WsEmptyStateComponent, WsPaginationComponent,
  ],
  templateUrl: './credits-list.component.html',
  styleUrl: './credits-list.component.scss',
})
export class CreditsListComponent implements OnInit {
  readonly store = inject(CreditsStore);
  private readonly api = inject(CreditsApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly payeesApi = inject(PayeesApiService);
  private readonly plansApi = inject(PlansApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly viewMode = signal<'table' | 'byPayee'>('table');
  readonly filterOpen = signal(false);

  // Payee chip resolution cache (FIX-11 pattern)
  private readonly _payeeCache = new Map<string, string>();
  readonly selectedPayees = signal<{ id: string; label: string }[]>([]);
  readonly selectedPlans = signal<{ id: string; label: string }[]>([]);

  // Currency dropdown + chips
  readonly selectedCurrencies = signal<string[]>([]);

  private static readonly _ALL_CURRENCIES: SelectOption[] = [
    'EUR', 'USD', 'GBP', 'PLN', 'CHF', 'JPY', 'CAD', 'AUD',
    'NOK', 'SEK', 'DKK', 'CZK', 'HUF', 'RON', 'BGN', 'MXN', 'BRL',
  ].map(c => ({ value: c, label: c }));

  readonly availableCurrencyOptions = computed<SelectOption[]>(() => {
    const selected = new Set(this.selectedCurrencies());
    return CreditsListComponent._ALL_CURRENCIES.filter(o => !selected.has(o.value as string));
  });

  // Rule dropdown + chips — options loaded from selected plans
  readonly availableRules = signal<{ id: string; label: string; planId: string; planName: string }[]>([]);
  readonly selectedRules = signal<{ id: string; label: string }[]>([]);
  readonly rulesLoading = signal(false);

  readonly availableRuleOptions = computed<SelectOption[]>(() => {
    const selected = new Set(this.store.filter().ruleIds);
    return this.availableRules()
      .filter(r => !selected.has(r.id))
      .map(r => ({ value: r.id, label: r.label }));
  });

  readonly statusOptions: SelectOption[] = [
    { value: 'Active', label: 'CREDITS.FILTER.STATUS_ACTIVE' },
    { value: 'Superseded', label: 'CREDITS.FILTER.STATUS_SUPERSEDED' },
    { value: 'All', label: 'CREDITS.FILTER.STATUS_ALL' },
  ];

  readonly form = new FormGroup({
    reference: new FormControl('', { nonNullable: true }),
    allocatedFrom: new FormControl<string | null>(null),
    allocatedTo: new FormControl<string | null>(null),
    amountMin: new FormControl<string>('', { nonNullable: true }),
    amountMax: new FormControl<string>('', { nonNullable: true }),
    status: new FormControl<string>('Active', { nonNullable: true }),
    payeeSearch: new FormControl<string | number>('', { nonNullable: true }),
    planSearch: new FormControl<string | number>('', { nonNullable: true }),
    currencySearch: new FormControl<string | number>('', { nonNullable: true }),
    ruleSearch: new FormControl<string | number>('', { nonNullable: true }),
  });

  readonly payeeSearchFn = (q: string) =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.fullName} (${p.employeeCode})`;
        this._payeeCache.set(p.id, label);
        return { value: p.id, label };
      })),
    );

  private readonly _planLabelCache = new Map<string, string>();

  readonly planSearchFn = (q: string) =>
    this.plansApi.getPlans({ page: 1, pageSize: 20, search: q, filters: { statuses: 'Active,Archived' } }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.name} v${p.version}`;
        this._planLabelCache.set(p.id, label);
        return {
          value: p.id,
          label,
          badge: {
            text: `PLANS.STATUS_${p.status.toUpperCase()}`,
            variant: (p.status === 'Active' ? 'success' : 'neutral') as BadgeVariant,
          },
        };
      })),
    );

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParams as Record<string, string>;
    if (Object.keys(qp).length > 0) {
      this.store.loadFromQueryParams(qp);
    }
    // First load handled by the store's constructor effect; re-entry refresh by [refreshOnEnter].
    this._syncFormFromStore();
    this._wireFormSubscriptions();
  }

  private _syncFormFromStore(): void {
    const f = this.store.filter();
    this.form.patchValue({
      reference: f.reference,
      allocatedFrom: f.allocatedFrom,
      allocatedTo: f.allocatedTo,
      amountMin: f.amountMin !== null ? String(f.amountMin) : '',
      amountMax: f.amountMax !== null ? String(f.amountMax) : '',
      status: f.status,
    }, { emitEvent: false });
    this.selectedPayees.set(f.payeeIds.map(id => ({ id, label: this._payeeCache.get(id) ?? id })));
    this.selectedPlans.set(f.planIds.map(id => ({ id, label: this._planLabelCache.get(id) ?? id })));
    this.selectedCurrencies.set([...f.currencies]);
    if (f.ruleIds.length > 0) {
      this.selectedRules.set(f.ruleIds.map(id => ({
        id, label: this.availableRules().find(r => r.id === id)?.label ?? id,
      })));
    }
    if (f.payeeIds.length > 0 || f.currencies.length > 0 || f.ruleIds.length > 0) {
      this.filterOpen.set(true);
    }
  }

  private _wireFormSubscriptions(): void {
    const c = this.form.controls;

    c.reference.valueChanges.pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this._setFilter({ reference: v }));

    c.allocatedFrom.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this._setFilter({ allocatedFrom: v }));

    c.allocatedTo.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this._setFilter({ allocatedTo: v }));

    c.amountMin.valueChanges.pipe(debounceTime(400), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this._setFilter({ amountMin: v ? (parseFloat(v) || null) : null }));

    c.amountMax.valueChanges.pipe(debounceTime(400), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this._setFilter({ amountMax: v ? (parseFloat(v) || null) : null }));

    c.status.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => this._setFilter({ status: (v as CreditStatus) }));

    c.payeeSearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v) return;
        const idStr = String(v);
        if (this.store.filter().payeeIds.includes(idStr)) return;
        const label = this._payeeCache.get(idStr) ?? idStr;
        this.selectedPayees.update(ps => [...ps, { id: idStr, label }]);
        this._setFilter({ payeeIds: [...this.store.filter().payeeIds, idStr] });
        setTimeout(() => c.payeeSearch.setValue('', { emitEvent: false }), 0);
      });

    c.planSearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v) return;
        const idStr = String(v);
        if (this.store.filter().planIds.includes(idStr)) return;
        const planLabel = this._planLabelCache.get(idStr) ?? idStr;
        this.selectedPlans.update(ps => [...ps, { id: idStr, label: planLabel }]);
        this._setFilter({ planIds: [...this.store.filter().planIds, idStr] });
        this._loadRulesForPlan(idStr);
        setTimeout(() => c.planSearch.setValue('', { emitEvent: false }), 0);
      });

    c.currencySearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v) return;
        const code = String(v);
        if (this.store.filter().currencies.includes(code)) return;
        this.selectedCurrencies.update(cs => [...cs, code]);
        this._setFilter({ currencies: [...this.store.filter().currencies, code] });
        setTimeout(() => c.currencySearch.setValue('', { emitEvent: false }), 0);
      });

    c.ruleSearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v) return;
        const ruleId = String(v);
        if (this.store.filter().ruleIds.includes(ruleId)) return;
        const label = this.availableRules().find(r => r.id === ruleId)?.label ?? ruleId;
        this.selectedRules.update(rs => [...rs, { id: ruleId, label }]);
        this._setFilter({ ruleIds: [...this.store.filter().ruleIds, ruleId] });
        setTimeout(() => c.ruleSearch.setValue('', { emitEvent: false }), 0);
      });
  }

  private _setFilter(partial: Partial<CreditFilter>): void {
    this.store.setFilter(partial);
    this._syncUrl();
    void this.store.loadCounters();
  }

  removePayee(id: string): void {
    this.selectedPayees.update(ps => ps.filter(p => p.id !== id));
    this._setFilter({ payeeIds: this.store.filter().payeeIds.filter(x => x !== id) });
  }

  removePlan(id: string): void {
    this.selectedPlans.update(ps => ps.filter(p => p.id !== id));
    const newPlanIds = this.store.filter().planIds.filter(x => x !== id);
    // Remove available and selected rules that belong to the removed plan
    const removedRuleIds = new Set(this.availableRules().filter(r => r.planId === id).map(r => r.id));
    this.availableRules.update(rs => rs.filter(r => r.planId !== id));
    this.selectedRules.update(rs => rs.filter(r => !removedRuleIds.has(r.id)));
    const newRuleIds = this.store.filter().ruleIds.filter(rid => !removedRuleIds.has(rid));
    this._setFilter({ planIds: newPlanIds, ruleIds: newRuleIds });
  }

  removeCurrency(code: string): void {
    this.selectedCurrencies.update(cs => cs.filter(c => c !== code));
    this._setFilter({ currencies: this.store.filter().currencies.filter(x => x !== code) });
  }

  removeRule(id: string): void {
    this.selectedRules.update(rs => rs.filter(r => r.id !== id));
    this._setFilter({ ruleIds: this.store.filter().ruleIds.filter(x => x !== id) });
  }

  clearFilters(): void {
    this.store.clearFilters();
    this.form.reset({
      reference: '', allocatedFrom: null, allocatedTo: null,
      amountMin: '', amountMax: '', status: 'Active',
      payeeSearch: '', planSearch: '', currencySearch: '', ruleSearch: '',
    });
    this.selectedPayees.set([]);
    this.selectedPlans.set([]);
    this.selectedCurrencies.set([]);
    this.availableRules.set([]);
    this.selectedRules.set([]);
    this._syncUrl();
    void this.store.loadCounters();
  }

  // ── Rule loading ─────────────────────────────────────────────────────────

  private _loadRulesForPlan(planId: string): void {
    this.rulesLoading.set(true);
    this.plansApi.getPlan(planId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: plan => {
        const planLabel = this._planLabelCache.get(planId) ?? plan.name;
        // Update plan label in selectedPlans with the real name
        this.selectedPlans.update(ps => ps.map(p => p.id === planId ? { ...p, label: planLabel } : p));
        const newRules = plan.rules
          .filter(r => r.isActive)
          .map(r => ({ id: r.id, label: r.name, planId, planName: planLabel }));
        this.availableRules.update(rs => [
          ...rs.filter(r => r.planId !== planId), // replace if already loaded
          ...newRules,
        ]);
        this.rulesLoading.set(false);
      },
      error: () => this.rulesLoading.set(false),
    });
  }

  readonly exporting = signal(false);
  readonly exportError = signal<string | null>(null);
  private static readonly EXPORT_CONFIRM_THRESHOLD = 50_000;

  async onExport(): Promise<void> {
    if (this.exporting()) return;

    const total = this.store.totalCount();
    if (total > CreditsListComponent.EXPORT_CONFIRM_THRESHOLD) {
      const msg = `This export contains ${total.toLocaleString()} rows and may take a moment. Continue?`;
      if (!window.confirm(msg)) return;
    }

    this.exporting.set(true);
    this.exportError.set(null);
    try {
      const blob = await firstValueFrom(this.api.exportToExcel(this.store.toExportParams()));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `credits-export-${new Date().toISOString().slice(0, 10)}.xlsx`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      this.exportError.set('CREDITS.EXPORT.ERROR');
    } finally {
      this.exporting.set(false);
    }
  }

  setViewMode(mode: 'table' | 'byPayee'): void {
    this.viewMode.set(mode);
    if (mode === 'byPayee') void this.store.loadByPayee();
  }

  openPayeeFilter(payeeId: string): void {
    this.viewMode.set('table');
    const label = this.store.byPayee().find(p => p.payeeId === payeeId)
      ?.payeeName ?? payeeId;
    this.selectedPayees.set([{ id: payeeId, label: label + (this.store.byPayee().find(p => p.payeeId === payeeId)?.payeeCode ? ` (${this.store.byPayee().find(p => p.payeeId === payeeId)?.payeeCode})` : '') }]);
    this._setFilter({ payeeIds: [payeeId] });
  }

  goToPage(n: number): void { this.store.setPage(n); }
  goToPageSize(n: number): void { this.store.setPageSize(n); }

  statusBadge(isSuperseded: boolean): BadgeVariant {
    return isSuperseded ? 'neutral' : 'success';
  }

  statusLabel(isSuperseded: boolean): string {
    return isSuperseded ? 'CREDITS.STATUS_SUPERSEDED' : 'CREDITS.STATUS_ACTIVE';
  }

  private _syncUrl(): void {
    const qp = this.store.toQueryParams();
    const suffix = Object.keys(qp).length > 0 ? '?' + new URLSearchParams(qp).toString() : '';
    window.history.replaceState(null, '', window.location.pathname + suffix);
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }
}
