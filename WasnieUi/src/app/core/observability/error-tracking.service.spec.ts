import { TestBed } from '@angular/core/testing';
import { ConsoleErrorTrackingService } from './console-error-tracking.service';
import { ErrorTrackingService } from './error-tracking.service';

describe('ConsoleErrorTrackingService', () => {
  let service: ConsoleErrorTrackingService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ConsoleErrorTrackingService,
        { provide: ErrorTrackingService, useExisting: ConsoleErrorTrackingService },
      ],
    });
    service = TestBed.inject(ConsoleErrorTrackingService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('captureException should call console.error with the error', () => {
    const consoleSpy = spyOn(console, 'error');
    const error = new Error('test error');

    service.captureException(error);

    expect(consoleSpy).toHaveBeenCalledWith('[Error Tracking]', error, undefined);
  });

  it('captureException should include context when provided', () => {
    const consoleSpy = spyOn(console, 'error');
    const error = new Error('with context');
    const context = { requestId: 'abc' };

    service.captureException(error, context);

    expect(consoleSpy).toHaveBeenCalledWith('[Error Tracking]', error, context);
  });

  it('captureMessage with error level should call console.error', () => {
    const consoleSpy = spyOn(console, 'error');

    service.captureMessage('something went wrong', 'error');

    expect(consoleSpy).toHaveBeenCalledWith('[Error Tracking]', 'something went wrong', undefined);
  });

  it('captureMessage with warning level should call console.warn', () => {
    const consoleSpy = spyOn(console, 'warn');

    service.captureMessage('degraded mode', 'warning');

    expect(consoleSpy).toHaveBeenCalledWith('[Error Tracking]', 'degraded mode', undefined);
  });

  it('captureMessage with info level should call console.info', () => {
    const consoleSpy = spyOn(console, 'info');

    service.captureMessage('feature flag applied', 'info');

    expect(consoleSpy).toHaveBeenCalledWith('[Error Tracking]', 'feature flag applied', undefined);
  });
});
