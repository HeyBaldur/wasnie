import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const categoryMappingsRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/category-mappings-list.component').then((m) => m.CategoryMappingsListComponent),
  },
];
