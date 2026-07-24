import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { TransactionsApiService } from '../services/transactions.api.service';
import { CreditsApiService } from '../../credits/services/credits.api.service';
import { Transaction, TransactionStatus } from '../models/transaction.model';
import { CreditListItem } from '../../credits/models/credit.model';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsPageLayoutComponent,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-transaction-detail',
  standalone: true,
  imports: [
    AppShellComponent, CommonModule, RouterLink, TranslateModule,
    IconComponent, DateFormatPipe, CurrencyFormatPipe,
    WsButtonComponent, WsBadgeComponent, WsCardComponent, WsPageLayoutComponent,
  ],
  templateUrl: './transaction-detail.component.html',
  styleUrl: './transaction-detail.component.scss',
})
export class TransactionDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(TransactionsApiService);
  private readonly creditsApi = inject(CreditsApiService);

  readonly transaction = signal<Transaction | null>(null);
  readonly credits = signal<CreditListItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly transactionId = this.route.snapshot.paramMap.get('id')!;

  async ngOnInit(): Promise<void> {
    try {
      const tx = await firstValueFrom(this.api.getById(this.transactionId));
      this.transaction.set(tx);
      // Reuse the credits list's Reference filter to fetch this transaction's credits, then narrow to
      // this exact transaction (the server filter is a substring match; transactionId makes it exact).
      const page = await firstValueFrom(this.creditsApi.list({
        page: 1,
        pageSize: 100,
        filters: { reference: tx.referenceNumber, status: 'all' },
      }));
      this.credits.set((page.items ?? []).filter(c => c.transactionId === tx.id));
    } catch {
      this.error.set('TRANSACTIONS.DETAIL.ERROR_LOAD');
    } finally {
      this.loading.set(false);
    }
  }

  // Mirrors the transactions list variants exactly (single source of truth for status colours).
  statusVariant(status: TransactionStatus): BadgeVariant {
    switch (status) {
      case TransactionStatus.Pending: return 'warning';
      case TransactionStatus.Eligible: return 'info';
      case TransactionStatus.Calculated: return 'brand';
      case TransactionStatus.Paid: return 'success';
      case TransactionStatus.Cancelled: return 'neutral';
      default: return 'neutral';
    }
  }

  statusKey(status: TransactionStatus): string {
    return `TRANSACTIONS.STATUS_${status.toUpperCase()}`;
  }

  goBack(): void { this.router.navigateByUrl('/transactions'); }
}
