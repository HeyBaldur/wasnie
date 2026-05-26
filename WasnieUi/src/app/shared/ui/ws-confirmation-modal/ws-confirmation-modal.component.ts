import { Component, input, model, output } from '@angular/core';
import { WsModalComponent } from '../ws-modal/ws-modal.component';
import { WsButtonComponent } from '../ws-button/ws-button.component';

@Component({
  selector: 'ws-confirmation-modal',
  standalone: true,
  imports: [WsModalComponent, WsButtonComponent],
  templateUrl: './ws-confirmation-modal.component.html',
  styleUrl: './ws-confirmation-modal.component.scss',
})
export class WsConfirmationModalComponent {
  readonly isOpen = model(false);
  readonly title = input.required<string>();
  readonly message = input('');
  readonly confirmLabel = input.required<string>();
  readonly cancelLabel = input('Cancel');
  readonly variant = input<'default' | 'danger'>('default');
  readonly loading = input(false);

  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  onConfirm(): void {
    this.confirmed.emit();
  }

  onCancel(): void {
    this.cancelled.emit();
    this.isOpen.set(false);
  }
}
