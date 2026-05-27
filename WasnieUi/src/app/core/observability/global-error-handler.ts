import { ErrorHandler, inject, Injectable, Injector } from '@angular/core';
import { ErrorTrackingService } from './error-tracking.service';

@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  // Lazy injection via Injector avoids circular DI issues at bootstrap time.
  private readonly injector = inject(Injector);
  private _errorTracking?: ErrorTrackingService;

  private get errorTracking(): ErrorTrackingService {
    return (this._errorTracking ??= this.injector.get(ErrorTrackingService));
  }

  handleError(error: unknown): void {
    if (error instanceof Error) {
      this.errorTracking.captureException(error);
    } else {
      this.errorTracking.captureMessage(String(error), 'error');
    }
    console.error('Unhandled error:', error);
  }
}
