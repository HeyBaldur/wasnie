import { Injectable, signal } from '@angular/core';

export type WsToastType = 'success' | 'error' | 'warning' | 'info';

/**
 * Values interpolated into the toast's translation, by name — the second argument of ngx-translate's
 * `translate` pipe.
 */
export type WsToastParams = Record<string, unknown>;

export interface WsToastItem {
  id: string;
  message: string;
  type: WsToastType;
  /**
   * ★ WHY A TOAST CARRIES PARAMETERS AT ALL. `message` is a translation KEY, and the container pipes
   * it through `translate`. A message with a number in it ("tier 2 ends at 10000") therefore had only
   * two ways to exist: as an English sentence built by whoever called `show` — untranslatable — or as
   * a key with nothing to fill its placeholders. Optional, and undefined for the great majority of
   * toasts, which are static keys.
   */
  params?: WsToastParams;
}

@Injectable({ providedIn: 'root' })
export class WsToastService {
  readonly toasts = signal<WsToastItem[]>([]);

  show(message: string, type: WsToastType = 'success', params?: WsToastParams): void {
    const id = crypto.randomUUID();
    this.toasts.update(t => [...t, { id, message, type, params }]);
    setTimeout(() => this.dismiss(id), 4000);
  }

  dismiss(id: string): void {
    this.toasts.update(t => t.filter(toast => toast.id !== id));
  }
}
