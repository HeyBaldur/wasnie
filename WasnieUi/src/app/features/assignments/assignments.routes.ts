import { Routes } from '@angular/router';

export const assignmentsRoutes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./list/assignments-list.component').then((m) => m.AssignmentsListComponent),
  },
  {
    path: 'new',
    loadComponent: () =>
      import('./create/assignment-create.component').then((m) => m.AssignmentCreateComponent),
  },
];
