import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { QuotaCreateComponent } from './quota-create.component';
import { QuotasStore } from '../state/quotas.store';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { AssignmentsApiService } from '../../assignments/services/assignments.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { QuotaMeasurementType } from '../models/quota.model';

/**
 * One quota configuration, N payees. The screen's job is to collect the people and to be honest when
 * the server refuses the batch — NOTHING is created in that case, which is what makes re-sending the
 * corrected batch safe rather than a way to duplicate the rows that already went in.
 */
describe('QuotaCreateComponent — selecting several payees', () => {
  let fixture: ComponentFixture<QuotaCreateComponent>;
  let component: QuotaCreateComponent;
  let store: jasmine.SpyObj<QuotasStore>;
  let toast: jasmine.SpyObj<ToastService>;

  const PLAN_ID = 'plan-1';

  beforeEach(async () => {
    store = jasmine.createSpyObj<QuotasStore>('QuotasStore', ['bulkCreateQuotas', 'createQuota']);
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['show']);

    const payeesApi = jasmine.createSpyObj<PayeesApiService>('PayeesApiService', ['getPayees', 'getPayee']);
    payeesApi.getPayees.and.returnValue(of({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }) as never);

    const plansApi = jasmine.createSpyObj<PlansApiService>('PlansApiService', ['getPlans', 'getPlan']);
    plansApi.getPlan.and.returnValue(of({
      id: PLAN_ID, name: 'EU Accelerator', currency: 'EUR',
      effectiveStart: '2026-01-01', effectiveEnd: '2026-12-31',
    }) as never);

    const assignmentsApi = jasmine.createSpyObj<AssignmentsApiService>(
      'AssignmentsApiService', ['getAssignmentsByPlan', 'getAssignmentsByPayee']);
    assignmentsApi.getAssignmentsByPlan.and.returnValue(
      of({ items: [], page: 1, pageSize: 500, totalCount: 0, totalPages: 0 }) as never);

    await TestBed.configureTestingModule({
      imports: [QuotaCreateComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // A catch-all so the post-submit navigation resolves: without it every successful submit
        // logs NG04002 into a suite shared with 500+ other tests.
        provideRouter([{ path: '**', children: [] }]),
        { provide: QuotasStore, useValue: store },
        { provide: PayeesApiService, useValue: payeesApi },
        { provide: PlansApiService, useValue: plansApi },
        { provide: AssignmentsApiService, useValue: assignmentsApi },
        { provide: ToastService, useValue: toast },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(QuotaCreateComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  /** Fills everything except the payees, which each test chooses. */
  function fillForm(): void {
    component.form.patchValue({
      planId: PLAN_ID,
      measurementType: String(QuotaMeasurementType.Revenue),
      amount: 10000,
      dateRange: { start: '2026-04-01', end: '2026-06-30' },
      notes: '',
    });
    component.form.controls.currency.setValue('EUR');
  }

  function pick(id: string): void {
    // The search field is a picker: setting it is what the ws-select does on selection.
    component.form.controls.payeeId.setValue(id);
  }

  it('turns each selection into a chip and clears the search field', () => {
    pick('payee-1');
    pick('payee-2');

    expect(component.selectedPayees().map(p => p.id)).toEqual(['payee-1', 'payee-2']);
    expect(component.form.controls.payeeId.value).toBe('', 'the field is ready for the next name');
  });

  it('ignores a payee picked twice — that means "include them", not "twice"', () => {
    pick('payee-1');
    pick('payee-1');

    expect(component.selectedPayees()).toHaveSize(1);
  });

  it('removes a payee from the batch', () => {
    pick('payee-1');
    pick('payee-2');

    component.removePayee('payee-1');

    expect(component.selectedPayees().map(p => p.id)).toEqual(['payee-2']);
  });

  it('sends ONE request carrying every payee', async () => {
    store.bulkCreateQuotas.and.returnValue(Promise.resolve({ created: [], failures: [] }));
    fillForm();
    pick('payee-1');
    pick('payee-2');
    pick('payee-3');

    await component.onSubmit();

    expect(store.bulkCreateQuotas).toHaveBeenCalledTimes(1);
    const sent = store.bulkCreateQuotas.calls.mostRecent().args[0];
    expect(sent.payeeIds).toEqual(['payee-1', 'payee-2', 'payee-3']);
    // The configuration is stated once and applies to all of them.
    expect(sent.planId).toBe(PLAN_ID);
    expect(sent.amount).toBe(10000);
    expect(sent.periodStart).toBe('2026-04-01');
    expect(sent.periodEnd).toBe('2026-06-30');
  });

  it('refuses to submit with no payee selected', async () => {
    fillForm();

    await component.onSubmit();

    expect(store.bulkCreateQuotas).not.toHaveBeenCalled();
  });

  it('shows every reason when the server refuses the batch', async () => {
    // The server answers 400 with a reason per payee. The admin needs the list to know what to fix.
    store.bulkCreateQuotas.and.returnValue(Promise.reject({
      error: {
        created: [],
        failures: [
          { payeeId: 'p1', payeeName: 'Ana Sales', payeeEmployeeCode: 'E1', reason: 'The quota period must fall within the selected plan\'s effective period.' },
          { payeeId: 'p2', payeeName: 'Bob Sales', payeeEmployeeCode: 'E2', reason: 'Quota currency does not match the Plan\'s currency.' },
        ],
      },
    }));
    fillForm();
    pick('p1');
    pick('p2');

    await component.onSubmit();

    expect(component.batchFailures()).toHaveSize(2);
    expect(component.batchFailures()[0].payeeName).toBe('Ana Sales');
    expect(component.batchFailures()[0].reason).toContain('plan');
    expect(toast.show).toHaveBeenCalledWith('QUOTAS.BULK_REJECTED', 'error');
    // The chips survive: the admin corrects the configuration and re-sends the SAME batch, which is
    // safe precisely because nothing was created.
    expect(component.selectedPayees()).toHaveSize(2);
  });

  it('clears previous failures when the batch is sent again', async () => {
    store.bulkCreateQuotas.and.returnValue(Promise.reject({
      error: { created: [], failures: [{ payeeId: 'p1', payeeName: 'Ana', payeeEmployeeCode: 'E1', reason: 'nope' }] },
    }));
    fillForm();
    pick('p1');
    await component.onSubmit();
    expect(component.batchFailures()).toHaveSize(1);

    store.bulkCreateQuotas.and.returnValue(Promise.resolve({ created: [], failures: [] }));
    await component.onSubmit();

    expect(component.batchFailures()).toHaveSize(0);
  });

  it('falls back to the plain error toast when the failure is not a batch report', async () => {
    // A 401, a network drop — anything that is not the per-payee list must not be rendered as one.
    store.bulkCreateQuotas.and.returnValue(Promise.reject({ error: { message: 'Unauthorized' } }));
    fillForm();
    pick('p1');

    await component.onSubmit();

    expect(component.batchFailures()).toHaveSize(0);
    expect(toast.show).toHaveBeenCalled();
    expect(toast.show).not.toHaveBeenCalledWith('QUOTAS.BULK_REJECTED', 'error');
  });
});
