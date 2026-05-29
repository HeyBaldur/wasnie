import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TransactionImportService } from './transaction-import.service';
import {
  TransactionImportColumnMapping,
  ParseResponse,
  TransactionValidateResponse,
  ExecuteAccepted,
  ImportJobStatus,
} from '../models/transaction-import.models';

describe('TransactionImportService', () => {
  let service: TransactionImportService;
  let http: HttpTestingController;

  const BASE = '/api/imports/transactions';

  const MAPPING: TransactionImportColumnMapping = {
    referenceNumberColumn: 'Reference Number',
    payeeCodeColumn: 'Payee Code',
    amountColumn: 'Amount',
    currencyColumn: 'Currency',
    transactionDateColumn: 'Transaction Date',
    externalIdColumn: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(TransactionImportService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── parseFile ─────────────────────────────────────────────────────────────────

  describe('parseFile', () => {
    it('sends a POST to /api/imports/transactions/parse', () => {
      service.parseFile(new File([], 'test.csv')).subscribe();
      const req = http.expectOne(`${BASE}/parse`);
      expect(req.request.method).toBe('POST');
      req.flush({ fileId: '1', headers: [], rowCount: 0, sampleRows: [] });
    });

    it('sends the file as FormData with key "file"', () => {
      const file = new File(['col1,col2\na,b'], 'test.csv', { type: 'text/csv' });
      service.parseFile(file).subscribe();
      const req = http.expectOne(`${BASE}/parse`);
      expect(req.request.body).toBeInstanceOf(FormData);
      expect((req.request.body as FormData).get('file')).toBe(file);
      req.flush({ fileId: '1', headers: [], rowCount: 0, sampleRows: [] });
    });

    it('emits the server response as ParseResponse', done => {
      const stub: ParseResponse = {
        fileId: 'abc-123',
        headers: ['Reference Number', 'Payee Code', 'Amount', 'Currency', 'Transaction Date'],
        rowCount: 10,
        sampleRows: [{ 'Reference Number': 'REF-001', 'Amount': '1500.00' }],
      };
      service.parseFile(new File([], 'test.csv')).subscribe(res => {
        expect(res).toEqual(stub);
        done();
      });
      http.expectOne(`${BASE}/parse`).flush(stub);
    });
  });

  // ── validateMapping ───────────────────────────────────────────────────────────

  describe('validateMapping', () => {
    it('sends a POST to /api/imports/transactions/validate', () => {
      service.validateMapping('file-1', MAPPING).subscribe();
      const req = http.expectOne(`${BASE}/validate`);
      expect(req.request.method).toBe('POST');
      req.flush({ totalRows: 0, errorCount: 0, warningCount: 0, validRowCount: 0, rowResults: [] });
    });

    it('sends { fileId, columnMapping } in the request body', () => {
      service.validateMapping('file-1', MAPPING).subscribe();
      const req = http.expectOne(`${BASE}/validate`);
      expect(req.request.body).toEqual({ fileId: 'file-1', columnMapping: MAPPING });
      req.flush({ totalRows: 0, errorCount: 0, warningCount: 0, validRowCount: 0, rowResults: [] });
    });

    it('emits the server response as TransactionValidateResponse', done => {
      const stub: TransactionValidateResponse = {
        totalRows: 10,
        errorCount: 1,
        warningCount: 2,
        validRowCount: 7,
        rowResults: [],
      };
      service.validateMapping('file-1', MAPPING).subscribe(res => {
        expect(res).toEqual(stub);
        done();
      });
      http.expectOne(`${BASE}/validate`).flush(stub);
    });
  });

  // ── executeImport ─────────────────────────────────────────────────────────────

  describe('executeImport', () => {
    it('sends a POST to /api/imports/transactions/execute', () => {
      service.executeImport('file-1', MAPPING, false).subscribe();
      const req = http.expectOne(`${BASE}/execute`);
      expect(req.request.method).toBe('POST');
      req.flush({ jobId: 'job-abc-123' });
    });

    it('sends { fileId, columnMapping, options } in the request body', () => {
      service.executeImport('file-1', MAPPING, false).subscribe();
      const req = http.expectOne(`${BASE}/execute`);
      expect(req.request.body).toEqual({
        fileId: 'file-1',
        columnMapping: MAPPING,
        options: { skipRowsWithWarnings: false },
      });
      req.flush({ jobId: 'job-abc-123' });
    });

    it('sets skipRowsWithWarnings: true when requested', () => {
      service.executeImport('file-1', MAPPING, true).subscribe();
      const req = http.expectOne(`${BASE}/execute`);
      expect(req.request.body.options.skipRowsWithWarnings).toBeTrue();
      req.flush({ jobId: 'job-abc-123' });
    });

    it('sets skipRowsWithWarnings: false when not skipping', () => {
      service.executeImport('file-1', MAPPING, false).subscribe();
      const req = http.expectOne(`${BASE}/execute`);
      expect(req.request.body.options.skipRowsWithWarnings).toBeFalse();
      req.flush({ jobId: 'job-abc-123' });
    });

    it('emits ExecuteAccepted with jobId', done => {
      const stub: ExecuteAccepted = { jobId: 'job-abc-123' };
      service.executeImport('file-1', MAPPING, false).subscribe(res => {
        expect(res).toEqual(stub);
        done();
      });
      http.expectOne(`${BASE}/execute`).flush(stub);
    });
  });

  // ── getJobStatus ──────────────────────────────────────────────────────────────

  describe('getJobStatus', () => {
    it('sends a GET to /api/jobs/{jobId}', () => {
      service.getJobStatus('job-123').subscribe();
      const req = http.expectOne('/api/jobs/job-123');
      expect(req.request.method).toBe('GET');
      req.flush({
        id: 'job-123', state: 'Running',
        progressCurrent: 5, progressTotal: 10,
        errorMessage: null, enqueuedAtUtc: '', startedAtUtc: null, completedAtUtc: null,
      });
    });

    it('emits the server response as ImportJobStatus', done => {
      const stub: ImportJobStatus = {
        id: 'job-123',
        state: 'Succeeded',
        progressCurrent: 10,
        progressTotal: 10,
        errorMessage: null,
        enqueuedAtUtc: '2024-01-15T10:00:00Z',
        startedAtUtc: '2024-01-15T10:00:01Z',
        completedAtUtc: '2024-01-15T10:00:05Z',
      };
      service.getJobStatus('job-123').subscribe(res => {
        expect(res).toEqual(stub);
        done();
      });
      http.expectOne('/api/jobs/job-123').flush(stub);
    });
  });
});
