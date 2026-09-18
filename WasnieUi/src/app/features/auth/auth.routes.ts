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
    path: 'forgot-password',
    // Without this the page inherits the parent `auth` route's title and the tab reads
    // "Sign in | Incentra" on the recovery screen.
    title: 'FORGOT_PASSWORD.TITLE',
    canActivate: [noAuthGuard],
    loadComponent: () =>
      import('./forgot-password/forgot-password.component').then(
        (m) => m.ForgotPasswordComponent
      ),
  },
  {
    // KAN-93. The only way back in for somebody who forgot their Organization identifier. Its own
    // title for the same reason `forgot-password` has one: without it the tab reads "Sign in".
    path: 'forgot-organization',
    title: 'FORGOT_ORGANIZATION.TITLE',
    canActivate: [noAuthGuard],
    loadComponent: () =>
      import('./forgot-organization/forgot-organization.component').then(
        (m) => m.ForgotOrganizationComponent
      ),
  },
  {
    // KAN-32. NO noAuthGuard: somebody already signed in to another tenant may legitimately be
    // holding an invitation for this one, and bouncing them to the dashboard would strand the link
    // with no way to act on it.
    path: 'accept-invitation',
    title: 'ACCEPT_INVITATION.TITLE_PLAIN',
    loadComponent: () =>
      import('./accept-invitation/accept-invitation.component').then(
        (m) => m.AcceptInvitationComponent
      ),
  },
  {
    path: 'reset-password',
    loadComponent: () =>
      import('./reset-password/reset-password.component').then(
        (m) => m.ResetPasswordComponent
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
  {
    path: 'verify-2fa',
    loadComponent: () =>
      import('./verify-two-factor/verify-two-factor.component').then(
        (m) => m.VerifyTwoFactorComponent
      ),
  },
];
