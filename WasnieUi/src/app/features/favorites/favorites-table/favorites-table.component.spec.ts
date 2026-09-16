/**
 * KAN-64: the quick-access table and a list star, mounted side by side, bound to the one store.
 *
 * ★ WHAT IS PINNED IS THE SYNC FROM THE TICKET COMMENT: un-starring from the LIST removes the row from the table above
 * at once, and a favorite still shows its filled star in the list — without a reload.
 */
import { Component } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

import { ToastService } from '../../../shared/services/toast.service';
import { FavoriteStarComponent } from '../favorite-star/favorite-star.component';
import { FavoriteItem } from '../models/favorite.model';
import { FavoritesTableComponent } from './favorites-table.component';

@Component({
  standalone: true,
  imports: [FavoritesTableComponent, FavoriteStarComponent],
  template: `
    <app-favorites-table entityType="plan" />
    <div class="list-row"><app-favorite-star entityType="plan" entityId="plan-1" /></div>
  `,
})
class PageComponent {}

const PLAN: FavoriteItem = { entityId: 'plan-1', name: 'EU Standard Commission 2026', code: null, version: 3, status: 'Active' };

describe('FavoritesTableComponent', () => {
  let fixture: ComponentFixture<PageComponent>;
  let http: HttpTestingController;

  const rows = () => fixture.nativeElement.querySelectorAll('[data-testid="favorites-row"]') as NodeListOf<HTMLElement>;
  const table = () => fixture.nativeElement.querySelector('[data-testid="favorites-table"]');
  const listStar = () => fixture.nativeElement.querySelector('.list-row button') as HTMLButtonElement;

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PageComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['show']) },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PageComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('takes no space when the user has no favorites', async () => {
    http.expectOne('/api/favorites/plan').flush([]);
    await settle();

    expect(table()).toBeNull();
    expect(listStar().getAttribute('aria-pressed')).toBe('false');
  });

  it('paints the favorite with its version and translated status, and the list star is filled', async () => {
    http.expectOne('/api/favorites/plan').flush([PLAN]);
    await settle();

    expect(rows().length).toBe(1);
    expect(rows()[0].textContent).toContain('EU Standard Commission 2026');
    expect(rows()[0].textContent).toContain('v3');
    expect(rows()[0].textContent).toContain('PLANS.STATUS_ACTIVE');
    expect(listStar().getAttribute('aria-pressed')).toBe('true');
  });

  it('★ un-starring from the LIST empties the table above at once, before the server answers', async () => {
    http.expectOne('/api/favorites/plan').flush([PLAN]);
    await settle();

    listStar().click();
    fixture.detectChanges();

    expect(table()).toBeNull();
    expect(listStar().getAttribute('aria-pressed')).toBe('false');

    http.expectOne('/api/favorites/plan/plan-1').flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    expect(table()).toBeNull();
  });

  it('an unknown status is painted with the fallback, never as a raw identifier (§C2)', async () => {
    http.expectOne('/api/favorites/plan').flush([{ ...PLAN, status: 'Frozen' }]);
    await settle();

    expect(rows()[0].textContent).toContain('FAVORITES.STATUS_UNKNOWN');
    expect(rows()[0].textContent).not.toContain('Frozen');
  });
});
