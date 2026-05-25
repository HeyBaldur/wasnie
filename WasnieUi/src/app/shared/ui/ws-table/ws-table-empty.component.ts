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
}
