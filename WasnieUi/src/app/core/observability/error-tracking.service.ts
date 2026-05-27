export abstract class ErrorTrackingService {
  abstract captureException(error: Error, context?: Record<string, unknown>): void;
  abstract captureMessage(message: string, level: 'info' | 'warning' | 'error', context?: Record<string, unknown>): void;
}
