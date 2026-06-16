import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { OverlapWarningComponent } from '../../../shared/components/overlap-warning/overlap-warning.component';
import { PayoutsApiService } from '../services/payouts.api.service';
import { OverlappingPayout, PaymentConflictItem, PayoutDetail, LineCalculationDto, RateTableDto } from '../models/payout.model';
import { OverlapRow } from '../../../shared/models/overlap-row.model';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsModalComponent,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-payout-detail',
  standalone: true,
  imports: [
    AppShellComponent, RouterLink, TranslateModule,
    IconComponent, DateFormatPipe, CurrencyFormatPipe, HasPermissionDirective,
    OverlapWarningComponent,
    WsButtonComponent, WsBadgeComponent, WsCardComponent, WsPageLayoutComponent,
    WsTableComponent, WsModalComponent,
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
  readonly doublePayConflicts = signal<OverlapRow[]>([]);

  readonly approveOverlaps = signal<OverlappingPayout[]>([]);
  readonly markPaidOverlaps = signal<OverlappingPayout[]>([]);
  readonly overlapsLoading = signal(false);

  readonly approveOverlapRows = computed<OverlapRow[]>(() =>
    this.approveOverlaps().map(p => this._toRow(p))
  );

  readonly markPaidOverlapRows = computed<OverlapRow[]>(() =>
    this.markPaidOverlaps().map(p => this._toRow(p))
  );

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

  async openApproveConfirm(): Promise<void> {
    this.approveOverlaps.set([]);
    this.approveConfirmOpen.set(true);
    this.overlapsLoading.set(true);
    try {
      const overlaps = await firstValueFrom(this.api.getOverlaps(this.payoutId));
      this.approveOverlaps.set(overlaps);
    } catch {
      // non-critical — modal is already open, overlap info unavailable
    } finally {
      this.overlapsLoading.set(false);
    }
  }

  async openMarkPaidConfirm(): Promise<void> {
    this.markPaidOverlaps.set([]);
    this.markPaidConfirmOpen.set(true);
    this.overlapsLoading.set(true);
    try {
      const overlaps = await firstValueFrom(this.api.getOverlaps(this.payoutId));
      this.markPaidOverlaps.set(overlaps);
    } catch {
      // non-critical — modal is already open, overlap info unavailable
    } finally {
      this.overlapsLoading.set(false);
    }
  }

  private _toRow(p: OverlappingPayout): OverlapRow {
    const variant: BadgeVariant = p.status === 'Paid' ? 'success' : 'brand';
    return {
      id: p.id,
      periodStart: p.periodStart,
      periodEnd: p.periodEnd,
      statusLabel: `PAYOUTS.STATUS_${p.status.toUpperCase()}`,
      statusVariant: variant,
      col3: p.planName,
      amounts: [{ amount: p.totalCommissionAmount, currency: p.totalCommissionCurrency }],
    };
  }

  viewOverlapPayout(id: string): void {
    window.open(`/payouts/${id}`, '_blank');
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
    this.doublePayConflicts.set([]);
    try {
      await firstValueFrom(this.api.markPaid(this.payoutId));
      await this._load();
    } catch (err) {
      const httpErr = err as { status?: number; error?: { blocked?: boolean; conflicts?: PaymentConflictItem[]; message?: string } };
      if (httpErr.status === 409 && httpErr.error?.blocked && httpErr.error.conflicts?.length) {
        this.doublePayConflicts.set(this._toConflictRows(httpErr.error.conflicts));
      } else {
        this.actionError.set(httpErr.error?.message ?? 'PAYOUTS.DETAIL.MARK_PAID_ERROR');
      }
    } finally {
      this.markingPaid.set(false);
    }
  }

  private _toConflictRows(conflicts: PaymentConflictItem[]): OverlapRow[] {
    return conflicts.map(c => ({
      id: c.paidInPayoutId,
      periodStart: c.paidInPayoutPeriodStart,
      periodEnd: c.paidInPayoutPeriodEnd,
      statusLabel: 'PAYOUTS.STATUS_PAID',
      statusVariant: 'success' as BadgeVariant,
      col3: c.transactionReference,
      amounts: [],
    }));
  }

  openPlan(planId: string): void {
    window.open(`/plans/${planId}`, '_blank');
  }

  openTransaction(referenceNumber: string): void {
    window.open(`/transactions?ref=${encodeURIComponent(referenceNumber)}`, '_blank');
  }

  // ── Calculation expand/collapse ──────────────────────────────────────────

  readonly expandedLines = signal(new Set<string>());

  toggleLine(lineId: string): void {
    this.expandedLines.update(s => {
      const next = new Set(s);
      if (next.has(lineId)) next.delete(lineId); else next.add(lineId);
      return next;
    });
  }

  isExpanded(lineId: string): boolean {
    return this.expandedLines().has(lineId);
  }

  rateLabel(rt: RateTableDto): string {
    if (rt.type === 'Flat' && rt.flatRate != null) {
      return `${(rt.flatRate * 100).toFixed(2).replace(/\.?0+$/, '')}% flat`;
    }
    if (rt.type === 'Tiered' && rt.tiers?.length) {
      return rt.tiers.map(t =>
        `${t.to != null ? t.from + '–' + t.to : t.from + '+'}@${(t.rate * 100).toFixed(2).replace(/\.?0+$/, '')}%`
      ).join(' / ');
    }
    if (rt.type === 'AttainmentBased' && rt.attainmentTiers?.length) {
      return rt.attainmentTiers.map(t =>
        `${t.attainmentTo != null ? (t.attainmentFrom * 100).toFixed(0) + '–' + (t.attainmentTo * 100).toFixed(0) + '%' : (t.attainmentFrom * 100).toFixed(0) + '%+'} @ ${(t.rate * 100).toFixed(2).replace(/\.?0+$/, '')}%`
      ).join(' / ');
    }
    return rt.type;
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
