/**
 * KAN-64: one favorites state behind every star.
 *
 * ★ THE FIXTURES HAVE THE SHAPE THE REAL ENDPOINT SENDS (§A4): `status` is the enum NAME as a string, and a refusal is
 * `{ messageKey, parameters }` — verified against `GET /api/favorites/payee` and the 422 of the limit.
 */
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ToastService } from '../../../shared/services/toast.service';
import { FavoriteItem } from '../models/favorite.model';
import { FavoritesStore } from './favorites.store';

const ALEKSANDRA: FavoriteItem = { entityId: 'p-1', name: 'Aleksandra Wojcik', code: 'EMP402', version: null, status: 'Active' };
const ANDREA: FavoriteItem = { entityId: 'p-2', name: 'Andrea Gomez', code: 'NB-3056', version: null, status: 'Active' };

describe('FavoritesStore', () => {
  let store: FavoritesStore;
  let http: HttpTestingController;
  let toast: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['show']);
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ToastService, useValue: toast }],
    });
    store = TestBed.inject(FavoritesStore);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function loadWith(items: FavoriteItem[]): Promise<void> {
    const p = store.load('payee');
    http.expectOne('/api/favorites/payee').flush(items);
    await p;
  }

  it('loads a type and answers isFavorite from it', async () => {
    await loadWith([ALEKSANDRA]);

    expect(store.items('payee')).toEqual([ALEKSANDRA]);
    expect(store.isFavorite('payee', 'p-1')).toBeTrue();
    expect(store.isFavorite('payee', 'p-2')).toBeFalse();
    // Types never bleed into each other.
    expect(store.isFavorite('plan', 'p-1')).toBeFalse();
  });

  it('★ removing flips the star AND empties the quick-access row before the server answers, then toasts', async () => {
    await loadWith([ALEKSANDRA, ANDREA]);

    const p = store.toggle('payee', 'p-1');

    // Both views read this at once: the list star is outlined and the row above is gone.
    expect(store.isFavorite('payee', 'p-1')).toBeFalse();
    expect(store.visibleItems('payee').map((i) => i.entityId)).toEqual(['p-2']);
    expect(store.isBusy('payee', 'p-1')).toBeTrue();

    const req = http.expectOne('/api/favorites/payee/p-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await p;

    expect(store.items('payee').map((i) => i.entityId)).toEqual(['p-2']);
    expect(store.isBusy('payee', 'p-1')).toBeFalse();
    expect(toast.show).toHaveBeenCalledWith('FAVORITES.TOAST_REMOVED', 'success');
  });

  it('adding flips the star at once, then re-reads so the row carries what the SERVER resolved', async () => {
    await loadWith([]);

    const p = store.toggle('payee', 'p-1');
    expect(store.isFavorite('payee', 'p-1')).toBeTrue();

    const put = http.expectOne('/api/favorites/payee/p-1');
    expect(put.request.method).toBe('PUT');
    put.flush(null, { status: 204, statusText: 'No Content' });
    await Promise.resolve();
    await Promise.resolve();

    http.expectOne('/api/favorites/payee').flush([ALEKSANDRA]);
    await p;

    expect(store.visibleItems('payee')).toEqual([ALEKSANDRA]);
    expect(store.isFavorite('payee', 'p-1')).toBeTrue();
    expect(toast.show).toHaveBeenCalledWith('FAVORITES.TOAST_ADDED', 'success');
  });

  it('★ a refused add reverts the star — it must not claim a favorite that does not exist', async () => {
    await loadWith([]);

    const p = store.toggle('payee', 'p-9');
    http.expectOne('/api/favorites/payee/p-9').flush(
      { messageKey: 'FAVORITES.NOT_FOUND' }, { status: 404, statusText: 'Not Found' });
    await p;

    expect(store.isFavorite('payee', 'p-9')).toBeFalse();
    expect(store.isBusy('payee', 'p-9')).toBeFalse();
    expect(toast.show).toHaveBeenCalledWith('FAVORITES.ERROR_NOT_AVAILABLE', 'error', undefined);
  });

  it('a press while the request is in flight is ignored', async () => {
    await loadWith([ALEKSANDRA]);

    const first = store.toggle('payee', 'p-1');
    await store.toggle('payee', 'p-1');

    // Only ONE request went out.
    http.expectOne('/api/favorites/payee/p-1').flush(null, { status: 204, statusText: 'No Content' });
    await first;
  });

  describe('errorToast — an explicit whitelist (§C2)', () => {
    const httpError = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

    it('the limit carries its number from the server', () => {
      expect(FavoritesStore.errorToast(httpError(422, { messageKey: 'FAVORITES.LIMIT_REACHED', parameters: { max: 50 } })))
        .toEqual({ key: 'FAVORITES.ERROR_LIMIT_REACHED', params: { max: 50 } });
    });

    it('an unknown key is never passed through', () => {
      expect(FavoritesStore.errorToast(httpError(422, { messageKey: 'SOMETHING.INTERNAL' })))
        .toEqual({ key: 'FAVORITES.ERROR_GENERIC' });
    });

    it('a 403 without a key becomes the generic sentence', () => {
      expect(FavoritesStore.errorToast(httpError(403, { error: 'Forbidden', message: 'Permission denied: Plans.Read' })))
        .toEqual({ key: 'FAVORITES.ERROR_GENERIC' });
    });
  });
});
