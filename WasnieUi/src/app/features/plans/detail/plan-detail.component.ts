import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { extractApiError } from '../../../shared/utils/api-error';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PlansStore } from '../state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { ModalService } from '../../../shared/modals/modal.service';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { Rule, MeasurementType, RateTableType } from '../models/rule.model';
import { getPlanPermissions } from '../services/plan-permissions';
import {
  WsPageHeaderComponent,
  WsBadgeComponent,
  WsButtonComponent,
  WsTableComponent,
  WsEmptyStateComponent,
  WsTooltipDirective,
  type BadgeVariant,
} from '../../../shared/ui';

type Tab = 'rules' | 'versions' | 'assignments';

@Component({
  selector: 'app-plan-detail',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    TranslateModule,
    DateFormatPipe,
    WsPageHeaderComponent,
    WsBadgeComponent,
    WsButtonComponent,
    WsTableComponent,
    WsEmptyStateComponent,
    WsTooltipDirective,
  ],
  templateUrl: './plan-detail.component.html',
  styleUrl: './plan-detail.component.scss',
})
export class PlanDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly modal = inject(ModalService);

  readonly activeTab = signal<Tab>('rules');
  readonly planId = this.route.snapshot.paramMap.get('planId')!;
  readonly idCopied = signal(false);

  readonly MeasurementType = MeasurementType;
  readonly RateTableType = RateTableType;

  readonly permissions = computed(() => getPlanPermissions(this.store.selectedPlan()?.status));

  readonly sortedRules = computed(() => {
    const plan = this.store.selectedPlan();
    if (!plan) return [];
    return [...plan.rules].sort((a, b) => a.sortOrder - b.sortOrder);
  });

  ngOnInit(): void {
    this.store.loadPlan(this.planId).then(() => {
      const name = this.store.selectedPlan()?.name;
      if (name) this.store.loadVersions(name);
    });
  }

  setTab(tab: Tab): void {
    this.activeTab.set(tab);
  }

  statusVariant(status: string): BadgeVariant {
    switch (status.toLowerCase()) {
      case 'active': return 'success';
      case 'draft': return 'neutral';
      case 'archived': return 'neutral';
      default: return 'neutral';
    }
  }

  goToNewRule(): void {
    this.router.navigate(['rules', 'new'], { relativeTo: this.route });
  }

  copyPlanId(id: string): void {
    navigator.clipboard.writeText(id).then(() => {
      this.idCopied.set(true);
      setTimeout(() => this.idCopied.set(false), 2000);
    });
  }

  async onActivate(): Promise<void> {
    const confirmed = await this.modal.confirm({
      title: 'PLANS.CONFIRM_ACTIVATE_TITLE',
      message: 'PLANS.CONFIRM_ACTIVATE_MSG',
      confirmLabel: 'PLANS.ACTION_ACTIVATE',
      cancelLabel: 'COMMON.CANCEL',
      variant: 'default',
    });
    if (!confirmed) return;
    try {
      await this.store.activatePlan(this.planId);
      this.toast.show('PLANS.TOAST_ACTIVATED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  async onArchive(): Promise<void> {
    const confirmed = await this.modal.confirm({
      title: 'PLANS.CONFIRM_ARCHIVE_TITLE',
      message: 'PLANS.CONFIRM_ARCHIVE_MSG',
      confirmLabel: 'PLANS.ACTION_ARCHIVE',
      cancelLabel: 'COMMON.CANCEL',
      variant: 'danger',
    });
    if (!confirmed) return;
    try {
      await this.store.archivePlan(this.planId);
      this.toast.show('PLANS.TOAST_ARCHIVED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  async onClone(): Promise<void> {
    try {
      const newPlan = await this.store.clonePlan(this.planId);
      this.toast.show('PLANS.TOAST_CLONED', 'success');
      this.router.navigate(['/plans', newPlan.id]);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  async onDeleteRule(rule: Rule): Promise<void> {
    const confirmed = await this.modal.confirm({
      title: 'PLANS.CONFIRM_DELETE_RULE_TITLE',
      message: 'PLANS.CONFIRM_DELETE_RULE_MSG',
      confirmLabel: 'COMMON.DELETE',
      cancelLabel: 'COMMON.CANCEL',
      variant: 'danger',
    });
    if (!confirmed) return;
    try {
      await this.store.deleteRule(this.planId, rule.id);
      this.toast.show('PLANS.TOAST_RULE_DELETED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  measurementLabel(type: MeasurementType): string {
    return `PLANS.MEASUREMENT_${MeasurementType[type].toUpperCase()}`;
  }

  rateTableLabel(type: RateTableType): string {
    return `PLANS.RATE_TABLE_${RateTableType[type].toUpperCase()}`;
  }
}
