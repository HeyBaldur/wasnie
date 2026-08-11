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
import { AssistantPanelComponent } from '../../../features/assistant/panel/assistant-panel.component';
import { WelcomeModalComponent } from '../welcome-modal/welcome-modal.component';
import { WelcomeService } from '../../../core/services/welcome.service';

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
    AssistantPanelComponent,
    WelcomeModalComponent,
    TranslateModule,
  ],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss',
})
export class AppShellComponent implements OnInit, OnDestroy {
  readonly sidebarState = inject(SidebarStateService);
  readonly inactivity = inject(InactivityService);
  readonly subState = inject(SubscriptionStateService);
  private readonly welcome = inject(WelcomeService);

  ngOnInit(): void {
    this.inactivity.start();
    this.subState.load();
    // First authenticated screen this browser has ever rendered → the product tour. Hooked here and
    // not in a route guard because the shell is the one place every signed-in page passes through,
    // and the onboarding screens (which do not use the shell) must not be interrupted by it.
    this.welcome.openIfFirstVisit();
  }

  ngOnDestroy(): void {
    this.inactivity.stop();
  }
}
