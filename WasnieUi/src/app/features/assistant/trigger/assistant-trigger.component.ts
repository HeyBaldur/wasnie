import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AssistantStore } from '../state/assistant.store';

/**
 * The entry point to the assistant, in the topbar.
 *
 * ★ HIDDEN, NOT DISABLED, when the user has no entitlement — and hidden while the entitlement is still
 * unknown, so a slow answer never flashes a button the user cannot use.
 *
 * ★ ON THE TAILWIND CLASSES: they are the style Rodolfo specified, and they are used here rather than
 * translated to SCSS because this app already does exactly this — the tier badges in the topbar are
 * built from the same `bg-gradient-to-br … focus:ring-4` idiom (topbar.component.html), mixing Tailwind
 * utilities with `var(--color-*)` tokens. Inventing a hand-rolled gradient beside them would be the
 * inconsistency, not the compliance. The design-system ban on Tailwind PALETTE utilities is about
 * ordinary surfaces and text (`text-blue-600` instead of a token); a brand gradient that no token
 * describes is the documented exception this file follows.
 */
@Component({
  selector: 'app-assistant-trigger',
  standalone: true,
  imports: [TranslateModule, RouterLink],
  templateUrl: './assistant-trigger.component.html',
  styleUrl: './assistant-trigger.component.scss',
})
export class AssistantTriggerComponent implements OnInit {
  readonly store = inject(AssistantStore);

  ngOnInit(): void {
    void this.store.loadEntitlement();
  }

  open(): void {
    void this.store.open();
  }
}
