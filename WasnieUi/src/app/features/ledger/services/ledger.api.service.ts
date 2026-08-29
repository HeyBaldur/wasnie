import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CreateAdjustmentRequest,
  PayeeLedgerEntry,
  PayeeStatement,
  TerminatedAccounts,
  CloseAccountRequest,
  CloseAccountResult,
} from '../models/ledger.model';

@Injectable({ providedIn: 'root' })
export class LedgerApiService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/payees';

  /** One statement per currency — the balance is per (payee, currency). */
  getStatements(payeeId: string): Observable<PayeeStatement[]> {
    return this.http.get<PayeeStatement[]>(`${this.base}/${payeeId}/ledger/statement`);
  }

  getEntries(payeeId: string): Observable<PayeeLedgerEntry[]> {
    return this.http.get<PayeeLedgerEntry[]>(`${this.base}/${payeeId}/ledger/entries`);
  }

  /**
   * Payees who have left with an account still open — the work queue finance closes.
   *
   * Returns rows AND server-computed totals: a screen must not add money, and the totals are per
   * currency because there is no exchange rate anywhere in Wasnie to blend them with.
   */
  getTerminatedWithBalance(): Observable<TerminatedAccounts> {
    return this.http.get<TerminatedAccounts>(`${this.base}/ledger/terminated-with-balance`);
  }

  /**
   * Closes a departed payee's account. One-way: the credits reach a terminal state and the ledger is
   * append-only, so there is no undo — only a new, separate decision.
   *
   * A 409 means the account moved between the modal opening and this call. The caller must reload and
   * show what is there now, never retry the same body.
   */
  closeAccount(payeeId: string, request: CloseAccountRequest): Observable<CloseAccountResult> {
    return this.http.post<CloseAccountResult>(`${this.base}/${payeeId}/ledger/close-account`, request);
  }

  createAdjustment(
    payeeId: string,
    request: CreateAdjustmentRequest,
  ): Observable<PayeeLedgerEntry> {
    return this.http.post<PayeeLedgerEntry>(
      `${this.base}/${payeeId}/ledger/adjustments`,
      request,
    );
  }
}
