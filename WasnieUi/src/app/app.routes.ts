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
    title: 'AUTH.LOGIN',
    loadChildren: () =>
      import('./features/auth/auth.routes').then((m) => m.authRoutes),
  },
  {
    path: 'onboarding',
    title: 'NAV.ONBOARDING',
    loadChildren: () =>
      import('./features/subscription/subscription.routes').then((m) => m.subscriptionRoutes),
  },
  {
    path: 'dashboard',
    title: 'NAV.DASHBOARD',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(
        (m) => m.DashboardComponent
      ),
  },
  {
    path: 'plans',
    title: 'NAV.PLANS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Plans.Read')],
    loadChildren: () =>
      import('./features/plans/plans.routes').then((m) => m.plansRoutes),
  },
  {
    path: 'payees',
    title: 'NAV.PAYEES',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Payees.Read')],
    loadChildren: () =>
      import('./features/payees/payees.routes').then((m) => m.payeesRoutes),
  },
  {
    path: 'transactions',
    title: 'NAV.TRANSACTIONS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Transactions.Read')],
    loadChildren: () =>
      import('./features/transactions/transactions.routes').then((m) => m.transactionsRoutes),
  },
  {
    path: 'credits',
    title: 'NAV.CREDITS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Credits.Read')],
    loadChildren: () =>
      import('./features/credits/credits.routes').then((m) => m.creditsRoutes),
  },
  {
    path: 'pay-runs',
    title: 'NAV.PAY_RUNS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Payouts.Read')],
    loadChildren: () =>
      import('./features/pay-runs/pay-runs.routes').then((m) => m.payRunsRoutes),
  },
  {
    path: 'payouts',
    title: 'NAV.PAYOUTS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Payouts.Read')],
    loadChildren: () =>
      import('./features/payouts/payouts.routes').then((m) => m.payoutsRoutes),
  },
  {
    path: 'quotas',
    title: 'NAV.QUOTAS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Quotas.Read')],
    loadChildren: () =>
      import('./features/quotas/quotas.routes').then((m) => m.quotasRoutes),
  },
  {
    path: 'assignments',
    title: 'NAV.ASSIGNMENTS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Assignments.Read')],
    loadChildren: () =>
      import('./features/assignments/assignments.routes').then((m) => m.assignmentsRoutes),
  },
  {
    path: 'category-mappings',
    title: 'NAV.CATEGORY_MAPPINGS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('CategoryMappings.Read')],
    loadChildren: () =>
      import('./features/category-mappings/category-mappings.routes').then((m) => m.categoryMappingsRoutes),
  },
  {
    path: 'subscription',
    title: 'NAV.SUBSCRIPTION',
    canActivate: [planGuard, hasPermissionGuard('Subscription.Manage')],
    loadChildren: () =>
      import('./features/subscription/subscription.routes').then((m) => m.manageSubscriptionRoutes),
  },
  {
    path: 'profile',
    title: 'NAV.PROFILE',
    canActivate: [planGuard, subscriptionGuard],
    loadChildren: () =>
      import('./features/profile/profile.routes').then(m => m.profileRoutes),
  },
  {
    path: 'profile/confirm-email-change',
    title: 'NAV.PROFILE',
    loadComponent: () =>
      import('./features/profile/confirm-email-change/confirm-email-change.component').then(
        m => m.ConfirmEmailChangeComponent,
      ),
  },
  {
    path: 'admin',
    title: 'NAV.ADMIN',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Subscription.Manage')],
    loadComponent: () =>
      import('./features/admin/admin.component').then(
        (m) => m.AdminComponent
      ),
  },
  {
    path: 'integrations',
    title: 'NAV.INTEGRATIONS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Integrations.Manage')],
    loadComponent: () =>
      import('./features/integrations/integrations.component').then(
        (m) => m.IntegrationsComponent
      ),
  },
  {
    path: 'integrations/hubspot/owners',
    title: 'INTEGRATIONS.HUBSPOT.OWNERS.TITLE',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Integrations.Manage')],
    loadComponent: () =>
      import('./features/integrations/owner-mapping/owner-mapping.component').then(
        (m) => m.CrmOwnerMappingComponent
      ),
  },
  {
    path: 'forbidden',
    title: 'ERRORS.FORBIDDEN_TITLE',
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
