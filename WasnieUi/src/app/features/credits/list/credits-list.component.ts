import { Component, computed, inject, OnInit, signal, DestroyRef } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged, map } from 'rxjs/operators';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
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
    AppShellComponent, RouterLink, ReactiveFormsModule, TranslateModule, DecimalPipe,
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
  });

  readonly payeeSearchFn = (q: string) =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.fullName} (${p.employeeCode})`;
        this._payeeCache.set(p.id, label);
        return { value: p.id, label };
      })),
    );

  readonly planSearchFn = (q: string) =>
    this.plansApi.getPlans({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.name} v${p.version}`;
        return { value: p.id, label };
      })),
    );

  ngOnInit(): void {
    const qp = this.route.snapshot.queryParams as Record<string, string>;
    if (Object.keys(qp).length > 0) {
      this.store.loadFromQueryParams(qp);
    }
    void this.store.loadAll();
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
    this.selectedPlans.set(f.planIds.map(id => ({ id, label: id })));
    if (f.payeeIds.length > 0) this.filterOpen.set(true);
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
        this.selectedPlans.update(ps => [...ps, { id: idStr, label: idStr }]);
        this._setFilter({ planIds: [...this.store.filter().planIds, idStr] });
        setTimeout(() => c.planSearch.setValue('', { emitEvent: false }), 0);
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
    this._setFilter({ planIds: this.store.filter().planIds.filter(x => x !== id) });
  }

  clearFilters(): void {
    this.store.clearFilters();
    this.form.reset({
      reference: '', allocatedFrom: null, allocatedTo: null,
      amountMin: '', amountMax: '', status: 'Active',
      payeeSearch: '', planSearch: '',
    });
    this.selectedPayees.set([]);
    this.selectedPlans.set([]);
    this._syncUrl();
    void this.store.loadCounters();
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
