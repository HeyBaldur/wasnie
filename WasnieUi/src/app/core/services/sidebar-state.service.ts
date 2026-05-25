import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'wasnie.sidebar.expanded';

@Injectable({ providedIn: 'root' })
export class SidebarStateService {
  private readonly _expanded = signal(
    localStorage.getItem(STORAGE_KEY) !== 'false'
  );

  readonly isExpanded = this._expanded.asReadonly();

  toggle(): void {
    const next = !this._expanded();
    this._expanded.set(next);
    localStorage.setItem(STORAGE_KEY, String(next));
  }
}
