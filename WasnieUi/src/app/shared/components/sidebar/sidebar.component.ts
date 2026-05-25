import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { SidebarStateService } from '../../../core/services/sidebar-state.service';
import { IconComponent } from '../icon/icon.component';
import { Router } from '@angular/router';

interface NavItem {
  path: string;
  labelKey: string;
  icon: string;
}

interface NavSection {
  sectionKey: string;
  items: NavItem[];
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, TranslatePipe, IconComponent],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  readonly sidebarState = inject(SidebarStateService);

  readonly navSections: NavSection[] = [
    {
      sectionKey: 'NAV.SECTION_OVERVIEW',
      items: [{ path: '/dashboard', labelKey: 'NAV.DASHBOARD', icon: 'dashboard' }],
    },
    {
      sectionKey: 'NAV.SECTION_COMPENSATION',
      items: [
        { path: '/plans', labelKey: 'NAV.PLANS', icon: 'plans' },
        { path: '/quotas', labelKey: 'NAV.QUOTAS', icon: 'target' },
        { path: '/assignments', labelKey: 'NAV.ASSIGNMENTS', icon: 'user-check' },
      ],
    },
    {
      sectionKey: 'NAV.SECTION_OPERATIONS',
      items: [
        { path: '/payees', labelKey: 'NAV.PAYEES', icon: 'users' },
        { path: '/transactions', labelKey: 'NAV.TRANSACTIONS', icon: 'arrows-exchange' },
        { path: '/payouts', labelKey: 'NAV.PAYOUTS', icon: 'coin' },
      ],
    },
  ];

  readonly settingsItem: NavItem = { path: '/admin', labelKey: 'NAV.ADMIN', icon: 'settings' };

  logout(): void {
    this.authService.logout();
    this.router.navigateByUrl('/auth/login');
  }
}
