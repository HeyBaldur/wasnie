import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { PayeeDetailComponent } from './payee-detail.component';
import { PayeesApiService } from '../services/payees.api.service';
import { PayeesStore } from '../state/payees.store';
import { ToastService } from '../../../shared/services/toast.service';

/**
 * Covers the assignment chip only. The chip used to be derived from the period alone, so a
 * DEACTIVATED assignment whose dates covered today rendered green and "In Progress" — a deactivated
 * plan shown as current on the screen an admin uses to answer "which plans is this person on?".
 */
describe('PayeeDetailComponent — assignment chip', () => {
  let component: PayeeDetailComponent;

  // Dates chosen relative to "now" so the tests don't rot: the period always contains today.
  const start = new Date(Date.now() - 30 * 86_400_000).toISOString();
  const end = new Date(Date.now() + 30 * 86_400_000).toISOString();

  beforeEach(async () => {
    const payeesApiSpy = jasmine.createSpyObj('PayeesApiService', [
      'getPayee', 'getPayeeDashboard', 'getPayeeAssignments', 'getPayeeQuotas', 'getPayeeCredits',
    ]);
    const empty = of({ items: [], totalCount: 0, page: 1, pageSize: 10, totalPages: 0, hasNextPage: false, hasPreviousPage: false });
    payeesApiSpy.getPayeeAssignments.and.returnValue(empty);
    payeesApiSpy.getPayeeQuotas.and.returnValue(empty);
    payeesApiSpy.getPayeeCredits.and.returnValue(empty);
    payeesApiSpy.getPayeeDashboard.and.returnValue(of(null));
    payeesApiSpy.getPayee.and.returnValue(of(null));

    await TestBed.configureTestingModule({
      imports: [PayeeDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PayeesApiService, useValue: payeesApiSpy },
        { provide: PayeesStore, useValue: jasmine.createSpyObj('PayeesStore', ['loadPayee'], { payee: () => null }) },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: new Map([['payeeId', 'payee-1']]) }, paramMap: of(new Map()) },
        },
      ],
    }).compileComponents();

    component = TestBed.createComponent(PayeeDetailComponent).componentInstance;
  });

  // (e) Unchanged behaviour for a live assignment.
  it('shows In Progress for an Active assignment whose period contains today', () => {
    expect(component.temporalKey(start, end, true, 'Active')).toBe('DASHBOARD.CHIP_IN_PROGRESS');
    expect(component.temporalVariant(start, end, true, 'Active')).toBe('success');
  });

  // (d) The bug: same dates, deactivated → must NOT read as current.
  it('does NOT show In Progress for a Deactivated assignment covering today', () => {
    expect(component.temporalKey(start, end, true, 'Deactivated')).toBe('ASSIGNMENTS.STATUS_DEACTIVATED');
    expect(component.temporalVariant(start, end, true, 'Deactivated')).toBe('neutral');
  });

  // Status wins over the period in every direction, not just the "covers today" case.
  it('reports Deactivated even for a future period', () => {
    const future = new Date(Date.now() + 10 * 86_400_000).toISOString();
    const later = new Date(Date.now() + 40 * 86_400_000).toISOString();
    expect(component.temporalKey(future, later, true, 'Deactivated')).toBe('ASSIGNMENTS.STATUS_DEACTIVATED');
  });

  // Callers that pass no status (other cards) keep the original period-only behaviour.
  it('falls back to the temporal chip when no status is supplied', () => {
    expect(component.temporalKey(start, end)).toBe('DASHBOARD.CHIP_IN_PROGRESS');
  });
});
