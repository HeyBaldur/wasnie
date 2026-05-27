import { Injectable } from '@angular/core';
import { ErrorTrackingService } from './error-tracking.service';

@Injectable()
export class ConsoleErrorTrackingService extends ErrorTrackingService {
  captureException(error: Error, context?: Record<string, unknown>): void {
    console.error('[Error Tracking]', error, context);
  }

  captureMessage(message: string, level: 'info' | 'warning' | 'error', context?: Record<string, unknown>): void {
    if (level === 'error') {
      console.error('[Error Tracking]', message, context);
    } else if (level === 'warning') {
      console.warn('[Error Tracking]', message, context);
    } else {
      console.info('[Error Tracking]', message, context);
    }
  }
}
