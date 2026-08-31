import { Injectable, inject } from '@angular/core';
import { WsToastParams, WsToastService, WsToastType } from '../ui/ws-toast/ws-toast.service';

export type ToastType = WsToastType;
export type ToastParams = WsToastParams;

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly ws = inject(WsToastService);

  readonly toasts = this.ws.toasts;

  /**
   * @param message a translation KEY, not a sentence — the container translates it.
   * @param params values interpolated into that translation. Omit for a static key.
   */
  show(message: string, type: ToastType = 'success', params?: ToastParams): void {
    this.ws.show(message, type, params);
  }

  dismiss(id: string): void {
    this.ws.dismiss(id);
  }
}
