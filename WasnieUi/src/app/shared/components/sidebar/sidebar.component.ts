import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { SidebarStateService } from '../../../core/services/sidebar-state.service';
import { IconComponent } from '../icon/icon.component';
import { HasPermissionDirective } from '../../directives/has-permission.directive';
import { Router } from '@angular/router';

interface NavItem {
  path: string;
  labelKey: string;
  icon: string;
  permission: string;
}

interface NavSection {
  sectionKey: string;
  items: NavItem[];
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, TranslatePipe, IconComponent, HasPermissionDirective],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {
  private readonly authService = inject(AuthService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly router = inject(Router);
  readonly sidebarState = inject(SidebarStateService);

  readonly navSections: NavSection[] = [
    {
      sectionKey: 'NAV.SECTION_OVERVIEW',
      items: [{ path: '/dashboard', labelKey: 'NAV.DASHBOARD', icon: 'dashboard', permission: 'Payees.Read' }],
    },
    {
      sectionKey: 'NAV.SECTION_COMPENSATION',
      items: [
        { path: '/plans', labelKey: 'NAV.PLANS', icon: 'plans', permission: 'Plans.Read' },
        { path: '/quotas', labelKey: 'NAV.QUOTAS', icon: 'target', permission: 'Quotas.Read' },
        { path: '/assignments', labelKey: 'NAV.ASSIGNMENTS', icon: 'user-check', permission: 'Assignments.Read' },
      ],
    },
    {
      sectionKey: 'NAV.SECTION_OPERATIONS',
      items: [
        { path: '/payees', labelKey: 'NAV.PAYEES', icon: 'users', permission: 'Payees.Read' },
        { path: '/transactions', labelKey: 'NAV.TRANSACTIONS', icon: 'arrows-exchange', permission: 'Transactions.Read' },
        { path: '/payouts', labelKey: 'NAV.PAYOUTS', icon: 'coin', permission: 'Reports.ViewAll' },
      ],
    },
  ];

  readonly settingsItem: NavItem = { path: '/admin', labelKey: 'NAV.ADMIN', icon: 'settings', permission: 'Subscription.Manage' };

  logout(): void {
    this.currentUser.clear();
    this.authService.logout();
    this.router.navigateByUrl('/auth/login');
  }
}
