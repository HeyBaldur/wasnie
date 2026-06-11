import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ComponentRef } from '@angular/core';
import { VoidTransactionModalComponent } from './void-transaction-modal.component';
import { TransactionsStore } from '../state/transactions.store';
import { TransactionStatus, TransactionSource } from '../models/transaction.model';

const makeTx = () => ({
  id: 'tx-1', tenantId: 't', referenceNumber: 'REF-001', payeeId: 'p',
  amount: 500, currency: 'EUR', quantity: 1, transactionDate: '2025-01-15',
  ingestedAt: '2025-01-01T00:00:00Z', source: TransactionSource.Manual,
  status: TransactionStatus.Pending, payeeName: 'Ana García',
});

const storeMock = {
  voidTransaction: jasmine.createSpy('voidTransaction').and.resolveTo({ id: 'tx-1' }),
};

describe('VoidTransactionModalComponent', () => {
  let componentRef: ComponentRef<VoidTransactionModalComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [VoidTransactionModalComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: TransactionsStore, useValue: storeMock },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(VoidTransactionModalComponent);
    componentRef = fixture.componentRef;
    componentRef.setInput('transaction', makeTx());
    componentRef.setInput('isOpen', true);
    fixture.detectChanges();
  });

  it('creates the component', () => {
    expect(componentRef.instance).toBeTruthy();
  });

  it('reasonError returns required key when reason is untouched and required', () => {
    const comp = componentRef.instance;
    comp.form.controls.reason.markAsTouched();
    comp.form.controls.reason.setValue('');
    expect(comp.reasonError).toBe('TRANSACTIONS.VOID_REASON_REQUIRED');
  });

  it('reasonError returns min-length key when reason is too short', () => {
    const comp = componentRef.instance;
    comp.form.controls.reason.markAsTouched();
    comp.form.controls.reason.setValue('ab');
    expect(comp.reasonError).toBe('TRANSACTIONS.VOID_REASON_MIN_LENGTH');
  });

  it('reasonError returns empty string when reason is valid', () => {
    const comp = componentRef.instance;
    comp.form.controls.reason.markAsTouched();
    comp.form.controls.reason.setValue('valid reason');
    expect(comp.reasonError).toBe('');
  });

  it('marks form as touched on submit when invalid', async () => {
    const comp = componentRef.instance;
    comp.form.controls.reason.setValue('');
    await comp.onSubmit();
    expect(comp.form.controls.reason.touched).toBeTrue();
  });

  it('onClose resets form and emits closed', () => {
    const comp = componentRef.instance;
    comp.form.controls.reason.setValue('some reason');
    const closedSpy = jasmine.createSpy('closed');
    comp.closed.subscribe(closedSpy);
    comp.onClose();
    expect(comp.form.controls.reason.value).toBeNull();
    expect(closedSpy).toHaveBeenCalled();
  });
});
