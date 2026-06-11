import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsModalComponent, WsButtonComponent } from '../../ui';
import { TierLimitModalService, TierLimitInfo } from './tier-limit-modal.service';

@Component({
  selector: 'app-tier-limit-modal',
  standalone: true,
  imports: [WsModalComponent, WsButtonComponent, TranslatePipe],
  templateUrl: './tier-limit-modal.component.html',
  styleUrl: './tier-limit-modal.component.scss',
})
export class TierLimitModalComponent {
  readonly modal = inject(TierLimitModalService);
  private readonly router = inject(Router);

  upgrade(): void {
    this.modal.close();
    void this.router.navigateByUrl('/subscription');
  }

  usagePercent(info: TierLimitInfo): number {
    if (info.limit <= 0) return 100;
    return Math.min(100, Math.round((info.currentCount / info.limit) * 100));
  }
}
