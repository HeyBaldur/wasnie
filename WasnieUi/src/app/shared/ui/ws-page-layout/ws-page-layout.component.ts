import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../components/icon/icon.component';
import { NgClass } from '@angular/common';

export type PageMaxWidth = 'narrow' | 'standard' | 'wide';

@Component({
  selector: 'ws-page-layout',
  standalone: true,
  imports: [RouterLink, IconComponent, NgClass],
  templateUrl: './ws-page-layout.component.html',
  styleUrl: './ws-page-layout.component.scss',
})
export class WsPageLayoutComponent {
  readonly icon = input.required<string>();
  readonly title = input.required<string>();
  readonly subtitle = input('');
  readonly backLink = input('');
  readonly backLabel = input('');
  readonly maxWidth = input<PageMaxWidth>('standard');
}
