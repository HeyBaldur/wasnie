import { ChangeDetectionStrategy, Component, OnInit, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { WsBadgeComponent, WsClickableRowDirective, WsTableComponent } from '../../../shared/ui';
import { FAVORITE_TYPES, FALLBACK_STATUS, FavoriteStatusDisplay, FavoriteTypeConfig } from '../favorite-types';
import { FavoriteEntityType } from '../models/favorite.model';
import { FavoritesStore } from '../state/favorites.store';
import { FavoriteStarComponent } from '../favorite-star/favorite-star.component';

/**
 * The quick-access table above a section's list (KAN-64). Give it a type; it loads and paints that type's favorites.
 *
 * ★ AN ADDITION TO THE LIST, NOT A FILTER OF IT. A favorite stays in the list below with its star filled; this table is
 * a shortcut on top (ticket comment). Both stars are bound to the same store entry.
 *
 * ★ NO FAVORITES, NO TABLE. It takes no space until there is something to show — including while the first load is in
 * flight, so the list does not jump down and back up for a user who has none.
 */
@Component({
  selector: 'app-favorites-table',
  standalone: true,
  imports: [
    RouterLink,
    TranslateModule,
    IconComponent,
    WsBadgeComponent,
    WsClickableRowDirective,
    WsTableComponent,
    FavoriteStarComponent,
  ],
  templateUrl: './favorites-table.component.html',
  styleUrl: './favorites-table.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FavoritesTableComponent implements OnInit {
  readonly store = inject(FavoritesStore);

  readonly entityType = input.required<FavoriteEntityType>();

  get config(): FavoriteTypeConfig {
    return FAVORITE_TYPES[this.entityType()];
  }

  ngOnInit(): void {
    // Every visit re-reads: favorites are per user and can change from another tab or device.
    void this.store.load(this.entityType());
  }

  status(name: string): FavoriteStatusDisplay {
    return this.config.statuses[name] ?? FALLBACK_STATUS;
  }
}
