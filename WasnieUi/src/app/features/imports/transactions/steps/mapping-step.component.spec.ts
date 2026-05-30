import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ReactiveFormsModule, FormsModule } from '@angular/forms';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { TxMappingStepComponent } from './mapping-step.component';
import { ParseResponse } from '../models/transaction-import.models';

const SAMPLE_PARSE_RESULT: ParseResponse & { fileName: string; fileSize: number } = {
  fileId: 'file-abc',
  headers: ['Reference Number', 'Payee Code', 'Amount', 'Currency', 'Transaction Date', 'External ID'],
  rowCount: 5,
  sampleRows: [
    {
      'Reference Number': 'REF-001',
      'Payee Code': 'EMP001',
      'Amount': '1500.00',
      'Currency': 'USD',
      'Transaction Date': '2024-01-15',
      'External ID': 'EXT-001',
    },
  ],
  fileName: 'transactions.csv',
  fileSize: 1024,
};

describe('TxMappingStepComponent', () => {
  let component: TxMappingStepComponent;
  let fixture: ComponentFixture<TxMappingStepComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        TxMappingStepComponent,
        ReactiveFormsModule,
        FormsModule,
        TranslateModule.forRoot(),
        HttpClientTestingModule,
      ],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(TxMappingStepComponent);
    component = fixture.componentInstance;

    // Set required input
    fixture.componentRef.setInput('parseResult', SAMPLE_PARSE_RESULT);
    fixture.detectChanges();
  });

  it('auto-detects referenceNumberColumn from "Reference Number" header', () => {
    expect(component.form.value.referenceNumberColumn).toBe('Reference Number');
  });

  it('auto-detects amountColumn from "Amount" header', () => {
    expect(component.form.value.amountColumn).toBe('Amount');
  });

  it('canContinue is false when required fields are empty, true when all filled', () => {
    // Clear all fields
    component.form.patchValue({
      referenceNumberColumn: '',
      payeeCodeColumn: '',
      amountColumn: '',
      currencyColumn: '',
      transactionDateColumn: '',
    });
    expect(component.canContinue).toBeFalse();

    // Fill all required fields
    component.form.patchValue({
      referenceNumberColumn: 'Reference Number',
      payeeCodeColumn: 'Payee Code',
      amountColumn: 'Amount',
      currencyColumn: 'Currency',
      transactionDateColumn: 'Transaction Date',
    });
    expect(component.canContinue).toBeTrue();
  });
});
