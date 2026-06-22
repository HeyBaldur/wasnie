import { Routes } from '@angular/router';
import { planGuard } from './core/guards/plan.guard';
import { hasPermissionGuard } from './core/auth/guards/has-permission.guard';
import { subscriptionGuard } from './core/guards/subscription.guard';
import { environment } from '../environments/environment';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'dashboard',
    pathMatch: 'full',
  },
  {
    path: 'auth',
    loadChildren: () =>
      import('./features/auth/auth.routes').then((m) => m.authRoutes),
  },
  {
    path: 'onboarding',
    loadChildren: () =>
      import('./features/subscription/subscription.routes').then((m) => m.subscriptionRoutes),
  },
  {
    path: 'dashboard',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(
        (m) => m.DashboardComponent
      ),
  },
  {
    path: 'plans',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Plans.Read')],
    loadChildren: () =>
      import('./features/plans/plans.routes').then((m) => m.plansRoutes),
  },
  {
    path: 'payees',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Payees.Read')],
    loadChildren: () =>
      import('./features/payees/payees.routes').then((m) => m.payeesRoutes),
  },
  {
    path: 'transactions',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Transactions.Read')],
    loadChildren: () =>
      import('./features/transactions/transactions.routes').then((m) => m.transactionsRoutes),
  },
  {
    path: 'credits',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Credits.Read')],
    loadChildren: () =>
      import('./features/credits/credits.routes').then((m) => m.creditsRoutes),
  },
  {
    path: 'pay-runs',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Payouts.Read')],
    loadChildren: () =>
      import('./features/pay-runs/pay-runs.routes').then((m) => m.payRunsRoutes),
  },
  {
    path: 'payouts',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Payouts.Read')],
    loadChildren: () =>
      import('./features/payouts/payouts.routes').then((m) => m.payoutsRoutes),
  },
  {
    path: 'quotas',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Quotas.Read')],
    loadChildren: () =>
      import('./features/quotas/quotas.routes').then((m) => m.quotasRoutes),
  },
  {
    path: 'assignments',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Assignments.Read')],
    loadChildren: () =>
      import('./features/assignments/assignments.routes').then((m) => m.assignmentsRoutes),
  },
  {
    path: 'subscription',
    canActivate: [planGuard, hasPermissionGuard('Subscription.Manage')],
    loadChildren: () =>
      import('./features/subscription/subscription.routes').then((m) => m.manageSubscriptionRoutes),
  },
  {
    path: 'profile',
    canActivate: [planGuard, subscriptionGuard],
    loadChildren: () =>
      import('./features/profile/profile.routes').then(m => m.profileRoutes),
  },
  {
    path: 'profile/confirm-email-change',
    loadComponent: () =>
      import('./features/profile/confirm-email-change/confirm-email-change.component').then(
        m => m.ConfirmEmailChangeComponent,
      ),
  },
  {
    path: 'admin',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Subscription.Manage')],
    loadComponent: () =>
      import('./features/admin/admin.component').then(
        (m) => m.AdminComponent
      ),
  },
  {
    path: 'integrations',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Integrations.Manage')],
    loadComponent: () =>
      import('./features/integrations/integrations.component').then(
        (m) => m.IntegrationsComponent
      ),
  },
  {
    path: 'forbidden',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/forbidden/forbidden.component').then(
        (m) => m.ForbiddenComponent
      ),
  },
  ...(!environment.production
    ? [
        {
          path: '__design-system',
          loadComponent: () =>
            import('./features/design-system/design-system.component').then(
              (m) => m.DesignSystemComponent
            ),
        },
      ]
    : []),
  {
    path: '**',
    redirectTo: 'dashboard',
  },
];
