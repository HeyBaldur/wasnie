import { Component, computed, inject, signal } from '@angular/core';
import { SidebarBadgesStore } from '../../../core/navigation/sidebar-badges.store';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { TerminatedAccountsStore } from '../state/terminated-accounts.store';
import { LedgerApiService } from '../services/ledger.api.service';
import {
  AccountClosureResolution,
  TerminatedPayeeBalance,
} from '../models/ledger.model';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { WsPageLayoutComponent } from '../../../shared/ui/ws-page-layout/ws-page-layout.component';
import { WsTableComponent } from '../../../shared/ui/ws-table/ws-table.component';
import { WsEmptyStateComponent } from '../../../shared/ui/ws-empty-state/ws-empty-state.component';
import { WsButtonComponent } from '../../../shared/ui/ws-button/ws-button.component';
import { WsCardComponent } from '../../../shared/ui/ws-card/ws-card.component';
import {
  WsModalComponent,
  WsSelectComponent,
  WsTextareaComponent,
  type SelectOption,
} from '../../../shared/ui';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';

/**
 * The accounts nobody is processing any more: payees who have LEFT with a balance that is not zero.
 *
 * This screen is the other half of the termination circuit breaker. Taking a departed payee out of
 * every future pay run is right — they will earn nothing more — but on its own it would also make
 * their debt invisible, and invisible debt is how debt quietly disappears. Finance needs to see
 * exactly these people and close each account deliberately.
 *
 * ★ IT NOW SHOWS BOTH KINDS OF OPEN ITEM. A non-zero ledger balance was the only thing that used to
 * put a payee on this list, and that left a hole with money in it: the ledger records what a payee
 * OWES, so commission they EARNED and were never paid produces no balance row at all. Someone owed
 * €3,869.34 appeared nowhere — skipped by the pay run for being terminated, and "settled" here
 * (docs/DIAG_POL-8554_PAYOUT_Y_CREDITOS_INVENTADOS.md).
 *
 * Nothing here computes money: the balances, the credit amounts and the per-currency totals all arrive
 * finished from the server, and the row links to the payee's ledger, where the closing entry is written
 * through the existing adjustment flow. This screen reports; it does not pay.
 */
@Component({
  selector: 'app-terminated-accounts',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    TranslateModule,
    AppShellComponent,
    WsPageLayoutComponent,
    WsTableComponent,
    WsEmptyStateComponent,
    WsButtonComponent,
    WsCardComponent,
    WsModalComponent,
    WsSelectComponent,
    WsTextareaComponent,
    ReactiveFormsModule,
    IconComponent,
    HasPermissionPipe,
    CurrencyFormatPipe,
  ],
  templateUrl: './terminated-accounts.component.html',
  styleUrl: './terminated-accounts.component.scss',
})
export class TerminatedAccountsComponent {
  /** Shared with the dashboard card that counts the same queue — one fetch, one definition. */
  private readonly store = inject(TerminatedAccountsStore);

  readonly rows = this.store.rows;
  /** Server-computed, one per currency. Nothing on this screen adds money. */
  readonly totals = this.store.totals;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly count = this.store.count;

  constructor() {
    void this.load();
  }

  load(): Promise<void> {
    return this.store.load();
  }

  /** Signed display, formatting only — the number itself is the server's. */
  fmtSigned(amount: number, currency: string): string {
    const formatted = Math.abs(amount).toLocaleString(undefined, {
      style: 'currency',
      currency,
      minimumFractionDigits: 2,
    });
    return amount < 0 ? `−${formatted}` : `+${formatted}`;
  }

  isDebt(amount: number): boolean {
    return amount < 0;
  }

  // ── Closing an account ────────────────────────────────────────────────────────────────────────
  //
  // ★★ THE CEREMONY IS THE ONE `Mark as paid` USES, AND FOR A HEAVIER REASON. Marking a payout paid
  // declares that money moved. This can declare that money will NEVER move: a write-off destroys a
  // claim a person who has already left still had, the credits reach a terminal state, and the ledger
  // it travels with is append-only — so there is no undo, only a new decision by somebody with
  // authority. The modal therefore shows exactly what is about to end, in figures, before it asks.

  private readonly api = inject(LedgerApiService);
  private readonly sidebarBadges = inject(SidebarBadgesStore);

