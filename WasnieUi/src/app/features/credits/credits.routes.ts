import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const creditsRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/credits-list.component').then((m) => m.CreditsListComponent),
  },
  {
    path: ':id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./detail/credit-detail.component').then((m) => m.CreditDetailComponent),
  },
];
