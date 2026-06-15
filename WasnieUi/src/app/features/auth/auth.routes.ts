import { Routes } from '@angular/router';
import { noAuthGuard } from '../../core/guards/no-auth.guard';

export const authRoutes: Routes = [
  {
    path: '',
    redirectTo: 'login',
    pathMatch: 'full',
  },
  {
    path: 'login',
    canActivate: [noAuthGuard],
    loadComponent: () =>
      import('./login/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'register',
    canActivate: [noAuthGuard],
    loadComponent: () =>
      import('./register-tenant/register-tenant.component').then(
        (m) => m.RegisterTenantComponent
      ),
  },
  {
    path: 'confirm-email-pending',
    loadComponent: () =>
      import('./confirm-email-pending/confirm-email-pending.component').then(
        (m) => m.ConfirmEmailPendingComponent
      ),
  },
  {
    path: 'confirm-email',
    loadComponent: () =>
      import('./confirm-email/confirm-email.component').then(
        (m) => m.ConfirmEmailComponent
      ),
  },
];
