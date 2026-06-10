import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const payRunsRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/pay-runs-list.component').then((m) => m.PayRunsListComponent),
  },
  {
    path: ':id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./detail/pay-run-detail.component').then((m) => m.PayRunDetailComponent),
  },
];
