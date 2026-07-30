import { Routes } from '@angular/router';
import { TerminatedAccountsComponent } from './terminated/terminated-accounts.component';

export const ledgerRoutes: Routes = [
  {
    // Empty path: the parent route already owns the '/terminated-accounts' segment.
    path: '',
    title: 'LEDGER.TERMINATED_TITLE',
    component: TerminatedAccountsComponent,
  },
];
