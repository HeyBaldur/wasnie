import { Routes } from '@angular/router';
import { onboardingGuard } from '../../core/guards/onboarding.guard';
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
  {
    // Accessible to Canceled users — no subscriptionGuard here.
    path: 'reactivate',
    loadComponent: () =>
      import('./reactivation/subscription-reactivation.component').then(
        (m) => m.SubscriptionReactivationComponent
      ),
  },
];
