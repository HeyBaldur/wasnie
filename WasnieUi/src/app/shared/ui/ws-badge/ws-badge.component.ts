import { Component, computed, input } from '@angular/core';

/** `sandbox` marks practice data and the practice environment (guided tour) — never a real status. */
export type BadgeVariant = 'neutral' | 'brand' | 'success' | 'warning' | 'danger' | 'info' | 'sandbox';
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
