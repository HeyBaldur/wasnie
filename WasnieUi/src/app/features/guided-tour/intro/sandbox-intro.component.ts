import { Component, OnInit, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent } from '../../../shared/ui';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { AssistantStore } from '../../assistant/state/assistant.store';

/**
 * Same `wasnie:` prefix as every other storage key in the app (see WelcomeService): the keys kept the
 * internal name through the Incentra rebrand so values already in users' browsers are not orphaned.
 */
const DISMISSED_KEY = 'wasnie:sandbox-intro-dismissed';

function isDismissed(): boolean {
  try {
    return localStorage.getItem(DISMISSED_KEY) === '1';
  } catch {
    // Storage disabled: treat it as dismissed rather than showing it on every visit.
    return true;
  }
}

/**
 * The first-visit introduction to the sandbox: what this section is for, that nothing here is real,
 * and where to get help. Built on the celebration pattern (DESIGN_SYSTEM → Pattern — celebration /
 * welcome modal), as a card instead of a modal: it explains the page it sits on, so it must not hide it.
 *
 * ★ CLOSED ONCE, GONE FOR GOOD. The X writes the flag; the card never comes back on this browser.
 *
 * ★ ZEKE ONLY FOR WHO HAS IT. The assistant is a paid entitlement: the button (and the sentence that
 * offers it) appear only when `entitled` is true — hidden, not disabled, and hidden while unknown, the
 * same rule as the topbar trigger.
 */
@Component({
  selector: 'app-sandbox-intro',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent, IconComponent],
  templateUrl: './sandbox-intro.component.html',
  styleUrl: './sandbox-intro.component.scss',
})
export class SandboxIntroComponent implements OnInit {
  readonly assistant = inject(AssistantStore);
  readonly visible = signal(!isDismissed());

  readonly points = [
    { icon: 'plans', key: 'GUIDED.INTRO.POINT_TRY' },
    { icon: 'shield-check', key: 'GUIDED.INTRO.POINT_SAFE' },
    { icon: 'archive', key: 'GUIDED.INTRO.POINT_PROMOTE' },
  ] as const;

  ngOnInit(): void {
    // The topbar trigger usually loaded it already; ask only if nobody has.
    if (this.visible() && this.assistant.entitled() === null) void this.assistant.loadEntitlement();
  }

  dismiss(): void {
    try {
      localStorage.setItem(DISMISSED_KEY, '1');
    } catch {
      // The write failed: it may show again next visit, which is the acceptable failure.
    }
    this.visible.set(false);
  }

  askZeke(): void {
    void this.assistant.open();
  }
}
