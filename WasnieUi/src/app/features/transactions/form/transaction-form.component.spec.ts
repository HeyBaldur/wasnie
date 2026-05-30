import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { TransactionFormComponent } from './transaction-form.component';
import { TransactionsStore } from '../state/transactions.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { Transaction, TransactionStatus, TransactionSource } from '../models/transaction.model';

describe('TransactionFormComponent', () => {
  let storeSpy: jasmine.SpyObj<TransactionsStore>;
  let payeesApiSpy: jasmine.SpyObj<PayeesApiService>;
  let toastSpy: jasmine.SpyObj<ToastService>;

  const mockTx: Transaction = {
    id: 'tx-1',
    tenantId: 'tenant-1',
    referenceNumber: 'REF-001',
    payeeId: 'payee-1',
    amount: 500,
    currency: 'USD',
    transactionDate: '2024-01-15',
    source: TransactionSource.Manual,
    status: TransactionStatus.Pending,
  };

  beforeEach(async () => {
    storeSpy = jasmine.createSpyObj('TransactionsStore', ['createTransaction']);
    storeSpy.createTransaction.and.returnValue(Promise.resolve(mockTx));

    payeesApiSpy = jasmine.createSpyObj('PayeesApiService', ['getPayees']);
    payeesApiSpy.getPayees.and.returnValue(
      of({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0, hasNextPage: false, hasPreviousPage: false })
    );

    toastSpy = jasmine.createSpyObj('ToastService', ['show']);

    await TestBed.configureTestingModule({
      imports: [TransactionFormComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: TransactionsStore, useValue: storeSpy },
        { provide: PayeesApiService, useValue: payeesApiSpy },
        { provide: ToastService, useValue: toastSpy },
      ],
    }).compileComponents();
  });

  it('form is invalid when required fields are empty', () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    expect(component.form.invalid).toBeTrue();
  });

  it('form is valid when all required fields are filled', () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.patchValue({
      payeeId: 'payee-1',
      referenceNumber: 'REF-001',
      transactionDate: '2024-01-15',
      amount: 500,
      currency: 'USD',
    });

    expect(component.form.valid).toBeTrue();
  });

  it('amount below 0.01 is invalid', () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.patchValue({
      payeeId: 'payee-1',
      referenceNumber: 'REF-001',
      transactionDate: '2024-01-15',
      amount: 0,
      currency: 'USD',
    });

    expect(component.form.get('amount')?.hasError('min')).toBeTrue();
  });

  it('onSubmit() marks form as touched when invalid', async () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    await component.onSubmit();

    expect(component.form.touched).toBeTrue();
    expect(storeSpy.createTransaction).not.toHaveBeenCalled();
  });

  it('onSubmit() calls store.createTransaction when form is valid', fakeAsync(async () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.patchValue({
      payeeId: 'payee-1',
      referenceNumber: 'REF-001',
      transactionDate: '2024-01-15',
      amount: 500,
      currency: 'USD',
    });

    await component.onSubmit();
    tick();

    expect(storeSpy.createTransaction).toHaveBeenCalledWith({
      payeeId: 'payee-1',
      referenceNumber: 'REF-001',
      transactionDate: '2024-01-15',
      amount: 500,
      currency: 'USD',
    });
  }));

  it('hasError() returns true for touched field with the given error', () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    const ctrl = component.form.get('payeeId')!;
    ctrl.markAsTouched();

    expect(component.hasError('payeeId', 'required')).toBeTrue();
  });

  it('onCancel() emits cancelled event', () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    let cancelled = false;
    component.cancelled.subscribe(() => (cancelled = true));

    component.onCancel();

    expect(cancelled).toBeTrue();
  });
});
