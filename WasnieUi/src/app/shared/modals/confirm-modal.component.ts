import { Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { ModalService } from './modal.service';
import { WsButtonComponent, WsModalComponent } from '../ui';

@Component({
  selector: 'app-confirm-modal',
  standalone: true,
  imports: [TranslatePipe, WsButtonComponent, WsModalComponent],
  templateUrl: './confirm-modal.component.html',
  styleUrl: './confirm-modal.component.scss',
})
export class ConfirmModalComponent {
  readonly modalService = inject(ModalService);
}
