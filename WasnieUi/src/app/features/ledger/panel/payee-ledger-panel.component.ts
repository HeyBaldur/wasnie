import { Component, effect, inject, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LedgerStore } from '../state/ledger.store';
import { ManualAdjustmentType } from '../models/ledger.model';
import { WsCardComponent } from '../../../shared/ui/ws-card/ws-card.component';
import { WsTableComponent } from '../../../shared/ui/ws-table/ws-table.component';
import { WsTableEmptyComponent } from '../../../shared/ui/ws-table/ws-table-empty.component';
import { WsButtonComponent } from '../../../shared/ui/ws-button/ws-button.component';
import { WsInputComponent } from '../../../shared/ui/ws-input/ws-input.component';
import { WsSelectComponent } from '../../../shared/ui/ws-select/ws-select.component';
import { WsSegmentedControlComponent } from '../../../shared/ui/ws-segmented-control/ws-segmented-control.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';

/**
 * The payee's clawback account: the two equations, the ledger, and the adjustment form.
 *
 * ★ MONEY RULE ★ — this component performs NO arithmetic on money. Every figure it prints comes
 * from a field the backend already computed (PayRunSettlement / PayeeBalance). `fmt()` formats and
 * nothing else. In particular `retentionApplied` and `amortization` are the same magnitude with
 * opposite meaning, and both are read from the DTO: deriving one from the other here would make
 * the browser a second source of truth about someone's pay.
 */
@Component({
  selector: 'app-payee-ledger-panel',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslateModule,
    WsCardComponent,
    WsTableComponent,
    WsTableEmptyComponent,
    WsButtonComponent,
    WsInputComponent,
    WsSelectComponent,
    WsSegmentedControlComponent,
    IconComponent,
    HasPermissionPipe,
  ],
  templateUrl: './payee-ledger-panel.component.html',
  styleUrl: './payee-ledger-panel.component.scss',
})
export class PayeeLedgerPanelComponent {
  readonly payeeId = input.required<string>();

  readonly store = inject(LedgerStore);
  private readonly fb = inject(FormBuilder);
  private readonly translate = inject(TranslateService);

  readonly showAdjustmentForm = signal(false);

  /** Justification is required here too, but the DOMAIN is what enforces it (see the handler). */
  readonly form = this.fb.nonNullable.group({
    transactionType: ['ClawbackForgivenessCredit' as ManualAdjustmentType, Validators.required],
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    justification: ['', [Validators.required, Validators.maxLength(1000)]],
  });

  readonly adjustmentTypes = [
    { value: 'ClawbackForgivenessCredit', label: 'LEDGER.TYPE_CLAWBACK_FORGIVENESS_CREDIT' },
    { value: 'ManualBonusCredit', label: 'LEDGER.TYPE_MANUAL_BONUS_CREDIT' },
    { value: 'DataCorrectionDebit', label: 'LEDGER.TYPE_DATA_CORRECTION_DEBIT' },
  ];

  constructor() {
    effect(() => {
      const id = this.payeeId();
      if (id) void this.store.load(id);
    });
  }

  /** Formatting only — never a calculation. */
  fmt(amount: number, currency: string): string {
    return amount.toLocaleString(this.translate.currentLang || 'en', {
      style: 'currency',
      currency,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  }

  /** Absolute display for the cash-flow row, where the minus sign lives in the operator. */
  fmtAbs(amount: number, currency: string): string {
    return this.fmt(Math.abs(amount), currency);
  }

  /** Explicit sign for the balance row, where the contrast is the lesson. */
  fmtSigned(amount: number, currency: string): string {
    const formatted = this.fmt(Math.abs(amount), currency);
    return amount < 0 ? `−${formatted}` : `+${formatted}`;
  }

  typeLabelKey(transactionType: string): string {
    return `LEDGER.TYPE_${this.toScreamingSnake(transactionType)}`;
  }

  isSystem(origin: string): boolean {
    return origin === 'System';
  }

  isCredit(amount: number): boolean {
    return amount > 0;
  }

  currencyOptions() {
    return this.store.currencies().map((c) => ({ value: c, label: c }));
  }

  toggleForm(): void {
    this.showAdjustmentForm.update((v) => !v);
    if (!this.showAdjustmentForm()) this.form.reset({
      transactionType: 'ClawbackForgivenessCredit',
      amount: null,
      justification: '',
    });
  }

  async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const currency = this.store.activeStatement()?.currency ?? this.store.currencies()[0];
    if (!currency) return;

    const raw = this.form.getRawValue();
    const ok = await this.store.createAdjustment(this.payeeId(), {
      transactionType: raw.transactionType,
      amount: raw.amount!,
      currency,
      justification: raw.justification,
    });

    if (ok) this.toggleForm();
  }

  private toScreamingSnake(value: string): string {
    return value.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toUpperCase();
  }
}
