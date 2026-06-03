import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  TransactionUpdateColumnMapping,
  TransactionUpdateValidateResponse,
  TransactionUpdateExecuteAccepted,
} from '../models/transaction-update.models';
import { ParseResponse } from '../models/transaction-import.models';

@Injectable({ providedIn: 'root' })
export class TransactionUpdateService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/imports/transactions/update';

  parseFile(file: File): Observable<ParseResponse> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ParseResponse>(`${this.base}/parse`, form);
  }

  validateMapping(
    fileId: string,
    columnMapping: TransactionUpdateColumnMapping,
  ): Observable<TransactionUpdateValidateResponse> {
    return this.http.post<TransactionUpdateValidateResponse>(`${this.base}/validate`, {
      fileId,
      columnMapping,
    });
  }

  executeUpdate(
    fileId: string,
    columnMapping: TransactionUpdateColumnMapping,
  ): Observable<TransactionUpdateExecuteAccepted> {
    return this.http.post<TransactionUpdateExecuteAccepted>(`${this.base}/execute`, {
      fileId,
      columnMapping,
    });
  }
}
