import { Component, inject, OnDestroy, OnInit } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { SidebarComponent } from '../sidebar/sidebar.component';
import { TopbarComponent } from '../topbar/topbar.component';
import { SidebarStateService } from '../../../core/services/sidebar-state.service';
import { InactivityService } from '../../../core/services/inactivity.service';
import { WsToastContainerComponent } from '../../ui/ws-toast/ws-toast-container.component';
import { WsConfirmationModalComponent } from '../../ui';
import { SubscriptionStateService } from '../../../features/subscription/services/subscription-state.service';
import { PastDueBannerComponent } from '../../../features/subscription/past-due-banner/past-due-banner.component';
import { TwoFaReminderComponent } from '../two-fa-reminder/two-fa-reminder.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    SidebarComponent,
    TopbarComponent,
    WsToastContainerComponent,
    WsConfirmationModalComponent,
    PastDueBannerComponent,
    TwoFaReminderComponent,
    TranslateModule,
  ],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent implements OnInit, OnDestroy {
  readonly sidebarState = inject(SidebarStateService);
  readonly inactivity = inject(InactivityService);
  readonly subState = inject(SubscriptionStateService);

  ngOnInit(): void {
    this.inactivity.start();
    this.subState.load();
  }

  ngOnDestroy(): void {
    this.inactivity.stop();
  }
}
