import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';
import { hasPermissionGuard } from '../../core/auth/guards/has-permission.guard';

/**
 * KAN-93. THE WRITING ROUTES CARRY THEIR OWN PERMISSION, not just `authGuard`.
 *
 * ★★ THE PARENT GUARD IS NOT ENOUGH, AND A REP PROVED IT. `app.routes.ts` gates this whole branch on
 * `Payees.Read`, which every role used to hold — so `/payees/new` was reachable by anybody signed in,
 * and the creation form rendered in full before the server refused the save. A form somebody may fill
 * in and never submit is worse than no form: it spends their time and then tells them no.
 *
 * ★ READ AND WRITE ARE DIFFERENT DOORS. A Manager holds `Payees.Read` and not `Payees.Create`: they
 * belong on the list and the detail page, and nowhere near `new` or the import wizard. One guard on
 * the parent cannot express that, because it only knows one key.
 */
export const payeesRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/payees-list.component').then((m) => m.PayeesListComponent),
  },
  {
    path: 'import',
    canActivate: [authGuard, hasPermissionGuard('Imports.Execute')],
    loadComponent: () =>
      import('../imports/payees/payee-import-wizard.component').then((m) => m.PayeeImportWizardComponent),
  },
  {
    path: 'new',
    canActivate: [authGuard, hasPermissionGuard('Payees.Create')],
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
    // The same component as the detail route; the editing controls inside it are already gated by
    // `*hasPermission`, so the guard here only stops the URL being a way in of its own.
    path: ':payeeId/edit',
    canActivate: [authGuard, hasPermissionGuard('Payees.Update')],
    loadComponent: () =>
      import('./detail/payee-detail.component').then((m) => m.PayeeDetailComponent),
  },
];
