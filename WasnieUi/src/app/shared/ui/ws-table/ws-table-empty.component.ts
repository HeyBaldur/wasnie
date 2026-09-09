import { Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';

@Component({
  selector: 'ws-table-empty',
  standalone: true,
  imports: [TranslatePipe, IconComponent],
  templateUrl: './ws-table-empty.component.html',
  styleUrl: './ws-table-empty.component.scss',
})
export class WsTableEmptyComponent {
  readonly icon = input('search');
  readonly messageKey = input('COMMON.EMPTY');

  /**
   * Interpolation values for `messageKey`, when the message needs to state a number.
   *
   * ★ Additive with an empty default, so every existing caller keeps rendering exactly what it did.
   *   It exists because "nothing here" is often not the useful sentence — "all 15 payouts in this run
   *   are worth nothing" is, and that cannot be said without the count.
   */
  readonly messageParams = input<Record<string, unknown>>({});
}
