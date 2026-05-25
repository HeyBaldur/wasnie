import { effect, inject, Injectable, signal, computed } from '@angular/core';
import { DOCUMENT } from '@angular/common';

export type ThemeMode = 'light' | 'soft' | 'dark' | 'system';
export type ResolvedTheme = 'light' | 'soft' | 'dark';

const STORAGE_KEY = 'wasnie.theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  private readonly _osDark = signal(false);

  readonly mode = signal<ThemeMode>('system');

  readonly resolvedTheme = computed<ResolvedTheme>(() => {
    const m = this.mode();
    if (m === 'system') {
      return this._osDark() ? 'dark' : 'light';
    }
    return m;
  });

  constructor() {
    effect(() => {
      this.document.documentElement.setAttribute('data-theme', this.resolvedTheme());
    });
  }

  init(): void {
    const stored = localStorage.getItem(STORAGE_KEY) as ThemeMode | null;
    if (stored === 'light' || stored === 'soft' || stored === 'dark' || stored === 'system') {
      this.mode.set(stored);
    }

    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    this._osDark.set(mq.matches);
    mq.addEventListener('change', (e) => this._osDark.set(e.matches));
  }

  setMode(mode: ThemeMode): void {
    this.mode.set(mode);
    try {
      localStorage.setItem(STORAGE_KEY, mode);
    } catch (_) {}
  }
}
