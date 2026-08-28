import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { IconComponent } from '../../components/icon/icon.component';
import { NgClass } from '@angular/common';

export type PageMaxWidth = 'narrow' | 'standard' | 'wide';

/**
 * Title scale. 'md' is the page-title default every screen uses. 'lg' is for headers whose title is
 * addressed to the person rather than naming the screen (the Dashboard greeting) — there the title IS
 * the content of the header, so it carries the weight a screen name doesn't need.
 */
export type PageTitleSize = 'md' | 'lg';

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
  readonly titleSize = input<PageTitleSize>('md');
}
