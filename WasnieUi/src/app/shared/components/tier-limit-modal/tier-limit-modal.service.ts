import { Injectable, computed, signal } from '@angular/core';

export interface TierLimitInfo {
  tier: string;
  currentCount: number;
  limit: number;
}

@Injectable({ providedIn: 'root' })
export class TierLimitModalService {
  private readonly _info = signal<TierLimitInfo | null>(null);

  readonly info = this._info.asReadonly();
  readonly isOpen = computed(() => this._info() !== null);

  show(info: TierLimitInfo): void {
    this._info.set(info);
  }

  close(): void {
    this._info.set(null);
  }
}
