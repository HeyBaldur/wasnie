/**
 * The review table's Amount column.
 *
 * ★ THE FIXTURE HAS THE SHAPE THE REAL ENDPOINT SENDS: `amount` a JSON number, `currency` the upper-case
 * code, both null together when the row could not be read. What is pinned is the rule, not the picture —
 * the server's reading is shown as money, and the file's own text only when there is no reading.
 */
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

import { TxPreviewStepComponent } from './preview-step.component';
import { TransactionImportService } from '../services/transaction-import.service';
import {
  TransactionImportColumnMapping,
  TransactionRowValidationResult,
  TransactionValidateResponse,
} from '../models/transaction-import.models';

const mapping: TransactionImportColumnMapping = {
  referenceNumberColumn: 'Reference',
  payeeCodeColumn: 'PayeeCode',
  amountColumn: 'Amount',
  currencyColumn: 'Currency',
  transactionDateColumn: 'Date',
};

function row(
  rowNumber: number,
  rawAmount: string,
  amount: number | null,
  currency: string | null,
): TransactionRowValidationResult {
  return {
    rowNumber,
    originalData: { Reference: `R-${rowNumber}`, PayeeCode: 'EMP402', Amount: rawAmount, Currency: 'EUR', Date: '2026-09-01' },
    issues: [],
    amount,
    currency,
    hasErrors: amount === null,
    hasWarnings: false,
  };
}

describe('TxPreviewStepComponent — Amount column', () => {
  let fixture: ComponentFixture<TxPreviewStepComponent>;

  function render(rows: TransactionRowValidationResult[]): string[] {
    const response: TransactionValidateResponse = {
      totalRows: rows.length,
      errorCount: rows.filter((r) => r.hasErrors).length,
      warningCount: 0,
      validRowCount: rows.filter((r) => !r.hasErrors).length,
      rowResults: rows,
    };

    fixture.componentRef.setInput('fileId', 'file-1');
    fixture.componentRef.setInput('columnMapping', mapping);
    fixture.componentRef.setInput('validateResponse', response);
    fixture.detectChanges();

    return Array.from(fixture.nativeElement.querySelectorAll('[data-testid="tx-preview-amount"]'))
      .map((cell) => (cell as HTMLElement).textContent!.trim());
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TxPreviewStepComponent, TranslateModule.forRoot()],
      providers: [provideRouter([]), { provide: TransactionImportService, useValue: {} }],
    }).compileComponents();

    fixture = TestBed.createComponent(TxPreviewStepComponent);
  });

  it('shows the server\'s amount as money, not the file\'s bare number', () => {
    expect(render([row(1, '2500.5', 2500.5, 'EUR')])).toEqual(['€2,500.50']);
  });

  it('★ formats what the SERVER read — "1,000.50" is 1000.50 to the import, NaN to a browser parse', () => {
    expect(render([row(1, '1,000.50', 1000.5, 'USD')])).toEqual(['$1,000.50']);
  });

  it('falls back to the file\'s text when the row could not be read, so it stays beside its error', () => {
    expect(render([row(1, 'abc', null, null)])).toEqual(['abc']);
  });
});
