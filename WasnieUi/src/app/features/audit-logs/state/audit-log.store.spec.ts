import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AuditLogStore } from './audit-log.store';
import { AuditLogApiService } from '../services/audit-log.api.service';
import { AuditLogDetail, AuditLogPage, AuditLogRow, SYSTEM_ACTOR } from '../models/audit-log.model';

/**
 * The store behind the audit trail page.
 *
 * What it is here to keep: the filter the user set is the filter that travels (including the one
 * that looks like "no filter"), the page resets when the page size changes, and a failure empties
 * the table instead of leaving the previous tenant's rows under a new heading.
 */
describe('AuditLogStore', () => {
  const row = (over: Partial<AuditLogRow> = {}): AuditLogRow => ({
    id: 1,
    timestampUtc: '2026-09-07T07:00:28Z',
    actorEmail: 'admin@wasnie.test',
    action: 'PLAN_ARCHIVED',
    resourceType: 'Plan',
    resourceId: 'plan-1',
    resourceDisplayName: 'EU Accelerator',
    hasDetail: true,
    ...over,
  });

  const page = (over: Partial<AuditLogPage> = {}): AuditLogPage => ({
    items: [row()],
    page: 1,
    pageSize: 25,
    totalCount: 1,
    ...over,
  });

  let api: jasmine.SpyObj<AuditLogApiService>;
  let store: AuditLogStore;

  beforeEach(() => {
    api = jasmine.createSpyObj<AuditLogApiService>('AuditLogApiService', ['list', 'options', 'detail']);
    api.list.and.returnValue(of(page()));
    api.options.and.returnValue(of({ actions: ['PLAN_ARCHIVED'], actors: ['admin@wasnie.test'] }));

    TestBed.configureTestingModule({
      providers: [AuditLogStore, { provide: AuditLogApiService, useValue: api }],
    });
    store = TestBed.inject(AuditLogStore);
  });

  it('loads a page and takes its total from the server, not from the rows', async () => {
    api.list.and.returnValue(of(page({ items: [row()], totalCount: 4137 })));

    await store.load();

    expect(store.rows().length).toBe(1);
    expect(store.total()).toBe(4137);
    expect(store.totalPages()).toBe(Math.ceil(4137 / 25));
  });

  /**
   * ★★ THE CASE THE SENTINEL EXISTS FOR. Rows written by a background job carry an EMPTY actor, so
   * the intuitive encoding of "show me those" is `''` — which the query-string builder drops, which
   * the server reads as "no filter", and which would therefore return EVERY row while the dropdown
   * claimed to be filtering. If somebody changes SYSTEM_ACTOR back to '', this goes red here rather
   * than silently on screen.
   */
  it('sends the system actor as a value, not as an absence', async () => {
    await store.load({ actor: SYSTEM_ACTOR });

    expect(SYSTEM_ACTOR).not.toBe('');
    expect(api.list.calls.mostRecent().args[0].actor).toBe(SYSTEM_ACTOR);
  });

  it('keeps "any actor" as null, distinct from the system', async () => {
    await store.load({ actor: null });
    expect(api.list.calls.mostRecent().args[0].actor).toBeNull();
  });

  it('counts the system-actor filter as an active filter', async () => {
    await store.load({ actor: SYSTEM_ACTOR });
    expect(store.activeFilterCount()).toBe(1);
    expect(store.hasActiveFilters()).toBe(true);
  });

  /**
   * ★ PAGE 5 OF 47 ROWS STOPS EXISTING AT 100-PER-PAGE, and the server answers a page past the end
   * with nothing — a blank table that looks like "no results" for a filter that has plenty. Every
   * other list in this app resets, and this one agrees with them.
   */
  it('resets to page 1 when the page size changes', async () => {
    await store.goToPage(5);
    expect(api.list.calls.mostRecent().args[0].page).toBe(5);

    await store.setPageSize(100);

    const sent = api.list.calls.mostRecent().args[0];
    expect(sent.pageSize).toBe(100);
    expect(sent.page).toBe(1);
  });

  it('keeps the filter when only the page turns', async () => {
    await store.load({ action: 'PLAN_ARCHIVED', from: '2026-01-01' });
    await store.goToPage(3);

    const sent = api.list.calls.mostRecent().args[0];
    expect(sent.action).toBe('PLAN_ARCHIVED');
    expect(sent.from).toBe('2026-01-01');
    expect(sent.page).toBe(3);
  });

  /**
   * ★ A FAILURE EMPTIES THE TABLE. Leaving the previous rows in place under an error would show one
   * query's answer beside another query's heading — on a screen whose entire purpose is telling
   * somebody what happened, that is worse than showing nothing.
   */
  it('clears the rows and the total when the load fails', async () => {
    await store.load();
    expect(store.rows().length).toBe(1);

    api.list.and.returnValue(throwError(() => new Error('boom')));
    await store.load();

    expect(store.error()).toBe('AUDIT.LOAD_ERROR');
    expect(store.rows()).toEqual([]);
    expect(store.total()).toBe(0);
    expect(store.loading()).toBe(false);
  });

  it('clearing the filters resets every field and reloads', async () => {
    await store.load({ action: 'PLAN_ARCHIVED', actor: SYSTEM_ACTOR, from: '2026-01-01', to: '2026-02-01' });
    expect(store.activeFilterCount()).toBe(4);

    await store.clearFilters();

    const sent = api.list.calls.mostRecent().args[0];
    expect(sent.action).toBeNull();
    expect(sent.actor).toBeNull();
    expect(sent.from).toBeNull();
    expect(sent.to).toBeNull();
    expect(store.activeFilterCount()).toBe(0);
  });

  it('survives an options request that fails, so the page still opens', async () => {
    api.options.and.returnValue(throwError(() => new Error('boom')));

    await store.loadOptions();

    expect(store.actions()).toEqual([]);
    expect(store.actors()).toEqual([]);
  });

  describe('the evidence drawer', () => {
    const detail: AuditLogDetail = {
      id: 1,
      timestampUtc: '2026-09-07T07:00:28Z',
      actorEmail: 'admin@wasnie.test',
      actorUserId: 'u-1',
      action: 'PLAN_ARCHIVED',
      resourceType: 'Plan',
      resourceId: 'plan-1',
      resourceDisplayName: 'EU Accelerator',
      beforeJson: '{"status":"Active"}',
      afterJson: '{"status":"Archived"}',
      metadata: null,
      correlationId: 'c-1',
      ipAddress: '10.0.0.1',
      userAgent: 'Mozilla/5.0',
    };

    it('loads one row\'s evidence', async () => {
      api.detail.and.returnValue(of(detail));

      await store.openDetail(1);

      expect(api.detail).toHaveBeenCalledWith(1);
      expect(store.detail()).toEqual(detail);
    });

    /**
     * ★★ ONE ROW'S EVIDENCE MUST NEVER APPEAR UNDER ANOTHER ROW'S HEADING. Leaving the previous
     * detail in place while the next request is in flight is not a flicker on this screen — it is a
     * false attribution, which is the one thing an audit trail may not produce.
     */
    it('clears the previous evidence before fetching the next', async () => {
      api.detail.and.returnValue(of(detail));
      await store.openDetail(1);
      expect(store.detail()).not.toBeNull();

      let seenDuringFlight: AuditLogDetail | null = detail;
      api.detail.and.callFake(() => {
        seenDuringFlight = store.detail();
        return of({ ...detail, id: 2 });
      });

      await store.openDetail(2);

      expect(seenDuringFlight).withContext('stale detail was still visible').toBeNull();
      expect(store.detail()!.id).toBe(2);
    });

    it('reports a failure instead of showing an empty drawer', async () => {
      api.detail.and.returnValue(throwError(() => new Error('boom')));

      await store.openDetail(9);

      expect(store.detail()).toBeNull();
      expect(store.detailError()).toBe('AUDIT.DETAIL_ERROR');
      expect(store.detailLoading()).toBe(false);
    });

    it('closing clears both the row and any error', async () => {
      api.detail.and.returnValue(throwError(() => new Error('boom')));
      await store.openDetail(9);

      store.closeDetail();

      expect(store.detail()).toBeNull();
      expect(store.detailError()).toBeNull();
    });
  });
});
