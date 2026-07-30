import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { TerminatedAccountsStore } from '../state/terminated-accounts.store';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { WsPageLayoutComponent } from '../../../shared/ui/ws-page-layout/ws-page-layout.component';
import { WsTableComponent } from '../../../shared/ui/ws-table/ws-table.component';
import { WsTableEmptyComponent } from '../../../shared/ui/ws-table/ws-table-empty.component';
import { WsButtonComponent } from '../../../shared/ui/ws-button/ws-button.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';

/**
 * The accounts nobody is processing any more: payees who have LEFT with a balance that is not zero.
 *
 * This screen is the other half of the termination circuit breaker. Taking a departed payee out of
 * every future pay run is right — they will earn nothing more — but on its own it would also make
 * their debt invisible, and invisible debt is how debt quietly disappears. Finance needs to see
 * exactly these people and close each account deliberately.
 *
 * Nothing here computes money: the balance arrives finished from the server and the row links to the
 * payee's ledger, where the closing entry is written through the existing adjustment flow.
 */
@Component({
  selector: 'app-terminated-accounts',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    TranslateModule,
    AppShellComponent,
    WsPageLayoutComponent,
    WsTableComponent,
    WsTableEmptyComponent,
    WsButtonComponent,
    IconComponent,
    HasPermissionPipe,
  ],
  templateUrl: './terminated-accounts.component.html',
  styleUrl: './terminated-accounts.component.scss',
})
export class TerminatedAccountsComponent {
  /** Shared with the dashboard card that counts the same queue — one fetch, one definition. */
  private readonly store = inject(TerminatedAccountsStore);

  readonly rows = this.store.rows;
  readonly loading = this.store.loading;
  readonly error = this.store.error;
  readonly count = this.store.count;

  constructor() {
    void this.load();
  }

  load(): Promise<void> {
    return this.store.load();
  }

  /** Signed display, formatting only — the number itself is the server's. */
  fmtSigned(amount: number, currency: string): string {
    const formatted = Math.abs(amount).toLocaleString(undefined, {
      style: 'currency',
      currency,
      minimumFractionDigits: 2,
    });
    return amount < 0 ? `−${formatted}` : `+${formatted}`;
  }

  isDebt(amount: number): boolean {
    return amount < 0;
  }
}
