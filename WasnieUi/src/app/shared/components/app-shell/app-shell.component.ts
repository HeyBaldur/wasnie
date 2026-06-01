import { Component, inject, OnDestroy, OnInit } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { SidebarComponent } from '../sidebar/sidebar.component';
import { TopbarComponent } from '../topbar/topbar.component';
import { SidebarStateService } from '../../../core/services/sidebar-state.service';
import { InactivityService } from '../../../core/services/inactivity.service';
import { WsToastContainerComponent } from '../../ui/ws-toast/ws-toast-container.component';
import { WsConfirmationModalComponent } from '../../ui';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    SidebarComponent,
    TopbarComponent,
    WsToastContainerComponent,
    WsConfirmationModalComponent,
    TranslateModule,
  ],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent implements OnInit, OnDestroy {
  readonly sidebarState = inject(SidebarStateService);
  readonly inactivity = inject(InactivityService);

  ngOnInit(): void {
    this.inactivity.start();
  }

  ngOnDestroy(): void {
    this.inactivity.stop();
  }
}
