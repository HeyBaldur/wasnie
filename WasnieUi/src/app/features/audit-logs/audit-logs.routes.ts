import { Routes } from '@angular/router';
import { authGuard } from '../../core/guards/auth.guard';

export const auditLogsRoutes: Routes = [
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./list/audit-log-list.component').then((m) => m.AuditLogListComponent),
  },
];
