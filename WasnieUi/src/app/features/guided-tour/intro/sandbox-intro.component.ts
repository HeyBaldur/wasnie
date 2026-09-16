import { Component, OnInit, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent } from '../../../shared/ui';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { AssistantStore } from '../../assistant/state/assistant.store';
import { UI_PREFERENCE_KEYS, UiPreferencesService } from '../../../core/services/ui-preferences.service';

/**
 * The first-visit introduction to the sandbox: what this section is for, that nothing here is real,
 * and where to get help. Built on the celebration pattern (DESIGN_SYSTEM → Pattern — celebration /
 * welcome modal), as a card instead of a modal: it explains the page it sits on, so it must not hide it.
 *
 * ★ CLOSED ONCE, GONE FOR GOOD — FOR THIS USER, ON ANY MACHINE (KAN-78). The X writes a UI preference on the
 * server; it used to be a browser flag and came back on another machine. Hidden until the preferences are known.
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
  private readonly preferences = inject(UiPreferencesService);
  readonly visible = signal(false);

  readonly points = [
    { icon: 'plans', key: 'GUIDED.INTRO.POINT_TRY' },
    { icon: 'shield-check', key: 'GUIDED.INTRO.POINT_SAFE' },
    { icon: 'archive', key: 'GUIDED.INTRO.POINT_PROMOTE' },
  ] as const;

  async ngOnInit(): Promise<void> {
    const known = await this.preferences.ensureLoaded();
    if (!known || this.preferences.isFlagSet(UI_PREFERENCE_KEYS.sandboxIntroDismissed)) return;
    this.visible.set(true);
    // The topbar trigger usually loaded it already; ask only if nobody has.
    if (this.assistant.entitled() === null) void this.assistant.loadEntitlement();
  }

  dismiss(): void {
    void this.preferences.setFlag(UI_PREFERENCE_KEYS.sandboxIntroDismissed);
    this.visible.set(false);
  }

  askZeke(): void {
    void this.assistant.open();
  }
}
