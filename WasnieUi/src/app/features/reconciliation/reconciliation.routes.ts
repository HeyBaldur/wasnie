import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const reconciliationRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/reconciliation-list.component').then((m) => m.ReconciliationListComponent),
  },
];
