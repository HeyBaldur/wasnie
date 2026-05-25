import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
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
    path: 'dashboard',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(
        (m) => m.DashboardComponent
      ),
  },
  {
    path: 'plans',
    canActivate: [authGuard],
    loadChildren: () =>
      import('./features/plans/plans.routes').then((m) => m.plansRoutes),
  },
  {
    path: 'payees',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/payees/payees.component').then(
        (m) => m.PayeesComponent
      ),
  },
  {
    path: 'transactions',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/transactions/transactions.component').then(
        (m) => m.TransactionsComponent
      ),
  },
  {
    path: 'payouts',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/payouts/payouts.component').then(
        (m) => m.PayoutsComponent
      ),
  },
  {
    path: 'quotas',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/quotas/quotas.component').then(
        (m) => m.QuotasComponent
      ),
  },
  {
    path: 'assignments',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/assignments/assignments.component').then(
        (m) => m.AssignmentsComponent
      ),
  },
  {
    path: 'admin',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/admin/admin.component').then(
        (m) => m.AdminComponent
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
