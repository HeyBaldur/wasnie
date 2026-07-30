import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LedgerApiService } from '../services/ledger.api.service';
import {
  CreateAdjustmentRequest,
  PayeeLedgerEntry,
  PayeeStatement,
} from '../models/ledger.model';

/**
 * Holds the payee's statements and ledger entries. Mirrors the Payees store shape.
 *
 * Deliberately has no computed money: every figure the screen shows arrives finished from the
 * server. The only `computed` here select and count — they never add, subtract or scale a number.
 */
@Injectable({ providedIn: 'root' })
export class LedgerStore {
  private readonly api = inject(LedgerApiService);

  readonly statements = signal<PayeeStatement[]>([]);
  readonly entries = signal<PayeeLedgerEntry[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  /** The currency currently shown in the header. Statements are per currency, never summed. */
  readonly selectedCurrency = signal<string | null>(null);

  readonly currencies = computed(() => this.statements().map((s) => s.currency));

  readonly activeStatement = computed<PayeeStatement | null>(() => {
    const all = this.statements();
    const selected = this.selectedCurrency();
    return all.find((s) => s.currency === selected) ?? all[0] ?? null;
  });

  readonly hasLedger = computed(() => this.statements().length > 0 || this.entries().length > 0);

  async load(payeeId: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const [statements, entries] = await Promise.all([
        firstValueFrom(this.api.getStatements(payeeId)),
        firstValueFrom(this.api.getEntries(payeeId)),
      ]);
      this.statements.set(statements);
      this.entries.set(entries);
      if (!this.selectedCurrency() && statements.length > 0) {
        this.selectedCurrency.set(statements[0].currency);
      }
    } catch {
      this.error.set('LEDGER.LOAD_ERROR');
    } finally {
      this.loading.set(false);
    }
  }

  selectCurrency(currency: string): void {
    this.selectedCurrency.set(currency);
  }

  /**
   * Creates the adjustment and RELOADS from the server rather than pushing the returned entry into
   * the list and adjusting a balance locally: the balance moved server-side, and re-reading is the
   * only way the screen and the ledger cannot disagree.
   */
  async createAdjustment(payeeId: string, request: CreateAdjustmentRequest): Promise<boolean> {
    this.saving.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.createAdjustment(payeeId, request));
      await this.load(payeeId);
      return true;
    } catch {
      this.error.set('LEDGER.ADJUSTMENT_ERROR');
      return false;
    } finally {
      this.saving.set(false);
    }
  }

  reset(): void {
    this.statements.set([]);
    this.entries.set([]);
    this.selectedCurrency.set(null);
    this.error.set(null);
  }
}
