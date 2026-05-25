import { Injectable, inject } from '@angular/core';
import { WsToastService, WsToastType } from '../ui/ws-toast/ws-toast.service';

export type ToastType = WsToastType;

@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly ws = inject(WsToastService);

  readonly toasts = this.ws.toasts;

  show(message: string, type: ToastType = 'success'): void {
    this.ws.show(message, type);
  }

  dismiss(id: string): void {
    this.ws.dismiss(id);
  }
}
