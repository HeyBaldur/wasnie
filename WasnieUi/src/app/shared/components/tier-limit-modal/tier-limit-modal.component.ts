import { Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WsModalComponent, WsButtonComponent } from '../../ui';
import { TierLimitModalService } from './tier-limit-modal.service';

@Component({
  selector: 'app-tier-limit-modal',
  standalone: true,
  imports: [WsModalComponent, WsButtonComponent, TranslatePipe],
  templateUrl: './tier-limit-modal.component.html',
  styleUrl: './tier-limit-modal.component.scss',
})
export class TierLimitModalComponent {
  readonly modal = inject(TierLimitModalService);
}
