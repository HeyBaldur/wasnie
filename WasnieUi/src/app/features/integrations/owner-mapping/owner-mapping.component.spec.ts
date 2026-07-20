import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { CrmOwnerMappingComponent } from './owner-mapping.component';
import { HubSpotApiService } from '../services/hubspot.api.service';
import { UnresolvedCrmOwner } from '../models/crm-sync.model';

const owner: UnresolvedCrmOwner = {
  ownerId: 'O1', name: 'Alice A', email: 'alice@example.com',
  archived: false, closedWonDealCount: 3, unassignedTransactionCount: 2,
};

describe('CrmOwnerMappingComponent', () => {
  let apiMock: jasmine.SpyObj<HubSpotApiService>;

  function setup() {
    const fixture = TestBed.createComponent(CrmOwnerMappingComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  beforeEach(() => {
    apiMock = jasmine.createSpyObj<HubSpotApiService>('HubSpotApiService', [
      'getUnresolvedOwners', 'linkOwner',
    ]);
    apiMock.getUnresolvedOwners.and.returnValue(of({ count: 1, owners: [owner] }));
    apiMock.linkOwner.and.returnValue(of({ reassignedTransactions: 2 }));

    TestBed.configureTestingModule({
      imports: [CrmOwnerMappingComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: HubSpotApiService, useValue: apiMock },
      ],
    });
  });

  it('loads unresolved owners on init', () => {
    const comp = setup();
    expect(apiMock.getUnresolvedOwners).toHaveBeenCalled();
    expect(comp.owners().length).toBe(1);
    expect(comp.loading()).toBeFalse();
  });

  it('surfaces a load error', () => {
    apiMock.getUnresolvedOwners.and.returnValue(
      throwError(() => new HttpErrorResponse({ error: { message: 'boom' }, status: 400 })),
    );
    const comp = setup();
    expect(comp.loadError()).toBe('boom');
  });

  it('openLink resets selection and opens the modal with reassign defaulted', () => {
    const comp = setup();
    comp.openLink(owner);
    expect(comp.linkOpen()).toBeTrue();
    expect(comp.activeOwner()).toEqual(owner);
    expect(comp.selectedPayeeId()).toBe('');
    expect(comp.reassignChoice()).toBe('reassign');
  });

  it('confirmLink does nothing without a selected payee', () => {
    const comp = setup();
    comp.openLink(owner);
    comp.confirmLink();
    expect(apiMock.linkOwner).not.toHaveBeenCalled();
  });

  it('confirmLink posts the link with the reassign flag and reloads', () => {
    const comp = setup();
    comp.openLink(owner);
    comp.selectedPayeeId.set('p-1');
    comp.reassignChoice.set('reassign');
    comp.confirmLink();

    expect(apiMock.linkOwner).toHaveBeenCalledWith({
      ownerId: 'O1', payeeId: 'p-1', reassignExistingUnassigned: true,
    });
    expect(comp.linkOpen()).toBeFalse();
    // load() called again after a successful link (initial + reload).
    expect(apiMock.getUnresolvedOwners).toHaveBeenCalledTimes(2);
  });

  it('confirmLink with future-only sends reassign flag false', () => {
    const comp = setup();
    comp.openLink(owner);
    comp.selectedPayeeId.set('p-1');
    comp.reassignChoice.set('future');
    comp.confirmLink();

    expect(apiMock.linkOwner).toHaveBeenCalledWith({
      ownerId: 'O1', payeeId: 'p-1', reassignExistingUnassigned: false,
    });
  });
});
