import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ProcessPendingComponent } from './process-pending.component';

describe('ProcessPendingComponent', () => {
  let component: ProcessPendingComponent;
  let fixture: ComponentFixture<ProcessPendingComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        ProcessPendingComponent,
        TranslateModule.forRoot(),
        HttpClientTestingModule,
      ],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(ProcessPendingComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    fixture.componentRef.setInput('scope', 'ByPlanAssignment');
    fixture.componentRef.setInput('scopeId', 'assignment-123');
  });

  afterEach(() => {
    // The component also fires GET /eligible-pending on init (eligible-list preview) — a side request no
    // test here asserts on. Flush any still-open ones; tests that destroy the fixture cancel it instead
    // (skip those, and let verify ignore cancelled). Keeps verify() meaningful for the asserted endpoints
    // (pending-count, process-pending, jobs).
    httpMock.match(r => r.url === '/api/transactions/eligible-pending')
      .forEach(req => { if (!req.cancelled) req.flush({ transactions: [], totalCount: 0 }); });
    httpMock.verify({ ignoreCancelled: true });
  });

  // Test 1: badge shows correct count from the count endpoint
  it('shows candidate count badge from API', fakeAsync(() => {
    fixture.detectChanges(); // triggers ngOnInit → _loadCount()

    const req = httpMock.expectOne(
      r => r.url === '/api/transactions/pending-count'
    );
    expect(req.request.method).toBe('GET');
    req.flush({ count: 42 });
    fixture.detectChanges();

    expect(component.candidateCount()).toBe(42);

    const html: string = fixture.nativeElement.innerHTML;
    expect(html).toContain('42');
  }));

  // Test 2: volume-aware message appears when count > 5000
  it('shows volume notice when candidate count exceeds 5000', fakeAsync(() => {
    fixture.detectChanges();

    const req = httpMock.expectOne(r => r.url === '/api/transactions/pending-count');
    req.flush({ count: 6000 });
    fixture.detectChanges();

    expect(component.showVolumeWarning).toBeTrue();
  }));

  // Test 3: volume notice does NOT appear when count <= 5000
  it('does not show volume notice when count is at or below 5000', fakeAsync(() => {
    fixture.detectChanges();

    const req = httpMock.expectOne(r => r.url === '/api/transactions/pending-count');
    req.flush({ count: 4999 });
    fixture.detectChanges();

    expect(component.showVolumeWarning).toBeFalse();
  }));

  // Test 4: clicking "Procesar Pending" dispatches job and starts polling
  it('dispatches job and starts polling on button click', fakeAsync(() => {
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === '/api/transactions/pending-count').flush({ count: 10 });
    fixture.detectChanges();

    component.onProcessPending();

    const dispatchReq = httpMock.expectOne('/api/transactions/process-pending');
    expect(dispatchReq.request.method).toBe('POST');
    dispatchReq.flush({ jobId: 'job-abc', candidateCount: 10 });

    expect(component.jobId()).toBe('job-abc');

    // Poll starts (tick 0)
    tick(0);
    const pollReq = httpMock.expectOne('/api/jobs/job-abc');
    pollReq.flush({
      id: 'job-abc', state: 'Running', progressCurrent: 5, progressTotal: 10,
      errorMessage: null, enqueuedAtUtc: '2026-01-01T00:00:00Z', startedAtUtc: null, completedAtUtc: null,
    });
    expect(component.isRunning).toBeTrue();

    fixture.destroy();
  }));

  // Test 5: cancel button calls cancel API endpoint
  it('calls cancel API when cancel button is clicked', fakeAsync(() => {
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === '/api/transactions/pending-count').flush({ count: 10 });

    component.onProcessPending();
    httpMock.expectOne('/api/transactions/process-pending').flush({ jobId: 'job-xyz', candidateCount: 10 });

    tick(0);
    httpMock.expectOne('/api/jobs/job-xyz').flush({
      id: 'job-xyz', state: 'Running', progressCurrent: 0, progressTotal: 10,
      errorMessage: null, enqueuedAtUtc: '', startedAtUtc: null, completedAtUtc: null,
    });

    component.onCancel();

    const cancelReq = httpMock.expectOne('/api/jobs/job-xyz/cancel');
    expect(cancelReq.request.method).toBe('POST');
    cancelReq.flush(null, { status: 204, statusText: 'No Content' });

    fixture.destroy();
  }));

  const summary = (over: Partial<{ processed: number; creditsCreated: number; skippedByValidation: number }>) =>
    JSON.stringify({
      processed: 0, creditsCreated: 0, skippedByOverlapRule: 0, skippedByIdempotency: 0,
      skippedByValidation: 0, skipReasonCounts: {}, skipDetails: [], ...over,
    });

  const jobStatus = (over: Record<string, unknown>) => ({
    id: 'job', state: 'Running', progressCurrent: 0, progressTotal: 0,
    errorMessage: null, enqueuedAtUtc: '', startedAtUtc: null, completedAtUtc: null, resultSummary: null,
    ...over,
  });

  function dispatch(jobId: string, count = 2): void {
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === '/api/transactions/pending-count').flush({ count });
    fixture.detectChanges();
    component.onProcessPending();
    httpMock.expectOne('/api/transactions/process-pending').flush({ jobId, candidateCount: count });
  }

  // Test 6 (WI-a): the UI must NOT declare done while the job is Running — the real result appears
  // only once the job reaches a terminal state.
  it('does not declare done while running; shows the real result only after Succeeded', fakeAsync(() => {
    dispatch('job-1');

    tick(0);
    httpMock.expectOne('/api/jobs/job-1').flush(jobStatus({ id: 'job-1', state: 'Running' }));
    fixture.detectChanges();

    expect(component.isDone).toBeFalse();
    expect((fixture.nativeElement.innerHTML as string)).not.toContain('PROCESS_PENDING.DONE');

    tick(1000);
    httpMock.expectOne('/api/jobs/job-1').flush(jobStatus({
      id: 'job-1', state: 'Succeeded', progressCurrent: 2, progressTotal: 2, completedAtUtc: '',
      resultSummary: summary({ processed: 2, creditsCreated: 3 }),
    }));
    fixture.detectChanges();

    expect(component.isDone).toBeTrue();
    expect(component.resultTone).toBe('success');
    expect((fixture.nativeElement.innerHTML as string)).toContain('PROCESS_PENDING.DONE');

    // On success the pending list is refreshed automatically (WI-c).
    httpMock.expectOne(r => r.url === '/api/transactions/pending-count').flush({ count: 0 });
  }));

  // Test 7 (WI-b): a failed job shows the error, never a success message.
  it('shows the error and not a success message when the job fails', fakeAsync(() => {
    dispatch('job-2');

    tick(0);
    httpMock.expectOne('/api/jobs/job-2').flush(jobStatus({
      id: 'job-2', state: 'Failed', progressTotal: 2, errorMessage: 'boom', completedAtUtc: '',
    }));
    fixture.detectChanges();

    expect(component.isDone).toBeTrue();
    const html = fixture.nativeElement.innerHTML as string;
    expect(html).toContain('boom');
    expect(html).toContain('PROCESS_PENDING.FAILED');
    expect(html).not.toContain('PROCESS_PENDING.DONE');
    // No auto-refresh on failure.
    httpMock.expectNone(r => r.url === '/api/transactions/pending-count');

    fixture.destroy();
  }));

  // Test 8 (WI-c): processed > 0 but 0 credits created must read as a notice, not a full success.
  it('reflects the no-credits outcome as a notice, not a full success', fakeAsync(() => {
    dispatch('job-3');

    tick(0);
    httpMock.expectOne('/api/jobs/job-3').flush(jobStatus({
      id: 'job-3', state: 'Succeeded', progressCurrent: 2, progressTotal: 2, completedAtUtc: '',
      resultSummary: summary({ processed: 2, creditsCreated: 0 }),
    }));
    fixture.detectChanges();

    expect(component.resultTone).toBe('notice');
    const html = fixture.nativeElement.innerHTML as string;
    expect(html).toContain('PROCESS_PENDING.NO_CREDITS');
    expect(html).not.toContain('PROCESS_PENDING.DONE');

    httpMock.expectOne(r => r.url === '/api/transactions/pending-count').flush({ count: 2 });
  }));

  // Test 9 (WI-d): a second click while the job is running must not dispatch again.
  it('guards against double dispatch while a job is running', fakeAsync(() => {
    dispatch('job-4');

    tick(0);
    httpMock.expectOne('/api/jobs/job-4').flush(jobStatus({
      id: 'job-4', state: 'Running', progressCurrent: 1, progressTotal: 2,
    }));

    expect(component.isRunning).toBeTrue();

    component.onProcessPending();
    httpMock.expectNone('/api/transactions/process-pending');

    fixture.destroy();
  }));

  // Test 10: after a run finishes with transactions still pending, the list reappears on its own
  // (no manual page refresh) alongside the reason — the count/eligible reload is not hidden by isDone.
  it('re-shows the still-pending transactions after completion without a manual refresh', fakeAsync(() => {
    dispatch('job-5');

    tick(0);
    httpMock.expectOne('/api/jobs/job-5').flush(jobStatus({
      id: 'job-5', state: 'Succeeded', progressCurrent: 2, progressTotal: 2, completedAtUtc: '',
      resultSummary: summary({ processed: 2, creditsCreated: 0 }),
    }));

    // The component auto-reloads count + eligible on success — feed the still-pending list back.
    httpMock.expectOne(r => r.url === '/api/transactions/pending-count').flush({ count: 2 });
    httpMock.match(r => r.url === '/api/transactions/eligible-pending')
      .forEach(req => { if (!req.cancelled) req.flush({
        transactions: [{
          id: 't1', payeeId: null, referenceNumber: 'REF-STILL-PENDING', payeeName: null,
          payeeCode: null, transactionDate: '2026-01-01', amount: 10, currency: 'EUR',
        }],
        totalCount: 1,
      }); });
    fixture.detectChanges();

    expect(component.isDone).toBeTrue();
    const html = fixture.nativeElement.innerHTML as string;
    expect(html).toContain('REF-STILL-PENDING');          // pending list came back automatically
    expect(html).toContain('PROCESS_PENDING.NO_CREDITS');  // and the reason is still shown
  }));
});
