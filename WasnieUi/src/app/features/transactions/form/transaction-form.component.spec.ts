import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { TransactionFormComponent } from './transaction-form.component';
import { TransactionsStore } from '../state/transactions.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { SettingsApiService } from '../../admin/services/settings.api.service';
import { TransactionsApiService } from '../services/transactions.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { Transaction, TransactionStatus, TransactionSource } from '../models/transaction.model';

describe('TransactionFormComponent', () => {
  let storeSpy: jasmine.SpyObj<TransactionsStore>;
  let payeesApiSpy: jasmine.SpyObj<PayeesApiService>;
  let toastSpy: jasmine.SpyObj<ToastService>;
  let settingsApiSpy: jasmine.SpyObj<SettingsApiService>;
  let transactionsApiSpy: jasmine.SpyObj<TransactionsApiService>;
  let plansApiSpy: jasmine.SpyObj<PlansApiService>;

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

    // Default: the payee is on a single plan, so no attribution choice is required. Specs that
    // exercise the multi-plan gate override this.
    transactionsApiSpy = jasmine.createSpyObj('TransactionsApiService', ['getPlanOptions']);
    transactionsApiSpy.getPlanOptions.and.returnValue(
      of({ options: [], selectionRequired: false })
    );

    // The category picker loads the tenant's known categories on init.
    plansApiSpy = jasmine.createSpyObj('PlansApiService', ['getCategoryValues']);
    plansApiSpy.getCategoryValues.and.returnValue(of(['Laptops', 'Servers']));

    await TestBed.configureTestingModule({
      imports: [TransactionFormComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: TransactionsStore, useValue: storeSpy },
        { provide: PayeesApiService, useValue: payeesApiSpy },
        { provide: ToastService, useValue: toastSpy },
        { provide: SettingsApiService, useValue: settingsApiSpy },
        { provide: TransactionsApiService, useValue: transactionsApiSpy },
        { provide: PlansApiService, useValue: plansApiSpy },
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
      // Left blank on the form → sent as null, not an empty string.
      description: null,
      productName: null,
      productSku: null,
      // Left blank → sent as null; the server still runs the SKU/name resolver.
      category: null,
      transactionDate: '2024-01-15',
      amount: 500,
      currency: 'USD',
      quantity: 1,
      // Single-plan payee → no attribution choice was required, so none is sent.
      selectedPlanAssignmentId: null,
      processImmediately: true,
    });
  }));

  it('onSubmit() sends an explicitly chosen category', fakeAsync(async () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.patchValue({
      payeeId: 'payee-1',
      referenceNumber: 'REF-001',
      transactionDate: '2024-01-15',
      amount: 500,
      currency: 'USD',
      category: 'Laptops',
    });

    await component.onSubmit();
    tick();

    expect(storeSpy.createTransaction).toHaveBeenCalledWith(
      jasmine.objectContaining({ category: 'Laptops' }),
    );
  }));

  it('can be saved without a category (stays optional)', fakeAsync(async () => {
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
    await component.onSubmit();
    tick();

    expect(storeSpy.createTransaction).toHaveBeenCalledWith(
      jasmine.objectContaining({ category: null }),
    );
  }));

  it('onSubmit() sends the trimmed description when one is typed', fakeAsync(async () => {
    const fixture = TestBed.createComponent(TransactionFormComponent);
    fixture.detectChanges();
    const component = fixture.componentInstance;

    component.form.patchValue({
      payeeId: 'payee-1',
      referenceNumber: 'REF-001',
      description: '  Acme Contract 2026  ',
      transactionDate: '2024-01-15',
      amount: 500,
      currency: 'USD',
    });

    await component.onSubmit();
    tick();

    expect(storeSpy.createTransaction).toHaveBeenCalledWith(
      jasmine.objectContaining({ description: 'Acme Contract 2026' }),
    );
  }));

  // The bug this guards: a payee on several plans had the plan chosen by an engine tie-break, which
  // silently decided the commission. The form must stop and make the admin state it.
  describe('plan attribution when the payee is on several plans', () => {
    const twoPlans = {
      options: [
        {
          planAssignmentId: 'asg-revenue', planId: 'plan-revenue', planName: 'Revenue Plan',
          planCurrency: 'USD', effectiveStart: '2024-01-01', effectiveEnd: '2024-12-31',
        },
        {
          planAssignmentId: 'asg-units', planId: 'plan-units', planName: 'Units Plan',
          planCurrency: 'USD', effectiveStart: '2024-01-01', effectiveEnd: '2024-12-31',
        },
      ],
      selectionRequired: true,
    };

    const fillValidExceptPlan = (component: TransactionFormComponent) =>
      component.form.patchValue({
        payeeId: 'payee-1',
        referenceNumber: 'REF-001',
        transactionDate: '2024-01-15',
        amount: 500,
        currency: 'USD',
      });

    it('blocks submission until a plan is chosen', fakeAsync(async () => {
      transactionsApiSpy.getPlanOptions.and.returnValue(of(twoPlans));
      const fixture = TestBed.createComponent(TransactionFormComponent);
      fixture.detectChanges();
      const component = fixture.componentInstance;

      fillValidExceptPlan(component);
      tick();

      expect(component.planSelectionRequired()).toBeTrue();
      expect(component.form.valid).toBeFalse();

      await component.onSubmit();
      tick();

      expect(storeSpy.createTransaction).not.toHaveBeenCalled();
    }));

    it('sends the chosen plan assignment once one is selected', fakeAsync(async () => {
      transactionsApiSpy.getPlanOptions.and.returnValue(of(twoPlans));
      const fixture = TestBed.createComponent(TransactionFormComponent);
      fixture.detectChanges();
      const component = fixture.componentInstance;

      fillValidExceptPlan(component);
      tick();
      component.form.patchValue({ selectedPlanAssignmentId: 'asg-units' });

      await component.onSubmit();
      tick();

      expect(storeSpy.createTransaction).toHaveBeenCalledWith(
        jasmine.objectContaining({ selectedPlanAssignmentId: 'asg-units' }),
      );
    }));

    it('offers exactly the plans the server returned', fakeAsync(() => {
      transactionsApiSpy.getPlanOptions.and.returnValue(of(twoPlans));
      const fixture = TestBed.createComponent(TransactionFormComponent);
      fixture.detectChanges();

      fillValidExceptPlan(fixture.componentInstance);
      tick();

      expect(fixture.componentInstance.planSelectOptions().map(o => o.value))
        .toEqual(['asg-revenue', 'asg-units']);
    }));

    // A stale choice must not survive a change that alters which plans apply.
    it('clears a chosen plan that is no longer a candidate after the date changes', fakeAsync(() => {
      transactionsApiSpy.getPlanOptions.and.returnValue(of(twoPlans));
      const fixture = TestBed.createComponent(TransactionFormComponent);
      fixture.detectChanges();
      const component = fixture.componentInstance;

      fillValidExceptPlan(component);
      tick();
      component.form.patchValue({ selectedPlanAssignmentId: 'asg-units' });

      transactionsApiSpy.getPlanOptions.and.returnValue(
        of({ options: [twoPlans.options[0]], selectionRequired: false })
      );
      component.form.patchValue({ transactionDate: '2025-03-01' });
      tick();

      expect(component.form.controls.selectedPlanAssignmentId.value).toBe('');
      expect(component.planSelectionRequired()).toBeFalse();
    }));

    // One plan (or none) means no ambiguity — the form must not add friction.
    it('does not require a plan when the payee has a single applicable plan', fakeAsync(() => {
      transactionsApiSpy.getPlanOptions.and.returnValue(
        of({ options: [twoPlans.options[0]], selectionRequired: false })
      );
      const fixture = TestBed.createComponent(TransactionFormComponent);
      fixture.detectChanges();
      const component = fixture.componentInstance;

      fillValidExceptPlan(component);
      tick();

      expect(component.planSelectionRequired()).toBeFalse();
      expect(component.form.valid).toBeTrue();
    }));
  });

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
