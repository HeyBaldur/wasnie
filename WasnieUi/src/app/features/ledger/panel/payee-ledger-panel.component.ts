import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { LedgerStore } from '../state/ledger.store';
import { ManualAdjustmentType, PayeeStatement } from '../models/ledger.model';
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

  /** Corrections a person can make at any time, whatever the balance says. */
  private static readonly GENERAL_TYPES = [
    { value: 'ClawbackForgivenessCredit', label: 'LEDGER.TYPE_CLAWBACK_FORGIVENESS_CREDIT' },
    { value: 'ManualBonusCredit', label: 'LEDGER.TYPE_MANUAL_BONUS_CREDIT' },
    { value: 'DataCorrectionDebit', label: 'LEDGER.TYPE_DATA_CORRECTION_DEBIT' },
    { value: 'DataCorrectionCredit', label: 'LEDGER.TYPE_DATA_CORRECTION_CREDIT' },
  ];

  /**
   * Closing an account only makes sense in the direction the balance actually points, so the
   * available closing types follow its SIGN:
   *   owes us  → the debt is recovered elsewhere, or absorbed as a loss;
   *   we owe   → treasury pays it outside Wasnie and we record the payment.
   * Offering both directions at once would let someone "write off" money the company OWES, which is
   * not a write-off — it is not paying somebody.
   */
  readonly adjustmentTypes = computed(() => {
    const balance = this.store.activeStatement()?.currentBalance ?? 0;
    const closing =
      balance < 0
        ? [
            { value: 'ExternalSettlementCredit', label: 'LEDGER.TYPE_EXTERNAL_SETTLEMENT_CREDIT' },
            { value: 'WriteOffCredit', label: 'LEDGER.TYPE_WRITE_OFF_CREDIT' },
          ]
        : balance > 0
          ? [{ value: 'FinalSettlementDebit', label: 'LEDGER.TYPE_FINAL_SETTLEMENT_DEBIT' }]
          : [];

    return [...PayeeLedgerPanelComponent.GENERAL_TYPES, ...closing];
  });

  /**
   * The type currently selected in the form, as a signal so the amount rules below can react to it.
   * (The control is the source of truth; this only mirrors it.)
   */
  private readonly selectedType = signal<ManualAdjustmentType>('ClawbackForgivenessCredit');

  /**
   * A FinalSettlementDebit must equal the live balance EXACTLY — the domain rejects anything else
   * (`PayeeBalance.Apply`: a closing is total, or it is not a closing). Leaving the field free would
   * make finance discover that rule by crashing into 400s, so the amount is filled in and locked.
   *
   * This is convenience, NOT the guarantee: the domain still decides. Nothing here validates money.
   */
  readonly amountIsLocked = computed(
    () =>
      this.selectedType() === 'FinalSettlementDebit' &&
      (this.store.activeStatement()?.currentBalance ?? 0) > 0,
  );

  /**
   * The debt-closing types are pre-filled with the outstanding debt but stay EDITABLE: recovering or
   * writing off PART of a debt is legitimate and the domain allows it. Saying so on screen stops the
   * suggested figure from reading as a requirement.
   */
  readonly amountIsPrefilled = computed(
    () =>
      (this.selectedType() === 'ExternalSettlementCredit' ||
        this.selectedType() === 'WriteOffCredit') &&
      (this.store.activeStatement()?.currentBalance ?? 0) < 0,
  );

  constructor() {
    effect(() => {
      const id = this.payeeId();
      if (id) void this.store.load(id);
    });

    this.form.controls.transactionType.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((type) => this.applyAmountRuleFor(type));
  }

  /**
   * What the amount field does when the type changes. The three closing types do NOT behave alike,
   * because the domain does not treat them alike:
   *
   *   FinalSettlementDebit  → must EQUAL the positive balance. Pre-filled and LOCKED.
   *   ExternalSettlementCredit / WriteOffCredit → a partial recovery or a partial write-off is
   *       legitimate (the domain places no cap on them), so the debt is offered as a starting point
   *       and stays EDITABLE. Pre-filling without locking is the honest shape of "usually all of it".
   *
   * Any other type: the operator is on their own, as before.
   */
  private applyAmountRuleFor(type: ManualAdjustmentType): void {
    this.selectedType.set(type);

    const amount = this.form.controls.amount;
    const balance = this.store.activeStatement()?.currentBalance ?? 0;

    if (type === 'FinalSettlementDebit' && balance > 0) {
      amount.setValue(balance);
      amount.disable();
      return;
    }

    // Coming back from a locked state, or moving to a type that is typed by hand.
    if (amount.disabled) amount.enable();

    if ((type === 'ExternalSettlementCredit' || type === 'WriteOffCredit') && balance < 0) {
      amount.setValue(Math.abs(balance));
      return;
    }

    amount.setValue(null);
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

  /** Absolute display for the cash-flow row, where the minus sign lives in the operator.
   *  Null means "there is no settled run to describe" — an em dash, never a fabricated 0. */
  fmtAbs(amount: number | null, currency: string): string {
    if (amount === null) return '—';
    return this.fmt(Math.abs(amount), currency);
  }

  /** Explicit sign for the balance row, where the contrast is the lesson. */
  fmtSigned(amount: number | null, currency: string): string {
    if (amount === null) return '—';
    const formatted = this.fmt(Math.abs(amount), currency);
    return amount < 0 ? `−${formatted}` : `+${formatted}`;
  }

  typeLabelKey(transactionType: string): string {
    return `LEDGER.TYPE_${this.toScreamingSnake(transactionType)}`;
  }

  /**
   * The sentence under the big number has to agree with the number's SIGN. It used to be one static
   * line — "what this payee owes" — which is simply false on a positive balance: nobody owes
   * anything, the company owes THEM (usually because a pay run withheld more than the debt).
   */
  balanceHintKey(balance: number): string {
    if (balance < 0) return 'LEDGER.CURRENT_BALANCE_HINT_DEBT';
    if (balance > 0) return 'LEDGER.CURRENT_BALANCE_HINT_CREDIT';
    return 'LEDGER.CURRENT_BALANCE_HINT_SETTLED';
  }

  /**
   * How much the balance moved AFTER the run in the snapshot. The two figures on screen — the live
   * balance and the run's carryover — legitimately differ whenever an entry landed later, and a
   * reader with no explanation for the gap concludes one of them is wrong.
   *
   * Not money arithmetic: both operands arrive finished from the server and this only subtracts them
   * to describe the difference the reader is already looking at.
   */
  movementsAfterRun(st: PayeeStatement): number {
    if (st.newCarryover === null) return 0;
    return st.currentBalance - st.newCarryover;
  }

  hasMovementsAfterRun(st: PayeeStatement): boolean {
    return this.movementsAfterRun(st) !== 0;
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
    if (!this.showAdjustmentForm()) {
      // Re-enable first: a control left disabled by a final settlement would come back locked with a
      // stale amount the next time the form is opened, on a balance that has since moved.
      this.form.controls.amount.enable();
      this.selectedType.set('ClawbackForgivenessCredit');
      this.form.reset({
        transactionType: 'ClawbackForgivenessCredit',
        amount: null,
        justification: '',
      });
    }
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
