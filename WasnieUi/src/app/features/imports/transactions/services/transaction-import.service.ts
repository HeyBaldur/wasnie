import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  ParseResponse,
  TransactionImportColumnMapping,
  TransactionValidateResponse,
  ExecuteAccepted,
  ImportJobStatus,
} from '../models/transaction-import.models';

@Injectable({ providedIn: 'root' })
export class TransactionImportService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/imports/transactions';

  parseFile(file: File): Observable<ParseResponse> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ParseResponse>(`${this.base}/parse`, form);
  }

  validateMapping(
    fileId: string,
    columnMapping: TransactionImportColumnMapping,
  ): Observable<TransactionValidateResponse> {
    return this.http.post<TransactionValidateResponse>(`${this.base}/validate`, {
      fileId,
      columnMapping,
    });
  }

  executeImport(
    fileId: string,
    columnMapping: TransactionImportColumnMapping,
    skipRowsWithWarnings: boolean,
  ): Observable<ExecuteAccepted> {
    return this.http.post<ExecuteAccepted>(`${this.base}/execute`, {
      fileId,
      columnMapping,
      options: { skipRowsWithWarnings },
    });
  }

  getJobStatus(jobId: string): Observable<ImportJobStatus> {
    return this.http.get<ImportJobStatus>(`/api/jobs/${jobId}`);
  }

  getImportLimits(): Observable<{ maxRows: number }> {
    return this.http.get<{ maxRows: number }>(`${this.base}/limits`);
  }
}
