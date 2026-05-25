import { Component, inject } from '@angular/core';
import { SidebarComponent } from '../sidebar/sidebar.component';
import { TopbarComponent } from '../topbar/topbar.component';
import { SidebarStateService } from '../../../core/services/sidebar-state.service';
import { ConfirmModalComponent } from '../../modals/confirm-modal.component';
import { WsToastContainerComponent } from '../../ui/ws-toast/ws-toast-container.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [SidebarComponent, TopbarComponent, WsToastContainerComponent, ConfirmModalComponent],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent {
  readonly sidebarState = inject(SidebarStateService);
}
