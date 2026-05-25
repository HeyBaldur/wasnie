import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'ws-button',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './ws-button.component.html',
  styleUrl: './ws-button.component.scss',
})
export class WsButtonComponent {
  readonly variant = input<'primary' | 'secondary' | 'ghost' | 'danger' | 'link'>('primary');
  readonly size = input<'sm' | 'md' | 'lg'>('md');
  readonly loading = input(false);
  readonly disabled = input(false);
  readonly routerLink = input<string | string[] | null>(null);
  readonly iconOnly = input(false);
  readonly fullWidth = input(false);
  readonly type = input<'button' | 'submit' | 'reset'>('button');

  readonly isDisabled = computed(() => this.disabled() || this.loading());

  readonly classes = computed(() =>
    [
      'ws-btn',
      `ws-btn--${this.variant()}`,
      `ws-btn--${this.size()}`,
      this.iconOnly() ? 'ws-btn--icon-only' : null,
      this.fullWidth() ? 'ws-btn--full' : null,
    ]
      .filter(Boolean)
      .join(' ')
  );
}
