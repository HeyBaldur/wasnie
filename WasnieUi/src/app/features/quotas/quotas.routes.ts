import { Routes } from '@angular/router';

export const quotasRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./list/quotas-list.component').then((m) => m.QuotasListComponent),
  },
  {
    path: 'new',
    loadComponent: () =>
      import('./create/quota-create.component').then((m) => m.QuotaCreateComponent),
  },
  {
    path: ':quotaId',
    loadComponent: () =>
      import('./detail/quota-detail.component').then((m) => m.QuotaDetailComponent),
  },
];
