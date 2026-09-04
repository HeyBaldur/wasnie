import { Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';
import { WsToastService, WsToastItem, WsToastType } from './ws-toast.service';

/**
 * The icon each kind of toast wears.
 *
 * ★ AN EXPLICIT MAP, NOT A NAME BUILT FROM THE TYPE (§C2, the same rule as the translation
 * whitelist). `IconComponent` renders nothing for a name it does not know, so a computed
 * `type + '-circle'` would fail silently and leave a toast with a hole where its icon should be.
 * Typed over the union, so a fifth toast type is a compile error rather than a blank square.
 */
const TOAST_ICONS: Readonly<Record<WsToastType, string>> = {
  success: 'check-circle',
  error: 'x-circle',
  warning: 'alert-triangle',
  info: 'info-circle',
};

@Component({
  selector: 'ws-toast-container',
  standalone: true,
  imports: [TranslatePipe, IconComponent],
  templateUrl: './ws-toast-container.component.html',
  styleUrl: './ws-toast-container.component.scss',
})
export class WsToastContainerComponent {
  readonly toastService = inject(WsToastService);

  trackToast(_: number, item: WsToastItem): string {
    return item.id;
  }

  iconFor(type: WsToastType): string {
    return TOAST_ICONS[type];
  }

  dismiss(id: string): void {
    this.toastService.dismiss(id);
  }

  /** The reader is on this toast — hold its countdown until they leave. */
  pause(id: string): void {
    this.toastService.pause(id);
  }

  /** They looked away: carry on with whatever time was left, not with a fresh four seconds. */
  resume(id: string): void {
    this.toastService.resume(id);
  }
}
