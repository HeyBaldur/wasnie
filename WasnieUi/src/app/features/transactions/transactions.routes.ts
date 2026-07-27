import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';
import { hasPermissionGuard } from '../../core/auth/guards/has-permission.guard';

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
  {
    path: 'import',
    canActivate: [authGuard, hasPermissionGuard('Transactions.Create')],
    loadComponent: () =>
      import('../imports/transactions/transaction-import-wizard.component').then((m) => m.TransactionImportWizardComponent),
  },
  {
    path: ':id',
    canActivate: [authGuard, hasPermissionGuard('Transactions.Read')],
    loadComponent: () =>
      import('./detail/transaction-detail.component').then((m) => m.TransactionDetailComponent),
  },
];
