import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';
import { WsTooltipDirective } from '../ws-tooltip/ws-tooltip.directive';

/**
 * Copy a value to the clipboard: an icon-only button that flips to a tick for two seconds.
 *
 * ★ WHY A PRIMITIVE. The same button was hand-rolled three times already — the plan id in the plan
 * header, and twice over the reference number in Process Pending — each with its own `copied` signal,
 * its own `setTimeout` and its own styling. A fourth copy (the payee name) is where that stops being a
 * coincidence and starts being a pattern, so the pattern lives in one file.
 *
 * ★ IT STOPS THE CLICK. Its intended home is a table cell, and payee/plan rows are themselves
 * `routerLink`s: without `stopPropagation` copying a name would also navigate away from the list the
 * user is copying it from. Callers must not have to remember this — a copy button that navigates is
 * never what anyone wanted, so the swallow is unconditional and lives here.
 *
 * ★ THE FEEDBACK IS THE ICON, NOT A TOAST. A toast for something this small is noise, and the tick +
 * "Copied!" tooltip says it where the user is already looking.
 */
@Component({
  selector: 'ws-copy-button',
  standalone: true,
  imports: [IconComponent, TranslateModule, WsTooltipDirective],
  templateUrl: './ws-copy-button.component.html',
  styleUrl: './ws-copy-button.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WsCopyButtonComponent {
  private readonly destroyRef = inject(DestroyRef);

  /** The text that reaches the clipboard. An empty value disables the button rather than copying "". */
  readonly value = input.required<string>();

  /**
   * Translation key for the accessible name, and for the idle tooltip when {@link tooltip} is absent.
   * Defaults to the generic "Copy"; pass COMMON.COPY_ID and the like when the page can be specific.
   */
  readonly label = input('COMMON.COPY');

  /**
   * Raw, ALREADY-RESOLVED text for the idle tooltip — not a translation key. It exists for the plan
   * header, which shows the id itself on hover; a key would be wrong there because an id is not a
   * translatable string.
   */
  readonly tooltip = input<string | null>(null);

  /** Icon edge in px. 13 matches the meta chips it sits among; table cells use 14. */
  readonly size = input(13);

  readonly copied = signal(false);

  readonly disabled = computed(() => this.value().trim().length === 0);

  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.clearTimer());
  }

  async copy(event: MouseEvent): Promise<void> {
    // See the class note: the row underneath is usually a link.
    event.stopPropagation();
    event.preventDefault();

    if (this.disabled()) {
      return;
    }

    try {
      await navigator.clipboard.writeText(this.value());
    } catch {
      // ★ A DENIED CLIPBOARD MUST NOT CLAIM SUCCESS. The API rejects without a user gesture, over
      // plain http, and whenever the permission is refused. Showing the tick anyway would tell the
      // user their id is on the clipboard when it is not — and they would paste something stale.
      // Staying silent leaves the button visibly un-ticked, which is the honest signal.
      return;
    }

    this.copied.set(true);
    this.clearTimer();
    this.timer = setTimeout(() => this.copied.set(false), CopiedFeedbackMs);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}

/** How long the tick stays. Long enough to be seen, short enough not to look stuck. */
export const CopiedFeedbackMs = 2000;
