import { HttpErrorResponse } from '@angular/common/http';

export function extractApiError(err: unknown, fallback = 'ERRORS.GENERIC'): string {
  if (err instanceof HttpErrorResponse) {
    const msg = err.error?.message;
    if (typeof msg === 'string' && msg.trim()) return msg;
  }
  return fallback;
}
