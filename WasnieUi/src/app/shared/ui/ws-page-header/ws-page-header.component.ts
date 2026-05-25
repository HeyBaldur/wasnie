import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';

@Component({
  selector: 'ws-page-header',
  standalone: true,
  imports: [RouterLink, TranslatePipe, IconComponent],
  templateUrl: './ws-page-header.component.html',
  styleUrl: './ws-page-header.component.scss',
})
export class WsPageHeaderComponent {
  readonly title = input.required<string>();
  readonly subtitle = input('');
  readonly backRoute = input('');
  readonly backLabel = input('COMMON.BACK');
}
