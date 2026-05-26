import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { PayeeImportService } from './payee-import.service';
import { PayeeImportColumnMapping, ParseResponse, ValidateResponse, PayeeImportResult } from '../models/payee-import.models';

describe('PayeeImportService', () => {
  let service: PayeeImportService;
  let http: HttpTestingController;

  const BASE = '/api/imports/payees';

  const MAPPING: PayeeImportColumnMapping = {
    fullNameColumns: ['First Name', 'Last Name'],
    employeeCodeColumn: 'Emp Code',
    emailColumn: 'Email',
    hireDateColumn: 'Hire Date',
    roleColumn: null,
    managerEmployeeCodeColumn: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(PayeeImportService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  // ── parseFile ─────────────────────────────────────────────────────────────────

  describe('parseFile', () => {
    it('sends a POST to /api/imports/payees/parse', () => {
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
        headers: ['First Name', 'Last Name', 'Email'],
        rowCount: 42,
        sampleRows: [{ 'First Name': 'Alice', 'Last Name': 'Smith', Email: 'a@b.com' }],
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
    it('sends a POST to /api/imports/payees/validate', () => {
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

    it('emits the server response as ValidateResponse', done => {
      const stub: ValidateResponse = {
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
    it('sends a POST to /api/imports/payees/execute', () => {
      service.executeImport('file-1', MAPPING, false).subscribe();
      const req = http.expectOne(`${BASE}/execute`);
      expect(req.request.method).toBe('POST');
      req.flush({ totalRows: 10, createdCount: 10, skippedCount: 0, skippedRows: [] });
    });

    it('sends { fileId, columnMapping, options } in the request body', () => {
      service.executeImport('file-1', MAPPING, false).subscribe();
      const req = http.expectOne(`${BASE}/execute`);
      expect(req.request.body).toEqual({
        fileId: 'file-1',
        columnMapping: MAPPING,
        options: { skipRowsWithWarnings: false },
      });
      req.flush({ totalRows: 10, createdCount: 10, skippedCount: 0, skippedRows: [] });
    });

    it('sets skipRowsWithWarnings: true when requested', () => {
      service.executeImport('file-1', MAPPING, true).subscribe();
      const req = http.expectOne(`${BASE}/execute`);
      expect(req.request.body.options.skipRowsWithWarnings).toBeTrue();
      req.flush({ totalRows: 10, createdCount: 10, skippedCount: 0, skippedRows: [] });
    });

    it('sets skipRowsWithWarnings: false when not skipping', () => {
      service.executeImport('file-1', MAPPING, false).subscribe();
      const req = http.expectOne(`${BASE}/execute`);
      expect(req.request.body.options.skipRowsWithWarnings).toBeFalse();
      req.flush({ totalRows: 10, createdCount: 10, skippedCount: 0, skippedRows: [] });
    });

    it('emits the server response as PayeeImportResult', done => {
      const stub: PayeeImportResult = {
        totalRows: 10,
        createdCount: 8,
        skippedCount: 2,
        skippedRows: [],
      };
      service.executeImport('file-1', MAPPING, false).subscribe(res => {
        expect(res).toEqual(stub);
        done();
      });
      http.expectOne(`${BASE}/execute`).flush(stub);
    });
  });
});
