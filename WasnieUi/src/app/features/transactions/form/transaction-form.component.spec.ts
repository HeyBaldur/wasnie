import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { TransactionFormComponent } from './transaction-form.component';
import { TransactionsStore } from '../state/transactions.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { SettingsApiService } from '../../admin/services/settings.api.service';
import { Transaction, TransactionStatus, TransactionSource } from '../models/transaction.model';

describe('TransactionFormComponent', () => {
  let storeSpy: jasmine.SpyObj<TransactionsStore>;
  let payeesApiSpy: jasmine.SpyObj<PayeesApiService>;
  let toastSpy: jasmine.SpyObj<ToastService>;
  let settingsApiSpy: jasmine.SpyObj<SettingsApiService>;

  const mockTx: Transaction = {
    id: 'tx-1',
    tenantId: 'tenant-1',
    referenceNumber: 'REF-001',
    payeeId: 'payee-1',
    amount: 500,
    currency: 'USD',
    transactionDate: '2024-01-15',
    ingestedAt: '2024-01-15T10:00:00Z',
    source: TransactionSource.Manual,
    status: TransactionStatus.Pending,
    quantity: 1,
  };

  beforeEach(async () => {
    storeSpy = jasmine.createSpyObj('TransactionsStore', ['createTransaction']);
    storeSpy.createTransaction.and.returnValue(Promise.resolve(mockTx));

    payeesApiSpy = jasmine.createSpyObj('PayeesApiService', ['getPayees']);
    payeesApiSpy.getPayees.and.returnValue(
      of({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0, hasNextPage: false, hasPreviousPage: false })
    );

    toastSpy = jasmine.createSpyObj('ToastService', ['show']);

    // Default: no rows returned -> payee falls back to Optional, matching the backend's
    // `?.IsRequired ?? false`. Individual specs override this to test the Required path.
    settingsApiSpy = jasmine.createSpyObj('SettingsApiService', ['getFieldRequirements']);
    settingsApiSpy.getFieldRequirements.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [TransactionFormComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: TransactionsStore, useValue: storeSpy },
        { provide: PayeesApiService, useValue: payeesApiSpy },
        { provide: ToastService, useValue: toastSpy },
        { provide: SettingsApiService, useValue: settingsApiSpy },
      ],
    }).compileComponents();
  });

  describe('payee requiredness follows the tenant setting', () => {
    const fill = (component: TransactionFormComponent) =>
      component.form.patchValue({
        referenceNumber: 'REF-001',
        transactionDate: '2024-01-15',
        amount: 500,
        currency: 'USD',
      });

    it('is valid without a payee when the setting is Optional', () => {
      settingsApiSpy.getFieldRequirements.and.returnValue(
        of([{ entityName: 'Transaction', fieldName: 'PayeeId', isRequired: false }])
      );
      const fixture = TestBed.createComponent(TransactionFormComponent);
      fixture.detectChanges();
      const component = fixture.componentInstance;

      fill(component);

      expect(component.payeeRequired()).toBeFalse();
      expect(component.form.valid).toBeTrue();
    });

    it('is invalid without a payee when the setting is Required', () => {
      settingsApiSpy.getFieldRequirements.and.returnValue(
        of([{ entityName: 'Transaction', fieldName: 'PayeeId', isRequired: true }])
      );
      const fixture = TestBed.createComponent(TransactionFormComponent);
      fixture.detectChanges();
      const component = fixture.componentInstance;

      fill(component);

      expect(component.payeeRequired()).toBeTrue();
      expect(component.form.valid).toBeFalse();
      expect(component.form.get('payeeId')?.hasError('required')).toBeTrue();
    });

    it('falls back to Optional when the settings call fails', () => {
      settingsApiSpy.getFieldRequirements.and.returnValue(throwError(() => new Error('boom')));
      const fixture = TestBed.createComponent(TransactionFormComponent);
      fixture.detectChanges();
      const component = fixture.componentInstance;

      fill(component);

      expect(component.payeeRequired()).toBeFalse();
      expect(component.form.valid).toBeTrue();
    });

    it('sends payeeId null rather than an empty string when left blank', fakeAsync(async () => {
      const fixture = TestBed.createComponent(TransactionFormComponent);
      fixture.detectChanges();
      const component = fixture.componentInstance;

      fill(component);
      await component.onSubmit();
      tick();

      expect(storeSpy.createTransaction).toHaveBeenCalled();
      const arg = storeSpy.createTransaction.calls.mostRecent().args[0];
      expect(arg.payeeId).toBeNull();
    }));
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
      quantity: 1,
      processImmediately: true,
    });
  }));

  it('processImmediately defaults to true (checkbox checked)', () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    expect(component.form.get('processImmediately')?.value).toBeTrue();
  });

  it('onSubmit() sends processImmediately=false when checkbox is unchecked', fakeAsync(async () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.patchValue({
      payeeId: 'payee-1',
      referenceNumber: 'REF-002',
      transactionDate: '2024-01-15',
      amount: 100,
      currency: 'USD',
      processImmediately: false,
    });

    await component.onSubmit();
    tick();

    expect(storeSpy.createTransaction).toHaveBeenCalledWith(
      jasmine.objectContaining({ processImmediately: false })
    );
  }));

  it('hasError() returns true for touched field with the given error', () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    // referenceNumber, not payeeId: payee requiredness is now tenant-configurable and
    // defaults to Optional, so it is no longer an unconditionally-required control.
    const ctrl = component.form.get('referenceNumber')!;
    ctrl.markAsTouched();

    expect(component.hasError('referenceNumber', 'required')).toBeTrue();
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
