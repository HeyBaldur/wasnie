import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { HubSpotApiService } from './hubspot.api.service';

describe('HubSpotApiService (Phase 2 deal sync)', () => {
  let service: HubSpotApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HubSpotApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('previewDeals() sends GET /api/integrations/hubspot/deals/preview', () => {
    service.previewDeals().subscribe((r) => expect(r.count).toBe(2));
    const req = httpMock.expectOne('/api/integrations/hubspot/deals/preview');
    expect(req.request.method).toBe('GET');
    req.flush({ count: 2, deals: [] });
  });

  it('importDeals() sends POST /api/integrations/hubspot/deals/import', () => {
    service.importDeals().subscribe((r) => expect(r.created).toBe(3));
    const req = httpMock.expectOne('/api/integrations/hubspot/deals/import');
    expect(req.request.method).toBe('POST');
    req.flush({
      dealsRead: 3, created: 3, assignedToPayee: 2, unassigned: 1,
      skippedAlreadyImported: 0, skippedInvalid: 0, newOwnerMappings: 1, warnings: [],
    });
  });

  it('getUnresolvedOwners() sends GET /api/integrations/hubspot/owners/unresolved', () => {
    service.getUnresolvedOwners().subscribe((r) => expect(r.count).toBe(0));
    const req = httpMock.expectOne('/api/integrations/hubspot/owners/unresolved');
    expect(req.request.method).toBe('GET');
    req.flush({ count: 0, owners: [] });
  });

  it('linkOwner() POSTs the owner id, payee id and reassign flag', () => {
    service
      .linkOwner({ ownerId: 'O1', payeeId: 'p-1', reassignExistingUnassigned: true })
      .subscribe((r) => expect(r.reassignedTransactions).toBe(2));

    const req = httpMock.expectOne('/api/integrations/hubspot/owners/link');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ ownerId: 'O1', payeeId: 'p-1', reassignExistingUnassigned: true });
    req.flush({ reassignedTransactions: 2 });
  });
});
