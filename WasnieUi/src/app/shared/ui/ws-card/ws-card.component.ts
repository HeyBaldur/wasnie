import { Component, computed, input } from '@angular/core';

export type CardVariant = 'default' | 'flat' | 'raised' | 'interactive';
export type CardPadding = 'sm' | 'md' | 'lg' | 'none';
export type CardAccent = 'none' | 'brand' | 'success' | 'warning' | 'danger' | 'info';

@Component({
  selector: 'ws-card',
  standalone: true,
  templateUrl: './ws-card.component.html',
  styleUrl: './ws-card.component.scss',
})
export class WsCardComponent {
  readonly variant = input<CardVariant>('default');
  readonly padding = input<CardPadding>('md');
  readonly accent = input<CardAccent>('none');

  readonly classes = computed(() =>
    [
      'ws-card',
      `ws-card--${this.variant()}`,
      `ws-card--pad-${this.padding()}`,
      this.accent() !== 'none' ? `ws-card--accent-${this.accent()}` : null,
    ]
      .filter(Boolean)
      .join(' ')
  );
}
