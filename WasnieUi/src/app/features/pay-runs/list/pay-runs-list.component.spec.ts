import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { PayRunsListComponent } from './pay-runs-list.component';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunsStore } from '../state/pay-runs.store';
import { PlansApiService } from '../../plans/services/plans.api.service';

const makeStore = () => ({
  items: signal([]),
  total: signal(0),
  totalCount: signal(0),
  loading: signal(false),
  page: signal(1),
  pageSize: signal(25),
  totalPages: signal(1),
  filter: signal({}),
  setFilter: jasmine.createSpy('setFilter'),
  setPage: jasmine.createSpy('setPage'),
  reload: jasmine.createSpy('reload').and.returnValue(Promise.resolve()),
  toExportParams: jasmine.createSpy('toExportParams').and.returnValue({}),
});

describe('PayRunsListComponent — plan selector', () => {
  let component: PayRunsListComponent;

  const plansApiSpy = jasmine.createSpyObj<PlansApiService>('PlansApiService', ['getPlans']);

  beforeEach(async () => {
    plansApiSpy.getPlans.and.returnValue(of({
      items: [
        { id: 'plan-1', name: 'Q1 2026', effectiveStart: '2026-01-01', effectiveEnd: '2026-03-31', status: 'Active', currency: 'EUR' },
        { id: 'plan-2', name: 'Q2 2026', effectiveStart: '2026-04-01', effectiveEnd: '2026-06-30', status: 'Active', currency: 'EUR' },
      ],
      totalCount: 2, page: 1, pageSize: 20, totalPages: 1, hasNextPage: false, hasPreviousPage: false,
    } as any));

    await TestBed.configureTestingModule({
      imports: [PayRunsListComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: PayRunsApiService, useValue: jasmine.createSpyObj('PayRunsApiService', ['listPayRuns', 'calculate', 'exportPayRuns', 'deleteDraft']) },
        { provide: PayRunsStore, useValue: makeStore() },
        { provide: PlansApiService, useValue: plansApiSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PayRunsListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('planSearchFn maps plan items to SelectOptions with date range in label', fakeAsync(() => {
    let result: { value: string; label: string }[] = [];
    component.planSearchFn('Q1').subscribe(opts => (result = opts as typeof result));
    tick();

    expect(result.length).toBe(2);
    expect(result[0].value).toBe('plan-1');
    expect(result[0].label).toContain('Q1 2026');
    expect(result[0].label).toContain('2026-01-01');
    expect(result[0].label).toContain('2026-03-31');
  }));

  it('selecting a plan auto-fills periodStart and periodEnd', fakeAsync(() => {
    // Trigger searchFn to populate the internal date cache
    component.planSearchFn('').subscribe();
    tick();

    component.calculateForm.controls.planId.setValue('plan-1');
    tick();

    expect(component.calculateForm.value.periodStart).toBe('2026-01-01');
    expect(component.calculateForm.value.periodEnd).toBe('2026-03-31');
  }));

  it('selecting a different plan overwrites the previously auto-filled dates', fakeAsync(() => {
    component.planSearchFn('').subscribe();
    tick();

    component.calculateForm.controls.planId.setValue('plan-1');
    tick();
    component.calculateForm.controls.planId.setValue('plan-2');
    tick();

    expect(component.calculateForm.value.periodStart).toBe('2026-04-01');
    expect(component.calculateForm.value.periodEnd).toBe('2026-06-30');
  }));

  it('closeCalculateModal resets the form including planId', fakeAsync(() => {
    component.planSearchFn('').subscribe();
    tick();
    component.calculateForm.controls.planId.setValue('plan-1');
    tick();

    component.closeCalculateModal();

    expect(component.calculateForm.value.planId).toBeNull();
    expect(component.calculateForm.value.periodStart).toBeNull();
    expect(component.calculateForm.value.periodEnd).toBeNull();
  }));
});
