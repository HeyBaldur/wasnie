import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';
import { hasPermissionGuard } from '../../core/auth/guards/has-permission.guard';

/**
 * KAN-93 bug 6. THE PARENT ROUTE NOW ADMITS TWO KINDS OF READER, so the administration routes carry
 * their own permission.
 *
 * ★★ `Plans.Read` IS THE CATALOGUE, AND IT STAYS HERE. A rep holding only `Plans.ReadOwn` must reach
 * `:planId` — their own plan, to check the arithmetic on their commission — and must never reach the
 * LIST, which is every plan of every team. Leaving the list ungated would have turned a transparency
 * feature into a directory of the company's compensation design.
 *
 * ★★ BUT `rules/:ruleId` IS NOT ADMINISTRATION, AND GATING IT WAS A REAL DEFECT. That route is BOTH
 * the editor and the READER: the rules list renders "Edit" on a Draft and "View" on an Active plan,
 * and both navigate to the same URL — the component switches itself to `readOnly` from the plan's
 * status. Requiring `Plans.Read` there sent every rep to Access Denied the moment they tried to open
 * the one thing this whole feature exists to show them. `rules/new` really is administration and
 * keeps the permission.
 */
export const plansRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard, hasPermissionGuard('Plans.Read')],
    loadComponent: () =>
      import('./list/plans-list.component').then((m) => m.PlansListComponent),
  },
  {
    path: 'new',
    canActivate: [authGuard, hasPermissionGuard('Plans.Create')],
    loadComponent: () =>
      import('./create/plan-create.component').then((m) => m.PlanCreateComponent),
  },
  {
    // ★ NO PERMISSION BEYOND THE PARENT'S, AND THAT IS DELIBERATE. Whether THIS plan may be opened is
    // not something the browser can decide — it depends on the reader's own assignments, which only
    // the server knows. `PlanAccessGuard` answers it on every request; a client-side guess here would
    // either refuse a rep their own plan or pretend to protect one it cannot see.
    path: ':planId',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./detail/plan-detail.component').then((m) => m.PlanDetailComponent),
  },
  {
    path: ':planId/rules/new',
    canActivate: [authGuard, hasPermissionGuard('Plans.Read')],
    loadComponent: () =>
      import('./rule-form/rule-form.component').then((m) => m.RuleFormComponent),
  },
  {
    // Same reasoning as `:planId`: whether THIS plan's rule may be read depends on the reader's own
    // assignments, which only the server knows. The parent guard has already admitted both kinds of
    // reader; `PlanAccessGuard` decides on every request behind it.
    path: ':planId/rules/:ruleId',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./rule-form/rule-form.component').then((m) => m.RuleFormComponent),
  },
];