  readonly closing = signal(false);
  readonly closeTarget = signal<TerminatedPayeeBalance | null>(null);

  /**
   * The 409 case, kept as its own state rather than folded into a generic error.
   *
   * ★ AND IT IS NEVER RETRIED SILENTLY. A conflict means the user was looking at a different account
   * than the one that exists now — a credit arrived, an amount moved, something was paid. Repeating
   * the same body would close a set nobody ever saw, which is the one outcome this whole mechanism
   * exists to prevent. The screen says what happened, reloads, and makes them look again.
   */
  readonly closeConflict = signal<string | null>(null);
  readonly closeError = signal<string | null>(null);

  readonly resolutionControl = new FormControl<AccountClosureResolution>('SettledExternally', {
    nonNullable: true,
  });

  readonly noteControl = new FormControl<string>('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(1000)],
  });

  readonly resolutionOptions: SelectOption[] = [
    { value: 'SettledExternally', label: 'LEDGER.CLOSE_RESOLUTION_SETTLED' },
    { value: 'WrittenOff', label: 'LEDGER.CLOSE_RESOLUTION_WRITTEN_OFF' },
  ];

  /**
   * A write-off is the destructive half; the modal says so louder for that one.
   *
   * ★ IT READS A SIGNAL, NOT `resolutionControl.value`. A `computed` over a FormControl's plain value
   * never re-evaluates — the control is not reactive state — so the severe warning would have stayed
   * hidden no matter what the user picked. Which is to say: the ceremony this whole modal exists for
   * would silently not have happened. `toSignal` over valueChanges is what makes it real.
   */
  private readonly resolution = toSignal(this.resolutionControl.valueChanges, {
    initialValue: this.resolutionControl.value,
  });

  readonly isWriteOff = computed(() => this.resolution() === 'WrittenOff');

  openClose(row: TerminatedPayeeBalance): void {
    this.closeTarget.set(row);
    this.closeConflict.set(null);
    this.closeError.set(null);
    this.resolutionControl.setValue('SettledExternally');
    this.noteControl.reset('');
  }

  cancelClose(): void {
    this.closeTarget.set(null);
    this.closeConflict.set(null);
    this.closeError.set(null);
  }

  async confirmClose(): Promise<void> {
    const row = this.closeTarget();
    if (!row || this.closing()) return;

    this.noteControl.markAsTouched();
    if (this.noteControl.invalid) return;

    this.closing.set(true);
    this.closeConflict.set(null);
    this.closeError.set(null);

    try {
      await firstValueFrom(
        this.api.closeAccount(row.payeeId, {
          currency: row.currency,
          resolution: this.resolutionControl.value,
          note: this.noteControl.value.trim(),
          // ★ THE IDS AND AMOUNTS EXACTLY AS SHOWN. The server closes this set or refuses.
          credits: row.unsettledCredits.map((c) => ({ creditId: c.creditId, amount: c.amount })),
          // Null when the row showed no balance row at all — not the same fact as zero, and the
          // server compares them as they were shown.
          expectedBalance: row.balanceUpdatedAt ? row.balance : null,
        }),
      );

      this.closeTarget.set(null);
      await this.load();

      // ★ Closing an account removes a row from this queue, which is the sidebar's other badge.
      void this.sidebarBadges.refresh();
    } catch (err) {
      const response = err as HttpErrorResponse;

      if (response?.status === 409) {
        // The reason is a CODE from the server; the words live in the translation files. An
        // unrecognised code degrades to a neutral sentence rather than printing an identifier.
        this.closeConflict.set(this.conflictKey(response.error?.reason));
        await this.load();
      } else {
        this.closeError.set(response?.error?.message ?? 'ERRORS.GENERIC');
      }
    } finally {
      this.closing.set(false);
    }
  }

  private static readonly KnownConflictReasons: readonly string[] = [
    'CreditAppeared',
    'CreditDisappeared',
    'CreditAmountChanged',
    'BalanceChanged',
  ];

  private conflictKey(reason: unknown): string {
    return typeof reason === 'string'
      && TerminatedAccountsComponent.KnownConflictReasons.includes(reason)
      ? `LEDGER.CLOSE_CONFLICT_${reason}`
      : 'LEDGER.CLOSE_CONFLICT_UNKNOWN';
  }
}
