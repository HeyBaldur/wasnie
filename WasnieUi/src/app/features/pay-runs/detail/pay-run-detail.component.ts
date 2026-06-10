import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { PayRunDetailStore } from '../state/pay-run-detail.store';
import { PayRunsApiService } from '../services/pay-runs.api.service';
import { PayRunStatus } from '../models/pay-run.model';
import { PayoutStatus } from '../../payouts/models/payout.model';
import {
  WsButtonComponent, WsBadgeComponent, WsCardComponent, WsPageLayoutComponent,
  WsTableComponent, WsTableEmptyComponent, WsPaginationComponent, WsModalComponent,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-pay-run-detail',
  standalone: true,
  providers: [PayRunDetailStore],
  imports: [
    AppShellComponent, RouterLink, TranslateModule,
    IconComponent, DateFormatPipe, CurrencyFormatPipe, HasPermissionDirective,
    WsButtonComponent, WsBadgeComponent, WsCardComponent, WsPageLayoutComponent,
    WsTableComponent, WsTableEmptyComponent, WsPaginationComponent, WsModalComponent,
  ],
  templateUrl: './pay-run-detail.component.html',
  styleUrl: './pay-run-detail.component.scss',
})
export class PayRunDetailComponent implements OnInit {
  readonly store = inject(PayRunDetailStore);
  private readonly api = inject(PayRunsApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly runId = this.route.snapshot.paramMap.get('id')!;

  readonly approveConfirmOpen = signal(false);
  readonly markPaidConfirmOpen = signal(false);
  readonly reopenConfirmOpen = signal(false);
  readonly actioning = signal(false);
  readonly actionError = signal<string | null>(null);

  ngOnInit(): void {
    void this.store.load(this.runId);
  }

  runStatusBadge(status: PayRunStatus): BadgeVariant {
    switch (status) {
      case 'Draft':    return 'neutral';
      case 'Approved': return 'brand';
      case 'Paid':     return 'success';
    }
  }

  payoutStatusBadge(status: PayoutStatus): BadgeVariant {
    switch (status) {
      case 'Calculated': return 'neutral';
      case 'Approved':   return 'brand';
      case 'Paid':       return 'success';
      case 'Disputed':   return 'danger';
    }
  }

  async onApprove(): Promise<void> {
    if (this.actioning()) return;
    this.approveConfirmOpen.set(false);
    this.actioning.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(this.api.approve(this.runId));
      await this.store.reload();
    } catch {
      this.actionError.set('PAY_RUNS.DETAIL.APPROVE_ERROR');
    } finally {
      this.actioning.set(false);
    }
  }

  async onMarkPaid(): Promise<void> {
    if (this.actioning()) return;
    this.markPaidConfirmOpen.set(false);
    this.actioning.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(this.api.markPaid(this.runId));
      await this.store.reload();
    } catch {
      this.actionError.set('PAY_RUNS.DETAIL.MARK_PAID_ERROR');
    } finally {
      this.actioning.set(false);
    }
  }

  async onReopen(): Promise<void> {
    if (this.actioning()) return;
    this.reopenConfirmOpen.set(false);
    this.actioning.set(true);
    this.actionError.set(null);
    try {
      await firstValueFrom(this.api.reopen(this.runId));
      await this.store.reload();
    } catch {
      this.actionError.set('PAY_RUNS.DETAIL.REOPEN_ERROR');
    } finally {
      this.actioning.set(false);
    }
  }

  viewPayout(id: string): void {
    window.open(`/payouts/${id}`, '_blank');
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 6 }, (_, i) => i);
  }
}
