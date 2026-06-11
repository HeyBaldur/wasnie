import { Routes } from '@angular/router';
import { onboardingGuard } from '../../core/guards/onboarding.guard';
import { authGuard } from '../../core/guards/auth.guard';

export const subscriptionRoutes: Routes = [
  {
    path: 'plan',
    canActivate: [onboardingGuard],
    loadComponent: () =>
      import('./wizard/subscription-wizard.component').then(
        (m) => m.SubscriptionWizardComponent
      ),
  },
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

export const manageSubscriptionRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./manage/manage-subscription.component').then(
        (m) => m.ManageSubscriptionComponent
      ),
  },
];
