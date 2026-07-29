import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { LedgerApiService } from '../services/ledger.api.service';
import { TerminatedPayeeBalance } from '../models/ledger.model';
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
  private readonly api = inject(LedgerApiService);

  readonly rows = signal<TerminatedPayeeBalance[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly count = computed(() => this.rows().length);

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.rows.set(await firstValueFrom(this.api.getTerminatedWithBalance()));
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
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
