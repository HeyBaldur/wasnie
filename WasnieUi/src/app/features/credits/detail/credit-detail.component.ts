import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { formatRate } from '../../../shared/utils/rate-format';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { CreditsApiService } from '../services/credits.api.service';
import { CreditDetail } from '../models/credit.model';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsPageLayoutComponent,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-credit-detail',
  standalone: true,
  imports: [
    AppShellComponent, CommonModule, RouterLink, TranslateModule,
    IconComponent, DateFormatPipe, CurrencyFormatPipe,
    WsButtonComponent, WsBadgeComponent, WsCardComponent, WsPageLayoutComponent,
  ],
  templateUrl: './credit-detail.component.html',
  styleUrl: './credit-detail.component.scss',
})
export class CreditDetailComponent implements OnInit {
  private readonly translate = inject(TranslateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(CreditsApiService);

  readonly credit = signal<CreditDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly snapshotExpanded = signal(false);

  readonly creditId = this.route.snapshot.paramMap.get('id')!;

  readonly rateDisplay = computed(() => {
    const c = this.credit();
    if (!c) return '';
    if (c.rateTableType === 'Flat' && c.flatRate !== null) {
      // ★★ THIS SCREEN SHOWED A FALSE EQUATION, not just a mislabelled rate. It renders
      // `base × rate = result`, so a per-unit rule read "€78,500.00 × 500% = €5.00" — arithmetic the
      // reader can see does not hold. The result was always the server's figure and was always right;
      // the operation around it was nonsense. See shared/utils/rate-format.
      return formatRate(
        c.flatRate, c.measurementBase, c.originalCurrency, this.translate.currentLang,
        this.translate.instant('PLANS.RATE_PER_UNIT_SUFFIX'));
    }
    return c.rateTableType;
  });

  readonly calculation = computed(() => {
    const c = this.credit();
    if (!c || c.rateTableType !== 'Flat' || c.flatRate === null) return null;
    return {
      base: c.originalAmount,
      currency: c.originalCurrency,
      rate: c.flatRate,
      result: c.creditedAmount,
    };
  });

  async ngOnInit(): Promise<void> {
    try {
      const data = await firstValueFrom(this.api.getById(this.creditId));
      this.credit.set(data);
    } catch {
      this.error.set('CREDITS.DETAIL.ERROR_LOAD');
    } finally {
      this.loading.set(false);
    }
  }

  openTransaction(): void {
    const c = this.credit();
    if (!c) return;
    // Deep-link to the transaction detail (WI-UI-TRACE) rather than the filtered list.
    window.open(`/transactions/${c.transactionId}`, '_blank', 'noopener');
  }

  openPlan(): void {
    const c = this.credit();
    if (!c) return;
    window.open(`/plans/${c.planId}`, '_blank', 'noopener');
  }

  openRule(): void {
    const c = this.credit();
    if (!c) return;
    window.open(`/plans/${c.planId}/rules/${c.ruleId}`, '_blank', 'noopener');
  }

  statusBadge(isSuperseded: boolean): BadgeVariant {
    return isSuperseded ? 'neutral' : 'success';
  }

  planStatusBadge(status: string): BadgeVariant {
    if (status === 'Active') return 'success';
    if (status === 'Draft') return 'warning';
    return 'neutral';
  }

  goBack(): void { this.router.navigateByUrl('/credits'); }
}
