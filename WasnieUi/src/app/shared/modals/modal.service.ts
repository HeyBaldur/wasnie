import { Injectable, signal } from '@angular/core';

export type ModalVariant = 'default' | 'danger';

export interface ConfirmConfig {
  title: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  variant: ModalVariant;
}

@Injectable({ providedIn: 'root' })
export class ModalService {
  readonly config = signal<ConfirmConfig | null>(null);

  private resolve: ((value: boolean) => void) | null = null;

  confirm(config: ConfirmConfig): Promise<boolean> {
    this.config.set(config);
    return new Promise<boolean>((res) => {
      this.resolve = res;
    });
  }

  respond(value: boolean): void {
    this.resolve?.(value);
    this.resolve = null;
    this.config.set(null);
  }
}
