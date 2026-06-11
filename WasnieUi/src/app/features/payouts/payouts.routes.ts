import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const payoutsRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/payouts-list.component').then((m) => m.PayoutsListComponent),
  },
  {
    path: ':id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./detail/payout-detail.component').then((m) => m.PayoutDetailComponent),
  },
];
