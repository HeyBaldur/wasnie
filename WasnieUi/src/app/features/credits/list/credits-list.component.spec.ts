import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { CreditsListComponent } from './credits-list.component';
import { CreditsStore, EMPTY_CREDIT_FILTER } from '../state/credits.store';
import { CreditsApiService } from '../services/credits.api.service';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { PlansApiService } from '../../plans/services/plans.api.service';
import { of } from 'rxjs';

const credit = {
  id: 'cr-1', transactionId: 'tx-77', referenceNumber: 'HUBSPOT-9',
  payeeId: 'p1', payeeName: 'Anna', payeeCode: 'E1',
  planId: 'plan-55', planName: 'Test SKU Laptops', ruleId: 'rule-33', ruleName: 'Solo LAP',
  originalAmount: 1000, originalCurrency: 'EUR', creditedAmount: 100, creditedCurrency: 'EUR',
  allocatedAt: '2026-07-01T00:00:00Z', isSuperseded: false,
};

const makeStoreMock = () => ({
  filter: signal({ ...EMPTY_CREDIT_FILTER }),
  items: signal([credit]),
  counters: signal(null),
  byPayee: signal([]),
  byPayeeLoading: signal(false),
  loading: signal(false),
  totalCount: signal(1),
  totalPages: signal(1),
  page: signal(1),
  pageSize: signal(10),
  activeFilterCount: signal(0),
  hasActiveFilters: signal(false),
  loadFromQueryParams: jasmine.createSpy(),
  loadByPayee: jasmine.createSpy(),
  loadCounters: jasmine.createSpy(),
  setFilter: jasmine.createSpy(),
  setPage: jasmine.createSpy(),
  setPageSize: jasmine.createSpy(),
  clearFilters: jasmine.createSpy(),
  toQueryParams: jasmine.createSpy().and.returnValue({}),
  toExportParams: jasmine.createSpy().and.returnValue({}),
}) as unknown as CreditsStore;

describe('CreditsListComponent — navigation links', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CreditsListComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CreditsStore, useValue: makeStoreMock() },
        { provide: CreditsApiService, useValue: {} },
        { provide: PayeesApiService, useValue: {} },
        { provide: PlansApiService, useValue: {} },
        // queryParams, not just snapshot: the component subscribes to the URL now (bindFiltersToUrl).
        { provide: ActivatedRoute, useValue: { snapshot: { queryParams: {} }, queryParams: of({}) } },
      ],
    }).compileComponents();
  });

  it('links Reference → transaction, Plan → plan, Rule → rule', () => {
    const fixture = TestBed.createComponent(CreditsListComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('a[href*="/transactions/tx-77"]'))
      .withContext('Reference should link to the source transaction detail').not.toBeNull();
    expect(el.querySelector('a[href*="/plans/plan-55"]'))
      .withContext('Plan should link to the plan').not.toBeNull();
    expect(el.querySelector('a[href="/plans/plan-55/rules/rule-33"]'))
      .withContext('Rule should link to the rule inside the plan').not.toBeNull();
  });

  it('row click opens the credit detail', () => {
    const fixture = TestBed.createComponent(CreditsListComponent);
    const comp = fixture.componentInstance;
    const router = (comp as unknown as { router: { navigate: jasmine.Spy } }).router;
    const navSpy = spyOn(router, 'navigate');
    fixture.detectChanges();

    comp.openCredit('cr-1');
    expect(navSpy).toHaveBeenCalledWith(['/credits', 'cr-1']);
  });
});
