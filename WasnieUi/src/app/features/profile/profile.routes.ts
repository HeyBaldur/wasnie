import { Routes } from '@angular/router';

export const profileRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./manage/manage-profile.component').then(m => m.ManageProfileComponent),
  },
  {
    path: 'confirm-email-change',
    loadComponent: () =>
      import('./confirm-email-change/confirm-email-change.component').then(
        m => m.ConfirmEmailChangeComponent,
      ),
  },
];
