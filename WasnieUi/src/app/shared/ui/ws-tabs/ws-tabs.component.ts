import { Component, input, model } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';

export interface WsTab {
  value: string;
  /** Translation key (or raw text with `[translateLabels]="false"`). */
  label: string;
  icon?: string;
}

/**
 * Tabs as one joined, full-width button group that belongs to the card it sits in.
 *
 * ★ NOT `ws-segmented-control`. That one is an inline pill for switching a filter; this is navigation
 * between two views of the same card, so it spans the card's width and its buttons share borders —
 * the group reads as part of the card, not as a control floating on it.
 */
@Component({
  selector: 'ws-tabs',
  standalone: true,
  imports: [TranslatePipe, IconComponent],
  templateUrl: './ws-tabs.component.html',
  styleUrl: './ws-tabs.component.scss',
})
export class WsTabsComponent {
  readonly tabs = input.required<WsTab[]>();
  readonly value = model<string>('');
  readonly translateLabels = input(true);

  /**
   * `group` — a standalone joined button group with its own border and rounded ends.
   * `header` — the tabs ARE the card's header: flush with the card's edges (put the card on
   * `padding="none"` and the body in its own padded element), no outer border of their own, and the
   * open tab takes the body's surface so it reads as one piece with the content below.
   */
  readonly variant = input<'group' | 'header'>('group');

  select(value: string): void {
    this.value.set(value);
  }

  /** ← → move between tabs and select, wrapping at the ends — the ARIA tabs keyboard pattern. */
  onKeydown(event: KeyboardEvent, index: number): void {
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
    event.preventDefault();

    const tabs = this.tabs();
    const next = (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
    this.select(tabs[next].value);

    const buttons = (event.currentTarget as HTMLElement).closest('.ws-tabs')?.querySelectorAll('button');
    (buttons?.[next] as HTMLButtonElement | undefined)?.focus();
  }
}
