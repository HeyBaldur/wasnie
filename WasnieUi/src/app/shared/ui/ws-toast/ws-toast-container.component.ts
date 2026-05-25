import { Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WsToastService, WsToastItem } from './ws-toast.service';

@Component({
  selector: 'ws-toast-container',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './ws-toast-container.component.html',
  styleUrl: './ws-toast-container.component.scss',
})
export class WsToastContainerComponent {
  readonly toastService = inject(WsToastService);

  trackToast(_: number, item: WsToastItem): string {
    return item.id;
  }

  dismiss(id: string): void {
    this.toastService.dismiss(id);
  }
}
