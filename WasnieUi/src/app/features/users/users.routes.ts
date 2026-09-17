import { Routes } from '@angular/router';

/**
 * KAN-32. Guarded by Users.Read — a Manager or Rep reaching the URL directly gets the forbidden
 * page rather than an empty table that looks like a bug.
 */
export const usersRoutes: Routes = [
  {
    path: '',
    title: 'USERS.TITLE',
    loadComponent: () =>
      import('./list/users-list.component').then((m) => m.UsersListComponent),
  },
];
