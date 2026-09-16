import { Routes } from '@angular/router';
import { qualificationGuard } from '../../core/guards/qualification.guard';
import { authGuard } from '../../core/guards/auth.guard';

export const subscriptionRoutes: Routes = [
  {
    path: 'qualify',
    canActivate: [qualificationGuard],
    loadComponent: () =>
      import('../onboarding/qualification/qualification.component').then(
        (m) => m.QualificationComponent
      ),
  },
  // KAN-77: the plan wizard (with its free plan and comparison table) is gone. New accounts start in a trial;
  // the address survives as a redirect for old links and e-mails.
  { path: 'plan', redirectTo: '/pricing', pathMatch: 'full' },
  {
    // Stripe redirects here after payment — accessible before plan activation
    path: 'success',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./success/subscription-success.component').then(
        (m) => m.SubscriptionSuccessComponent
      ),
  },
];

