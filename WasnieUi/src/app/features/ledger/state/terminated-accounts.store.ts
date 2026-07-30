import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LedgerApiService } from '../services/ledger.api.service';
import { TerminatedPayeeBalance } from '../models/ledger.model';

/**
 * The queue of departed payees whose account is still open, shared by the screen that lists them and
 * the dashboard card that announces them.
 *
 * WHO decides what belongs in the queue: the SERVER. `GET /ledger/terminated-with-balance` already
 * returns exactly the payees who are terminated with a balance != 0, so nothing here re-derives that
 * rule — the counts below are `length` over the rows the backend chose, split by the sign it stored.
 * No money is added, scaled or compared to a threshold anywhere in this file.
 */
@Injectable({ providedIn: 'root' })
export class TerminatedAccountsStore {
  private readonly api = inject(LedgerApiService);

  readonly rows = signal<TerminatedPayeeBalance[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  /** Every open account, whichever way the money points. */
  readonly count = computed(() => this.rows().length);

  /** Positive balance: the company still OWES them — a liability nobody is processing any more. */
  readonly owedToPayeesCount = computed(() => this.rows().filter((r) => r.balance > 0).length);

  /** Negative balance: THEY owe the company — debt to recover or write off. */
  readonly owedByPayeesCount = computed(() => this.rows().filter((r) => r.balance < 0).length);

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.rows.set(await firstValueFrom(this.api.getTerminatedWithBalance()));
    } catch {
      this.error.set('ERRORS.GENERIC');
      this.rows.set([]);
    } finally {
      this.loading.set(false);
    }
  }
}
