import { Routes } from '@angular/router';
import { planGuard } from './core/guards/plan.guard';
import { hasPermissionGuard } from './core/auth/guards/has-permission.guard';
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
    canActivate: [planGuard],
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(
        (m) => m.DashboardComponent
      ),
  },
  {
    path: 'plans',
    canActivate: [planGuard, hasPermissionGuard('Plans.Read')],
    loadChildren: () =>
      import('./features/plans/plans.routes').then((m) => m.plansRoutes),
  },
  {
    path: 'payees',
    canActivate: [planGuard, hasPermissionGuard('Payees.Read')],
    loadChildren: () =>
      import('./features/payees/payees.routes').then((m) => m.payeesRoutes),
  },
  {
    path: 'transactions',
    canActivate: [planGuard, hasPermissionGuard('Transactions.Read')],
    loadChildren: () =>
      import('./features/transactions/transactions.routes').then((m) => m.transactionsRoutes),
  },
  {
    path: 'credits',
    canActivate: [planGuard, hasPermissionGuard('Credits.Read')],
    loadChildren: () =>
      import('./features/credits/credits.routes').then((m) => m.creditsRoutes),
  },
  {
    path: 'pay-runs',
    canActivate: [planGuard, hasPermissionGuard('Payouts.Read')],
    loadChildren: () =>
      import('./features/pay-runs/pay-runs.routes').then((m) => m.payRunsRoutes),
  },
  {
    path: 'payouts',
    canActivate: [planGuard, hasPermissionGuard('Payouts.Read')],
    loadChildren: () =>
      import('./features/payouts/payouts.routes').then((m) => m.payoutsRoutes),
  },
  {
    path: 'quotas',
    canActivate: [planGuard, hasPermissionGuard('Quotas.Read')],
    loadChildren: () =>
      import('./features/quotas/quotas.routes').then((m) => m.quotasRoutes),
  },
  {
    path: 'assignments',
    canActivate: [planGuard, hasPermissionGuard('Assignments.Read')],
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
    path: 'admin',
    canActivate: [planGuard, hasPermissionGuard('Subscription.Manage')],
    loadComponent: () =>
      import('./features/admin/admin.component').then(
        (m) => m.AdminComponent
      ),
  },
  {
    path: 'forbidden',
    canActivate: [planGuard],
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
