import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { AuthService } from '../../core/services/auth.service';
import { WsPageHeaderComponent, WsCardComponent, type CardAccent } from '../../shared/ui';

interface KpiCard {
  labelKey: string;
  value: string;
  trend: string;
  positive: boolean | null;
  icon: string;
}

interface MetricCard {
  titleKey: string;
  count: string;
  subtitle: string;
  route: string;
  accent: CardAccent;
  icon: string;
}

interface ActivityItem {
  initials: string;
  textKey: string;
  time: string;
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, TranslatePipe, AppShellComponent, IconComponent, WsPageHeaderComponent, WsCardComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {
  readonly authService = inject(AuthService);

  readonly kpis: KpiCard[] = [
    { labelKey: 'DASHBOARD.KPI_TOTAL_REVENUE', value: '$124,500', trend: '+12%', positive: true, icon: 'currency' },
    { labelKey: 'DASHBOARD.KPI_ACTIVE_PAYEES', value: '24', trend: '+3', positive: true, icon: 'users' },
    { labelKey: 'DASHBOARD.KPI_OPEN_PLANS', value: '8', trend: '0', positive: null, icon: 'plans' },
    { labelKey: 'DASHBOARD.KPI_PENDING_PAYOUTS', value: '$18,200', trend: '+$2.1k', positive: true, icon: 'coin' },
  ];

  readonly cards: MetricCard[] = [
    { titleKey: 'DASHBOARD.CARD_PLANS', count: '8', subtitle: '2 pending approval', route: '/plans', accent: 'brand', icon: 'plans' },
    { titleKey: 'DASHBOARD.CARD_QUOTAS', count: '73%', subtitle: 'avg. attainment', route: '/quotas', accent: 'success', icon: 'target' },
    { titleKey: 'DASHBOARD.CARD_ASSIGNMENTS', count: '24', subtitle: 'across 8 plans', route: '/assignments', accent: 'info', icon: 'user-check' },
    { titleKey: 'DASHBOARD.CARD_PAYEES', count: '24', subtitle: '3 inactive', route: '/payees', accent: 'warning', icon: 'users' },
    { titleKey: 'DASHBOARD.CARD_TRANSACTIONS', count: '142', subtitle: 'this month', route: '/transactions', accent: 'brand', icon: 'arrows-exchange' },
    { titleKey: 'DASHBOARD.CARD_PAYOUTS', count: '$18,200', subtitle: 'pending', route: '/payouts', accent: 'danger', icon: 'coin' },
  ];

  readonly activity: ActivityItem[] = [
    { initials: 'AK', textKey: 'DASHBOARD.ACT_1', time: '2m' },
    { initials: 'MB', textKey: 'DASHBOARD.ACT_2', time: '18m' },
    { initials: 'JL', textKey: 'DASHBOARD.ACT_3', time: '1h' },
    { initials: 'RK', textKey: 'DASHBOARD.ACT_4', time: '3h' },
    { initials: 'SA', textKey: 'DASHBOARD.ACT_5', time: '5h' },
  ];
}
