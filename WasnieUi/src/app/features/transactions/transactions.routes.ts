import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const transactionsRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/transactions-list.component').then((m) => m.TransactionsListComponent),
  },
  {
    path: 'new',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./create/transaction-create.component').then((m) => m.TransactionCreateComponent),
  },
];
