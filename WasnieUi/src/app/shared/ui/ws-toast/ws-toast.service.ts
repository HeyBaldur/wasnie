import { Injectable, signal } from '@angular/core';

export type WsToastType = 'success' | 'error' | 'warning' | 'info';

export interface WsToastItem {
  id: string;
  message: string;
  type: WsToastType;
}

@Injectable({ providedIn: 'root' })
export class WsToastService {
  readonly toasts = signal<WsToastItem[]>([]);

  show(message: string, type: WsToastType = 'success'): void {
    const id = crypto.randomUUID();
    this.toasts.update(t => [...t, { id, message, type }]);
    setTimeout(() => this.dismiss(id), 4000);
  }

  dismiss(id: string): void {
    this.toasts.update(t => t.filter(toast => toast.id !== id));
  }
}
