import { Component, computed, input } from '@angular/core';

export type BadgeVariant = 'neutral' | 'brand' | 'success' | 'warning' | 'danger' | 'info';
export type BadgeSize = 'sm' | 'md';

@Component({
  selector: 'ws-badge',
  standalone: true,
  templateUrl: './ws-badge.component.html',
  styleUrl: './ws-badge.component.scss',
})
export class WsBadgeComponent {
  readonly variant = input<BadgeVariant>('neutral');
  readonly size = input<BadgeSize>('md');
  readonly dot = input(false);

  readonly classes = computed(() =>
    ['ws-badge', `ws-badge--${this.variant()}`, `ws-badge--${this.size()}`].join(' ')
  );
}
