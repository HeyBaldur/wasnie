import { Component, computed, effect, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, startWith } from 'rxjs/operators';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { SidebarStateService } from '../../../core/services/sidebar-state.service';
import { IconComponent } from '../icon/icon.component';
import { HasPermissionDirective } from '../../directives/has-permission.directive';
import { SubscriptionStateService } from '../../../features/subscription/services/subscription-state.service';
import { HubSpotSyncBannerComponent } from '../../../features/integrations/components/hubspot-sync-banner/hubspot-sync-banner.component';
import { AssistantStore } from '../../../features/assistant/state/assistant.store';

interface NavItem {
  path: string;
  labelKey: string;
  icon: string;
  permission: string;
}

interface NavGroupEntry {
  type: 'group';
  key: string;
  labelKey: string;
  icon: string;
  permission: string;
  children: NavItem[];
}

type NavEntry = NavItem | NavGroupEntry;

interface NavSection {
  sectionKey: string;
  items: NavEntry[];
}

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, TranslatePipe, IconComponent, HasPermissionDirective, HubSpotSyncBannerComponent],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent {
  private readonly authService = inject(AuthService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly router = inject(Router);
  readonly sidebarState = inject(SidebarStateService);
  private readonly subscriptionState = inject(SubscriptionStateService);

  // Reads from the same root-singleton already loaded by AppShellComponent.
  // To revert logo gradient: remove the @if overlay blocks in the template.
  readonly tierName = computed(() => this.subscriptionState.subscription()?.tier ?? null);

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      map(() => this.router.url.split('?')[0]),
      startWith(this.router.url.split('?')[0]),
    ),
    { initialValue: this.router.url.split('?')[0] },
  );

  readonly expandedGroups = signal<Set<string>>(new Set<string>());

  constructor() {
    // Auto-expand any group that contains the active route
    effect(() => {
      const url = this.currentUrl();
      for (const section of this.navSections) {
        for (const entry of section.items) {
          if (!this.isNavGroup(entry)) continue;
          const hasActiveChild = entry.children.some(
            c => url === c.path || url.startsWith(c.path + '/'),
          );
          if (hasActiveChild) {
            this.expandedGroups.update(s => {
              if (s.has(entry.key)) return s;
              const next = new Set(s);
              next.add(entry.key);
              return next;
            });
          }
        }
      }
    });
  }

  isNavGroup(entry: NavEntry): entry is NavGroupEntry {
    return (entry as NavGroupEntry).type === 'group';
  }

  toggleGroup(key: string): void {
    this.expandedGroups.update(s => {
      const next = new Set(s);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });
  }

  isGroupExpanded(key: string): boolean {
    return this.expandedGroups().has(key);
  }

  isGroupActive(children: NavItem[]): boolean {
    return children.some(c => this.isNavActive(c.path));
  }

  isNavActive(path: string): boolean {
    const url = this.currentUrl();
    if (path === '/transactions') {
      // Don't highlight Transactions when the import sub-route is active
      return url.startsWith('/transactions') && !url.startsWith('/transactions/import');
    }
    return url === path || url.startsWith(path + '/');
  }

  /**
   * The assistant's entitlement, for the rail's own entry.
   *
   * ★★ IT CANNOT RIDE ON `*hasPermission` LIKE EVERY OTHER ITEM, and that is the whole reason
   * this entry is not in `navSections`. Access to the assistant is an ENTITLEMENT — a seat plus a paid
   * plan — not a role permission, and it is decided by the server. Inventing a permission string for it
   * would either show the link to people whose first click gets a 403, or hide it from people who have
   * paid for it; both are worse than one special case that reads the real gate.
   *
   * ★ HIDE, DO NOT DISABLE (Spec §5b.6): while the answer is unknown the entry renders nothing at
   * all rather than flashing a control the user may not be entitled to. Same rule the topbar trigger
   * follows, and the same signal, so the two cannot disagree.
   */
  readonly assistant = inject(AssistantStore);

  readonly navSections: NavSection[] = [
    {
      sectionKey: 'NAV.SECTION_OVERVIEW',
      items: [{ path: '/dashboard', labelKey: 'NAV.DASHBOARD', icon: 'dashboard', permission: 'Payees.Read' }],
    },
    {
      sectionKey: 'NAV.SECTION_SETUP',
      items: [
        { path: '/plans', labelKey: 'NAV.PLANS', icon: 'plans', permission: 'Plans.Read' },
        { path: '/quotas', labelKey: 'NAV.QUOTAS', icon: 'target', permission: 'Quotas.Read' },
        { path: '/payees', labelKey: 'NAV.PAYEES', icon: 'users', permission: 'Payees.Read' },
        { path: '/assignments', labelKey: 'NAV.ASSIGNMENTS', icon: 'user-check', permission: 'Assignments.Read' },
        { path: '/category-mappings', labelKey: 'NAV.CATEGORY_MAPPINGS', icon: 'tag', permission: 'CategoryMappings.Read' },
      ],
    },
    {
      sectionKey: 'NAV.SECTION_OPERATIONS',
      items: [
        { path: '/transactions', labelKey: 'NAV.TRANSACTIONS', icon: 'arrows-exchange', permission: 'Transactions.Read' },
        { path: '/credits', labelKey: 'NAV.CREDITS', icon: 'receipt', permission: 'Credits.Read' },
        {
          type: 'group',
          key: 'pay-financials',
          labelKey: 'NAV.PAY_GROUP',
          icon: 'coin',
          permission: 'Reports.ViewAll',
          children: [
            { path: '/pay-runs', labelKey: 'NAV.PAY_RUNS', icon: 'coin', permission: 'Reports.ViewAll' },
            { path: '/payouts', labelKey: 'NAV.PAYOUTS', icon: 'layers', permission: 'Reports.ViewAll' },
            { path: '/terminated-accounts', labelKey: 'NAV.TERMINATED_ACCOUNTS', icon: 'users', permission: 'Ledger.Read' },
          ],
        },
      ],
    },
  ];

  // The manual is NOT in this menu. It moved to the topbar, beside the user: it is help, not a place in
  // the product's navigation, and it sat oddly among Subscription / Integrations / Settings.
  readonly subscriptionItem: NavItem ={ path: '/subscription', labelKey: 'NAV.SUBSCRIPTION', icon: 'brand-stripe', permission: 'Subscription.Manage' };
  readonly integrationsItem: NavItem = { path: '/integrations', labelKey: 'NAV.INTEGRATIONS', icon: 'link-2', permission: 'Integrations.Manage' };
  readonly settingsItem: NavItem = { path: '/admin', labelKey: 'NAV.ADMIN', icon: 'settings', permission: 'Subscription.Manage' };

  logout(): void {
    this.currentUser.clear();
    this.authService.logout();
    this.router.navigateByUrl('/auth/login');
  }
}
