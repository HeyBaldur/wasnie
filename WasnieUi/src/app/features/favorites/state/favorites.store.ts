import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, WritableSignal, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ToastService } from '../../../shared/services/toast.service';
import { FavoriteEntityType, FavoriteItem } from '../models/favorite.model';
import { FavoritesApiService } from '../services/favorites.api.service';

type ByType<T> = Partial<Record<FavoriteEntityType, T>>;

/**
 * The user's favorites, for every entity type, in one place (KAN-64).
 *
 * ★★ ONE STATE, TWO TABLES. The star in the quick-access table and the star in the regular list below it read THIS
 * store and nothing else, so un-starring from either one empties the row above and outlines the star below in the
 * same change detection — there is no second copy to keep in step (ticket comment, AC "sin recargar la página").
 *
 * ★ `providedIn: 'root'` because the two tables never meet in the component tree, and because a future section
 * (transactions) reuses the same instance by passing another `FavoriteEntityType` — no per-section store.
 *
 * ★ OPTIMISTIC, AND HONEST ABOUT FAILURE. The star flips on press; the server's answer then either confirms it (and
 * the list is re-read, so the quick-access row carries the name and status the SERVER resolved) or reverts it with a
 * toast that says why. A star left filled after a refused request would claim a favorite that does not exist.
 */
@Injectable({ providedIn: 'root' })
export class FavoritesStore {
  private readonly api = inject(FavoritesApiService);
  private readonly toast = inject(ToastService);

  /** The resolved favorites the server returned, per type. Absent = not loaded yet. */
  private readonly _items = signal<ByType<FavoriteItem[]>>({});

  /**
   * Presses whose answer has not arrived: entity id → the state the user asked for. It overrides `_items` while the
   * request is in flight, which is what makes the star flip immediately.
   */
  private readonly _pending = signal<ByType<Record<string, boolean>>>({});

  private readonly _loading = signal<ByType<boolean>>({});

  /** The quick-access rows for a type, in the server's order (by name). Empty until loaded. */
  items(type: FavoriteEntityType): FavoriteItem[] {
    return this._items()[type] ?? [];
  }

  loaded(type: FavoriteEntityType): boolean {
    return this._items()[type] !== undefined;
  }

  loading(type: FavoriteEntityType): boolean {
    return this._loading()[type] ?? false;
  }

  /** Whether the entity is a favorite as far as the screen should say right now (pending press included). */
  isFavorite(type: FavoriteEntityType, entityId: string): boolean {
    const pending = this._pending()[type]?.[entityId];
    if (pending !== undefined) {
      return pending;
    }
    return this.items(type).some((i) => i.entityId === entityId);
  }

  /** A request for this star is in flight. */
  isBusy(type: FavoriteEntityType, entityId: string): boolean {
    return this._pending()[type]?.[entityId] !== undefined;
  }

  /**
   * The quick-access rows with pending REMOVALS already gone — so un-starring from the list below empties the row above
   * at once, not after the round trip. Pending ADDS are not invented here: the row needs the server's name and status,
   * and it appears when the list is re-read.
   */
  visibleItems(type: FavoriteEntityType): FavoriteItem[] {
    return this.items(type).filter((i) => this._pending()[type]?.[i.entityId] !== false);
  }

  async load(type: FavoriteEntityType): Promise<void> {
    this.patch(this._loading, type, true);
    try {
      const items = await firstValueFrom(this.api.list(type));
      this.patch(this._items, type, items);
    } catch {
      // A quick-access table that failed to load is simply absent; the list below still works, and a toast on every
      // page visit for a convenience would be noise. What was already known is KEPT: wiping it would outline stars the
      // server just confirmed. Only a type never loaded becomes an empty list, so the table can decide to hide.
      if (!this.loaded(type)) {
        this.patch(this._items, type, []);
      }
    } finally {
      this.patch(this._loading, type, false);
    }
  }

  async toggle(type: FavoriteEntityType, entityId: string): Promise<void> {
    if (this.isBusy(type, entityId)) {
      return;
    }

    const adding = !this.isFavorite(type, entityId);
    this.setPending(type, entityId, adding);

    try {
      if (adding) {
        await firstValueFrom(this.api.add(type, entityId));
      } else {
        await firstValueFrom(this.api.remove(type, entityId));
      }
    } catch (err) {
      this.setPending(type, entityId, undefined);
      const { key, params } = FavoritesStore.errorToast(err);
      this.toast.show(key, 'error', params);
      return;
    }

    if (adding) {
      // Re-read so the quick-access row carries what the SERVER resolved (name, code, status) — never a copy guessed
      // from the list row, which could differ from what the favorites endpoint is allowed to show.
      await this.load(type);
    } else {
      this.patch(this._items, type, this.items(type).filter((i) => i.entityId !== entityId));
    }

    this.setPending(type, entityId, undefined);
    this.toast.show(adding ? 'FAVORITES.TOAST_ADDED' : 'FAVORITES.TOAST_REMOVED', 'success');
  }

  /**
   * The server's refusal as a toast.
   *
   * ★ AN EXPLICIT WHITELIST (§C2). The server sends `messageKey`; it is matched against the codes this feature knows and
   * anything else becomes the generic sentence — a key is never concatenated or passed through unchecked.
   */
  static errorToast(err: unknown): { key: string; params?: Record<string, unknown> } {
    const body = err instanceof HttpErrorResponse ? err.error : null;
    const messageKey = body && typeof body === 'object' ? (body as { messageKey?: unknown }).messageKey : undefined;

    switch (messageKey) {
      case 'FAVORITES.LIMIT_REACHED': {
        const max = (body as { parameters?: { max?: unknown } }).parameters?.max;
        return typeof max === 'number'
          ? { key: 'FAVORITES.ERROR_LIMIT_REACHED', params: { max } }
          : { key: 'FAVORITES.ERROR_GENERIC' };
      }
      case 'FAVORITES.NOT_FOUND':
        return { key: 'FAVORITES.ERROR_NOT_AVAILABLE' };
      default:
        return { key: 'FAVORITES.ERROR_GENERIC' };
    }
  }

  private setPending(type: FavoriteEntityType, entityId: string, value: boolean | undefined): void {
    this._pending.update((all) => {
      const current = { ...(all[type] ?? {}) };
      if (value === undefined) {
        delete current[entityId];
      } else {
        current[entityId] = value;
      }
      return { ...all, [type]: current };
    });
  }

  private patch<T>(target: WritableSignal<ByType<T>>, type: FavoriteEntityType, value: T): void {
    target.update((all) => ({ ...all, [type]: value }));
  }
}
