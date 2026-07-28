import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CreateAdjustmentRequest,
  PayeeLedgerEntry,
  PayeeStatement,
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
