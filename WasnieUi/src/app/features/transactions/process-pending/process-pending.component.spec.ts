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
    httpMock.verify();
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
});
