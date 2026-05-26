import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  ParseResponse,
  PayeeImportColumnMapping,
  ValidateResponse,
  PayeeImportResult,
} from '../models/payee-import.models';

@Injectable({ providedIn: 'root' })
export class PayeeImportService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/imports/payees';

  parseFile(file: File): Observable<ParseResponse> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ParseResponse>(`${this.base}/parse`, form);
  }

  validateMapping(fileId: string, columnMapping: PayeeImportColumnMapping): Observable<ValidateResponse> {
    return this.http.post<ValidateResponse>(`${this.base}/validate`, { fileId, columnMapping });
  }

  executeImport(
    fileId: string,
    columnMapping: PayeeImportColumnMapping,
    skipRowsWithWarnings: boolean,
  ): Observable<PayeeImportResult> {
    return this.http.post<PayeeImportResult>(`${this.base}/execute`, {
      fileId,
      columnMapping,
      options: { skipRowsWithWarnings },
    });
  }
}
