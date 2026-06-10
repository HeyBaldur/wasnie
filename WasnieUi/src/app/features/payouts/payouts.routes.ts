import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const payoutsRoutes: Routes = [
  {
    // Flat payout list retired — redirect to the pay-runs entry point.
    path: '',
    redirectTo: '/pay-runs',
    pathMatch: 'full',
  },
  {
    path: ':id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./detail/payout-detail.component').then((m) => m.PayoutDetailComponent),
  },
];
