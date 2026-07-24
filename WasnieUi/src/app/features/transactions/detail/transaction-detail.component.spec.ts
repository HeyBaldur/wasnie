import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { TransactionDetailComponent } from './transaction-detail.component';
import { TransactionsApiService } from '../services/transactions.api.service';
import { CreditsApiService } from '../../credits/services/credits.api.service';
import { TransactionStatus, TransactionSource } from '../models/transaction.model';

describe('TransactionDetailComponent', () => {
  const tx = {
    id: 'tx-1', tenantId: 't', referenceNumber: 'HUBSPOT-1-2',
    description: 'Close Sell', productName: 'Laptop X', productSku: 'LAP-12', category: 'Laptops',
    payeeId: 'p1', payeeName: 'Anna', payeeEmployeeCode: 'E1',
    amount: 1000, currency: 'EUR', quantity: 1,
    transactionDate: '2026-07-01', ingestedAt: '2026-07-01T00:00:00Z',
    externalId: '1-2', source: TransactionSource.Manual, status: TransactionStatus.Calculated,
  };

  const credit = {
    id: 'cr-1', transactionId: 'tx-1', referenceNumber: 'HUBSPOT-1-2',
    payeeId: 'p1', payeeName: 'Anna', payeeCode: 'E1',
    planId: 'plan-1', planName: 'Test SKU Laptops', ruleId: 'rule-1', ruleName: 'Solo LAP',
    originalAmount: 1000, originalCurrency: 'EUR', creditedAmount: 100, creditedCurrency: 'EUR',
    allocatedAt: '2026-07-01T00:00:00Z', isSuperseded: false,
  };
  // A credit for a DIFFERENT transaction that the reference substring-filter might return — must be dropped.
  const otherCredit = { ...credit, id: 'cr-2', transactionId: 'tx-OTHER' };

  function setup(getById = of(tx as never), creditsPage = of({ items: [credit, otherCredit], totalCount: 2, page: 1, pageSize: 100, totalPages: 1, hasNextPage: false, hasPreviousPage: false } as never)) {
    const txApi = { getById: jasmine.createSpy().and.returnValue(getById) };
    const creditsApi = { list: jasmine.createSpy().and.returnValue(creditsPage) };
    TestBed.configureTestingModule({
      imports: [TransactionDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: TransactionsApiService, useValue: txApi },
        { provide: CreditsApiService, useValue: creditsApi },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'tx-1' } } } },
      ],
    });
    return { txApi, creditsApi };
  }

  it('shows the transaction fields including the category', async () => {
    setup();
    const fixture = TestBed.createComponent(TransactionDetailComponent);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();

    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('HUBSPOT-1-2');   // reference
    expect(text).toContain('Close Sell');    // description
    expect(text).toContain('LAP-12');        // sku
    expect(text).toContain('Laptops');       // category
    expect(text).toContain('1-2');           // external id
  });

  it('lists only THIS transaction\'s generated credits, each linking to the credit detail', async () => {
    setup();
    const fixture = TestBed.createComponent(TransactionDetailComponent);
    await fixture.componentInstance.ngOnInit();
    fixture.detectChanges();

    // The substring-filtered page returned two credits; only the exact-transaction one survives.
    expect(fixture.componentInstance.credits().length).toBe(1);
    const creditLink: HTMLAnchorElement | null =
      fixture.nativeElement.querySelector('a[href*="/credits/cr-1"]');
    expect(creditLink).not.toBeNull();
    expect(fixture.nativeElement.querySelector('a[href*="/credits/cr-2"]')).toBeNull();
  });

  it('surfaces a load error without throwing', async () => {
    TestBed.configureTestingModule({
      imports: [TransactionDetailComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: TransactionsApiService, useValue: { getById: () => { throw new Error('boom'); } } },
        { provide: CreditsApiService, useValue: { list: () => of({ items: [] }) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'tx-1' } } } },
      ],
    });
    const fixture = TestBed.createComponent(TransactionDetailComponent);
    await fixture.componentInstance.ngOnInit();
    expect(fixture.componentInstance.error()).toBe('TRANSACTIONS.DETAIL.ERROR_LOAD');
  });
});
