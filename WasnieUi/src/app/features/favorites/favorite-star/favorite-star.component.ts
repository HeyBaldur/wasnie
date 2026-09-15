import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { WsFavoriteToggleComponent } from '../../../shared/ui';
import { FavoriteEntityType } from '../models/favorite.model';
import { FavoritesStore } from '../state/favorites.store';

/**
 * The star, connected: give it a type and an id, and it reads and writes the shared favorites state (KAN-64).
 *
 * This is the one thing a section drops into a row. Every star for the same entity — in the quick-access table and in
 * the list — is bound to the same store entry, so they can never disagree.
 */
@Component({
  selector: 'app-favorite-star',
  standalone: true,
  imports: [WsFavoriteToggleComponent],
  template: `
    <ws-favorite-toggle
      [active]="store.isFavorite(entityType(), entityId())"
      [busy]="store.isBusy(entityType(), entityId())"
      [size]="size()"
      (toggled)="store.toggle(entityType(), entityId())"
    />
  `,
  styles: [':host { display: inline-flex; line-height: 0; }'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FavoriteStarComponent {
  readonly store = inject(FavoritesStore);

  readonly entityType = input.required<FavoriteEntityType>();
  readonly entityId = input.required<string>();
  readonly size = input(16);
}
