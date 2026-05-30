import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { TxProgressStepComponent } from './progress-step.component';
import { ImportJobStatus, TransactionImportResult } from '../models/transaction-import.models';

function makeStatus(overrides: Partial<ImportJobStatus> = {}): ImportJobStatus {
  return {
    id: 'job-123',
    state: 'Running',
    progressCurrent: 5,
    progressTotal: 10,
    errorMessage: null,
    enqueuedAtUtc: '2024-01-15T10:00:00Z',
    startedAtUtc: '2024-01-15T10:00:01Z',
    completedAtUtc: null,
    ...overrides,
  };
}

describe('TxProgressStepComponent', () => {
  let component: TxProgressStepComponent;
  let fixture: ComponentFixture<TxProgressStepComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        TxProgressStepComponent,
        TranslateModule.forRoot(),
        HttpClientTestingModule,
      ],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(TxProgressStepComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    fixture.componentRef.setInput('jobId', 'job-123');
  });

  afterEach(() => {
    httpMock.verify();
  });

  // Test 1: Starts polling immediately on init (first call at tick(0))
  it('starts polling immediately on init (first call at tick 0)', fakeAsync(() => {
    fixture.detectChanges(); // triggers ngOnInit

    // At tick(0) the timer fires immediately
    tick(0);
    const req = httpMock.expectOne('/api/jobs/job-123');
    expect(req.request.method).toBe('GET');
    req.flush(makeStatus());

    fixture.destroy();
  }));

  // Test 2: Emits `completed` when status is Succeeded
  it('emits completed when status is Succeeded', fakeAsync(() => {
    let emittedResult: TransactionImportResult | undefined;
    component.completed.subscribe(r => (emittedResult = r));

    fixture.detectChanges();
    tick(0);

    const req = httpMock.expectOne('/api/jobs/job-123');
    req.flush(makeStatus({ state: 'Succeeded', progressCurrent: 10, progressTotal: 10 }));

    expect(emittedResult).toEqual({ totalRows: 10, processedRows: 10 });

    fixture.destroy();
  }));

  // Test 3: Stops polling after Succeeded (no more HTTP calls after terminal state)
  it('stops polling after Succeeded — no further HTTP calls', fakeAsync(() => {
    fixture.detectChanges();
    tick(0);

    httpMock.expectOne('/api/jobs/job-123').flush(
      makeStatus({ state: 'Succeeded', progressCurrent: 10, progressTotal: 10 }),
    );

    // Advance time past one full poll cycle; no more requests should be made
    tick(3000);
    const pending = httpMock.match('/api/jobs/job-123');
    expect(pending.length).toBe(0);

    fixture.destroy();
  }));

  // Test 4: Stops polling when component destroyed (zombie-poll regression guard)
  it('stops polling when component is destroyed', fakeAsync(() => {
    fixture.detectChanges();
    tick(0);

    // Handle the first request
    httpMock.expectOne('/api/jobs/job-123').flush(makeStatus());

    // Destroy the component
    fixture.destroy();

    // Advance past the next poll interval — no requests should fire
    tick(3000);
    const pending = httpMock.match('/api/jobs/job-123');
    expect(pending.length).toBe(0);
  }));

  // Test 5: Handles transient network error without failing — netError goes true then false
  it('sets netError on HTTP error and clears it on next successful poll', fakeAsync(() => {
    fixture.detectChanges();
    tick(0);

    // First request fails with a network error
    httpMock.expectOne('/api/jobs/job-123').error(new ProgressEvent('error'));
    expect(component.netError()).toBeTrue();

    // Second poll (3s later) succeeds
    tick(3000);
    httpMock.expectOne('/api/jobs/job-123').flush(makeStatus());
    expect(component.netError()).toBeFalse();

    fixture.destroy();
  }));

  // Test 6: Emits retryRequested when retry button clicked on failure
  it('emits retryRequested when retry button is clicked on failure', fakeAsync(() => {
    let retryEmitted = false;
    component.retryRequested.subscribe(() => (retryEmitted = true));

    fixture.detectChanges();
    tick(0);

    // Simulate a failed job
    httpMock.expectOne('/api/jobs/job-123').flush(
      makeStatus({ state: 'Failed', errorMessage: 'Something went wrong' }),
    );
    fixture.detectChanges();

    // Find and click the retry button
    const retryBtn = fixture.nativeElement.querySelector('[data-testid="retry-btn"]');
    expect(retryBtn).toBeTruthy();
    retryBtn.click();

    expect(retryEmitted).toBeTrue();

    fixture.destroy();
  }));
});
