import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  untracked,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { debounceTime, distinctUntilChanged, map } from 'rxjs/operators';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { TransactionStatus } from '../models/transaction.model';
import { TransactionFilter } from '../state/transactions.store';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsInputComponent,
  WsSelectComponent,
  WsDatePickerComponent,
  type SelectOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-transaction-filter',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    IconComponent,
    WsButtonComponent,
    WsBadgeComponent,
    WsCardComponent,
    WsInputComponent,
    WsSelectComponent,
    WsDatePickerComponent,
  ],
  templateUrl: './transaction-filter.component.html',
  styleUrl: './transaction-filter.component.scss',
})
export class TransactionFilterComponent implements OnInit {
  private readonly payeesApi = inject(PayeesApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly filter = input.required<TransactionFilter>();
  readonly activeFilterCount = input(0);
  readonly filterChange = output<Partial<TransactionFilter>>();
  readonly cleared = output<void>();

  readonly panelOpen = signal(false);
  readonly activeStatuses = signal<TransactionStatus[]>([]);
  readonly selectedPayees = signal<{ id: string; label: string }[]>([]);
  readonly selectedCurrencies = signal<string[]>([]);

  private static readonly _ALL_CURRENCIES: SelectOption[] = [
    'EUR', 'USD', 'GBP', 'PLN', 'CHF', 'JPY', 'CAD', 'AUD',
    'NOK', 'SEK', 'DKK', 'CZK', 'HUF', 'RON', 'BGN', 'MXN', 'BRL',
  ].map(c => ({ value: c, label: c }));

  readonly availableCurrencyOptions = computed<SelectOption[]>(() => {
    const selected = new Set(this.selectedCurrencies());
    return TransactionFilterComponent._ALL_CURRENCIES.filter(o => !selected.has(o.value as string));
  });

  // id → "FullName (EMP000)" — populated when search results arrive or a payee is fetched by id.
  private readonly _payeeCache = new Map<string, string>();

  private _patching = false;

  readonly form = new FormGroup({
    reference: new FormControl('', { nonNullable: true }),
    txDateFrom: new FormControl<string | null>(null),
    txDateTo: new FormControl<string | null>(null),
    ingestedFrom: new FormControl<string | null>(null),
    ingestedTo: new FormControl<string | null>(null),
    amountMin: new FormControl<string>('', { nonNullable: true }),
    amountMax: new FormControl<string>('', { nonNullable: true }),
    amountSort: new FormControl<string>('', { nonNullable: true }),
    payeeSearch: new FormControl<string | number>('', { nonNullable: true }),
    currencySearch: new FormControl<string | number>('', { nonNullable: true }),
  });

  readonly amountSortOptions: SelectOption[] = [
    { value: '', label: 'TRANSACTIONS.FILTER.AMT_SORT_NONE' },
    { value: 'asc', label: 'TRANSACTIONS.FILTER.AMT_SORT_ASC' },
    { value: 'desc', label: 'TRANSACTIONS.FILTER.AMT_SORT_DESC' },
  ];

  readonly STATUS_OPTIONS: TransactionStatus[] = [
    TransactionStatus.Pending,
    TransactionStatus.Calculated,
    TransactionStatus.Paid,
    TransactionStatus.Cancelled,
  ];

  readonly payeeSearchFn = (q: string) =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: q }).pipe(
      map(r => r.items.map(p => {
        const label = `${p.fullName} (${p.employeeCode})`;
        this._payeeCache.set(p.id, label);
        return { value: p.id, label };
      })),
    );

  constructor() {
    // Sync form controls when filter input changes (URL load, clear, tab click).
    // allowSignalWrites: true because we update local signals (activeStatuses, selectedPayees).
    effect(() => {
      const f = this.filter();
      if (this._patching) return;
      this._patching = true;
      this.form.patchValue({
        reference: f.reference,
        txDateFrom: f.txDateFrom,
        txDateTo: f.txDateTo,
        ingestedFrom: f.ingestedFrom,
        ingestedTo: f.ingestedTo,
        amountMin: f.amountMin !== null ? String(f.amountMin) : '',
        amountMax: f.amountMax !== null ? String(f.amountMax) : '',
        amountSort: f.amountSort ?? '',
      }, { emitEvent: false });
      this.activeStatuses.set([...f.statuses]);
      this.selectedCurrencies.set([...f.currencies]);
      // Sync payee chips — preserve known labels, then check cache, then fall back to GUID
      const known = untracked(() => this.selectedPayees()); // untracked: avoids re-triggering effect
      this.selectedPayees.set(f.payeeIds.map(id => ({
        id,
        label: known.find(p => p.id === id && p.label !== p.id)?.label
          ?? this._payeeCache.get(id)
          ?? id,
      })));
      // Fetch names for any payeeId still showing as a raw GUID (e.g. loaded from URL params)
      const unresolved = f.payeeIds.filter(id => !this._payeeCache.has(id));
      if (unresolved.length > 0) untracked(() => this._resolvePayeeNames(unresolved));
      this._patching = false;
    }, { allowSignalWrites: true });
  }

  ngOnInit(): void {
    if (this.activeFilterCount() > 0) this.panelOpen.set(true);

    const c = this.form.controls;

    c.reference.valueChanges.pipe(
      debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef),
    ).subscribe(v => { if (!this._patching) this.filterChange.emit({ reference: v }); });

    c.amountMin.valueChanges.pipe(
      debounceTime(400), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef),
    ).subscribe(v => {
      if (!this._patching) this.filterChange.emit({ amountMin: v ? (parseFloat(v) || null) : null });
    });

    c.amountMax.valueChanges.pipe(
      debounceTime(400), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef),
    ).subscribe(v => {
      if (!this._patching) this.filterChange.emit({ amountMax: v ? (parseFloat(v) || null) : null });
    });

    c.txDateFrom.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => { if (!this._patching) this.filterChange.emit({ txDateFrom: v }); });

    c.txDateTo.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => { if (!this._patching) this.filterChange.emit({ txDateTo: v }); });

    c.ingestedFrom.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => { if (!this._patching) this.filterChange.emit({ ingestedFrom: v }); });

    c.ingestedTo.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => { if (!this._patching) this.filterChange.emit({ ingestedTo: v }); });

    c.amountSort.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!this._patching) {
          this.filterChange.emit({ amountSort: v === 'asc' || v === 'desc' ? v : null });
        }
      });

    c.payeeSearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v || this._patching) return;
        const idStr = String(v);
        if (this.filter().payeeIds.includes(idStr)) return;
        const label = this._payeeCache.get(idStr) ?? idStr;
        this.selectedPayees.update(ps => [...ps, { id: idStr, label }]);
        this.filterChange.emit({ payeeIds: [...this.filter().payeeIds, idStr] });
        setTimeout(() => {
          this._patching = true;
          c.payeeSearch.setValue('', { emitEvent: false });
          this._patching = false;
        }, 0);
      });

    c.currencySearch.valueChanges.pipe(distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(v => {
        if (!v || this._patching) return;
        const code = String(v);
        if (this.filter().currencies.includes(code)) return;
        this.selectedCurrencies.update(cs => [...cs, code]);
        this.filterChange.emit({ currencies: [...this.filter().currencies, code] });
        setTimeout(() => {
          this._patching = true;
          c.currencySearch.setValue('', { emitEvent: false });
          this._patching = false;
        }, 0);
      });
  }

  private _resolvePayeeNames(ids: string[]): void {
    ids.forEach(id => {
      this.payeesApi.getPayee(id).pipe(
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(payee => {
        const label = `${payee.fullName} (${payee.employeeCode})`;
        this._payeeCache.set(id, label);
        this.selectedPayees.update(chips =>
          chips.map(c => c.id === id ? { ...c, label } : c)
        );
      });
    });
  }

  togglePanel(): void { this.panelOpen.update(v => !v); }

  isStatusActive(s: TransactionStatus): boolean {
    return this.activeStatuses().includes(s);
  }

  toggleStatus(s: TransactionStatus): void {
    const current = this.activeStatuses();
    const next = current.includes(s) ? current.filter(x => x !== s) : [...current, s];
    this.activeStatuses.set(next);
    this.filterChange.emit({ statuses: next });
  }

  removePayee(id: string): void {
    this.selectedPayees.update(ps => ps.filter(p => p.id !== id));
    this.filterChange.emit({ payeeIds: this.filter().payeeIds.filter(p => p !== id) });
  }

  removeCurrency(code: string): void {
    this.selectedCurrencies.update(cs => cs.filter(c => c !== code));
    this.filterChange.emit({ currencies: this.filter().currencies.filter(c => c !== code) });
  }

  onUnassignedToggle(): void {
    this.filterChange.emit({ unassignedOnly: !this.filter().unassignedOnly });
  }

  onClear(): void {
    this._patching = true;
    // emitEvent: true (default) — replaces any pending debounce timers so stale values
    // from before the clear never fire. Non-debounced subscribers are blocked by _patching.
    this.form.reset({
      reference: '', txDateFrom: null, txDateTo: null,
      ingestedFrom: null, ingestedTo: null,
      amountMin: '', amountMax: '', amountSort: '', payeeSearch: '', currencySearch: '',
    });
    this.activeStatuses.set([]);
    this.selectedCurrencies.set([]);
    this.selectedPayees.set([]);
    this._patching = false;
    this.cleared.emit();
  }

  statusKey(s: TransactionStatus): string {
    return `TRANSACTIONS.STATUS_${s.toUpperCase()}`;
  }
}
