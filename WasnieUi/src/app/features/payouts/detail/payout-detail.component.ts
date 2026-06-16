import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { PayoutsApiService } from '../services/payouts.api.service';
import { PayoutDetail } from '../models/payout.model';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsConfirmationModalComponent,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-payout-detail',
  standalone: true,
  imports: [
    AppShellComponent, RouterLink, TranslateModule,
    IconComponent, DateFormatPipe, CurrencyFormatPipe, HasPermissionDirective,
    WsButtonComponent, WsBadgeComponent, WsCardComponent, WsPageLayoutComponent,
    WsTableComponent, WsConfirmationModalComponent,
  ],
  templateUrl: './payout-detail.component.html',
  styleUrl: './payout-detail.component.scss',
})
export class PayoutDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(PayoutsApiService);

  readonly payoutId = this.route.snapshot.paramMap.get('id')!;
  readonly payout = signal<PayoutDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly approveConfirmOpen = signal(false);
  readonly markPaidConfirmOpen = signal(false);
  readonly approving = signal(false);
  readonly markingPaid = signal(false);
  readonly exporting = signal(false);
  readonly actionError = signal<string | null>(null);

  readonly statusBadge = computed<BadgeVariant>(() => {
    switch (this.payout()?.status) {
      case 'Calculated': return 'neutral';
      case 'Approved': return 'brand';
      case 'Paid': return 'success';
      case 'Disputed': return 'danger';
      default: return 'neutral';
    }
  });

  readonly isCalculated = computed(() => this.payout()?.status === 'Calculated');
  readonly isApproved = computed(() => this.payout()?.status === 'Approved');
  readonly isPaid = computed(() => this.payout()?.status === 'Paid');
  readonly isDisputed = computed(() => this.payout()?.status === 'Disputed');
  readonly isReadOnly = computed(() => this.isPaid() || this.isDisputed());

  ngOnInit(): void {
    void this._load();
  }

  private async _load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const data = await firstValueFrom(this.api.getById(this.payoutId));
      this.payout.set(data);
    } catch {
      this.error.set('PAYOUTS.DETAIL.ERROR_LOAD');
    } finally {
      this.loading.set(false);
    }
  }

  async onApprove(): Promise<void> {
    if (this.approving()) return;
    this.approveConfirmOpen.set(false);
    this.approving.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(this.api.approve(this.payoutId));
      await this._load();
    } catch {
      this.actionError.set('PAYOUTS.DETAIL.APPROVE_ERROR');
    } finally {
      this.approving.set(false);
    }
  }

  async onMarkPaid(): Promise<void> {
    if (this.markingPaid()) return;
    this.markPaidConfirmOpen.set(false);
    this.markingPaid.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(this.api.markPaid(this.payoutId));
      await this._load();
    } catch {
      this.actionError.set('PAYOUTS.DETAIL.MARK_PAID_ERROR');
    } finally {
      this.markingPaid.set(false);
    }
  }

  openPlan(planId: string): void {
    window.open(`/plans/${planId}`, '_blank');
  }

  openTransaction(referenceNumber: string): void {
    window.open(`/transactions?ref=${encodeURIComponent(referenceNumber)}`, '_blank');
  }

  async onExportPdf(): Promise<void> {
    if (this.exporting()) return;
    this.exporting.set(true);
    this.actionError.set(null);
    try {
      const blob = await firstValueFrom(this.api.exportPdf(this.payoutId));
      const p = this.payout()!;
      const fileName = `payout-${p.payeeCode}-${p.periodStart}.pdf`;
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      this.actionError.set('PAYOUTS.DETAIL.EXPORT_ERROR');
    } finally {
      this.exporting.set(false);
    }
  }
}
