import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DashboardService } from '../services/dashboard.service';
import { DashboardSummary } from '../models/dashboard.models';

@Injectable({ providedIn: 'root' })
export class DashboardStore {
  private readonly api = inject(DashboardService);

  readonly period = signal<string>('this-month');
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly summary = signal<DashboardSummary | null>(null);

  readonly actionBand = computed(() => this.summary()?.actionBand ?? null);
  readonly periodBand = computed(() => this.summary()?.periodBand ?? null);
  readonly trendBand = computed(() => this.summary()?.trendBand ?? null);
  readonly activityFeed = computed(() => this.summary()?.activityFeed ?? []);

  readonly hasPendingActions = computed(() => {
    const b = this.actionBand();
    if (!b) return false;
    return (
      b.draftPayRunsCount > 0 ||
      b.payoutsPendingApprovalCount > 0 ||
      b.payoutsApprovedUnpaidByCurrency.length > 0 ||
      b.pendingByPlanItems.length > 0
    );
  });

  constructor() {
    effect(() => {
      const p = this.period();
      void this._load(p);
    });
  }

  setPeriod(period: string): void {
    this.period.set(period);
  }

  async reload(): Promise<void> {
    await this._load(this.period());
  }

  private async _load(period: string): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const data = await firstValueFrom(this.api.getSummary(period));
      this.summary.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }
}
