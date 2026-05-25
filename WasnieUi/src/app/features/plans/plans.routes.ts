import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const plansRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/plans-list.component').then((m) => m.PlansListComponent),
  },
  {
    path: 'new',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./create/plan-create.component').then((m) => m.PlanCreateComponent),
  },
  {
    path: ':planId',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./detail/plan-detail.component').then((m) => m.PlanDetailComponent),
  },
  {
    path: ':planId/rules/new',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./rule-form/rule-form.component').then((m) => m.RuleFormComponent),
  },
  {
    path: ':planId/rules/:ruleId',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./rule-form/rule-form.component').then((m) => m.RuleFormComponent),
  },
];
