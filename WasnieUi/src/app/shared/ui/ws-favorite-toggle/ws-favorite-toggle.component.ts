import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';
import { WsTooltipDirective } from '../ws-tooltip/ws-tooltip.directive';

/**
 * The favorite star: an icon-only toggle, filled when on, outlined when off (KAN-64).
 *
 * ★ PRESENTATIONAL ON PURPOSE. It knows nothing about favorites, entity types or the server — it shows a state and
 * says it was pressed. The favorites feature wraps it (`app-favorite-star`) with the store; keeping the store out of
 * `shared/ui` is what lets a primitive stay a primitive.
 *
 * ★ IT STOPS THE CLICK, like `ws-copy-button` and for the same reason: its home is a `routerLink` row, and starring a
 * payee must not also open it.
 *
 * ★ `aria-pressed`, NOT A LABEL THAT FLIPS ALONE. A toggle button announces its state; the tooltip and label still say
 * what pressing it will DO ("Add to favorites" / "Remove from favorites").
 */
@Component({
  selector: 'ws-favorite-toggle',
  standalone: true,
  imports: [IconComponent, TranslateModule, WsTooltipDirective],
  templateUrl: './ws-favorite-toggle.component.html',
  styleUrl: './ws-favorite-toggle.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WsFavoriteToggleComponent {
  /** Whether the entity is a favorite now. */
  readonly active = input.required<boolean>();

  /** A request for this star is in flight: presses are ignored so a double-click cannot race itself. */
  readonly busy = input(false);

  /** Icon edge in px. Table rows use 16. */
  readonly size = input(16);

  /** Translation key for the action when OFF. */
  readonly addLabel = input('FAVORITES.ADD');

  /** Translation key for the action when ON. */
  readonly removeLabel = input('FAVORITES.REMOVE');

  readonly toggled = output<void>();

  press(event: MouseEvent): void {
    event.stopPropagation();
    event.preventDefault();

    if (this.busy()) {
      return;
    }

    this.toggled.emit();
  }
}
