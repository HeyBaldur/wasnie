import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const payeesRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/payees-list.component').then((m) => m.PayeesListComponent),
  },
  {
    path: 'import',
    canActivate: [authGuard],
    loadComponent: () =>
      import('../imports/payees/payee-import-wizard.component').then((m) => m.PayeeImportWizardComponent),
  },
  {
    path: 'new',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./create/payee-create.component').then((m) => m.PayeeCreateComponent),
  },
  {
    path: ':payeeId',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./detail/payee-detail.component').then((m) => m.PayeeDetailComponent),
  },
  {
    path: ':payeeId/edit',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./detail/payee-detail.component').then((m) => m.PayeeDetailComponent),
  },
];
