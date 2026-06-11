import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { extractApiError } from '../../../shared/utils/api-error';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PlansStore } from '../state/plans.store';
import { PlansApiService } from '../services/plans.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import {
  Rule,
  MeasurementType,
  RateTableType,
} from '../models/rule.model';
import { getPlanPermissions } from '../services/plan-permissions';
import { Assignment } from '../../assignments/models/assignment.model';
import { PagedResult } from '../../../shared/models/pagination.models';
import { ProcessPendingComponent } from '../../transactions/process-pending/process-pending.component';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import {
  WsPageLayoutComponent,
  WsBadgeComponent,
  WsButtonComponent,
  WsTableComponent,
  WsEmptyStateComponent,
  WsConfirmationModalComponent,
  WsTooltipDirective,
  WsPaginationComponent,
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
    WsPageLayoutComponent,
    WsBadgeComponent,
    WsButtonComponent,
    WsTableComponent,
    WsEmptyStateComponent,
    WsConfirmationModalComponent,
    WsTooltipDirective,
    WsPaginationComponent,
    ProcessPendingComponent,
    HasPermissionDirective,
  ],
  templateUrl: './plan-detail.component.html',
  styleUrl: './plan-detail.component.scss',
})
export class PlanDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly plansApi = inject(PlansApiService);

  readonly activeTab = signal<Tab>('rules');
  readonly planId = this.route.snapshot.paramMap.get('planId')!;
  readonly idCopied = signal(false);
  readonly planAssignmentsResult = signal<PagedResult<Assignment> | null>(null);
  readonly assignmentsLoading = signal(false);
  readonly assignmentsPage = signal(1);

  readonly activateOpen = signal(false);
  readonly activateSaving = signal(false);
  readonly archiveOpen = signal(false);
  readonly archiveSaving = signal(false);
  readonly deleteRuleOpen = signal(false);
  readonly deleteRuleSaving = signal(false);
  readonly pendingRule = signal<Rule | null>(null);

  readonly permissions = computed(() => getPlanPermissions(this.store.selectedPlan()?.status));

  readonly sortedRules = computed(() => {
    const plan = this.store.selectedPlan();
    if (!plan) return [];
    return [...plan.rules].sort((a, b) => a.sortOrder - b.sortOrder);
  });

  ngOnInit(): void {
    const urlTab = this.route.snapshot.queryParams['tab'] as Tab | undefined;
    if (urlTab && (['rules', 'versions', 'assignments'] as Tab[]).includes(urlTab)) {
      this.setTab(urlTab);
    }
    this.store.loadPlan(this.planId).then(() => {
      const name = this.store.selectedPlan()?.name;
      if (name) this.store.loadVersions(name);
    });
  }

  setTab(tab: Tab): void {
    this.activeTab.set(tab);
    if (tab === 'assignments' && !this.planAssignmentsResult()) {
      this.loadPlanAssignments(1);
    }
  }

  async loadPlanAssignments(page: number): Promise<void> {
    this.assignmentsPage.set(page);
    this.assignmentsLoading.set(true);
    try {
      const data = await firstValueFrom(this.plansApi.getPlanAssignments(this.planId, { page, pageSize: 10, sortBy: 'effectivestart', sortOrder: 'desc' }));
      this.planAssignmentsResult.set(data);
    } catch {
      // non-critical — tab just shows empty
    } finally {
      this.assignmentsLoading.set(false);
    }
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

  onActivate(): void {
    this.activateOpen.set(true);
  }

  async onConfirmActivate(): Promise<void> {
    this.activateSaving.set(true);
    try {
      await this.store.activatePlan(this.planId);
      this.toast.show('PLANS.TOAST_ACTIVATED', 'success');
      this.activateOpen.set(false);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.activateSaving.set(false);
    }
  }

  onArchive(): void {
    this.archiveOpen.set(true);
  }

  async onConfirmArchive(): Promise<void> {
    this.archiveSaving.set(true);
    try {
      await this.store.archivePlan(this.planId);
      this.toast.show('PLANS.TOAST_ARCHIVED', 'success');
      this.archiveOpen.set(false);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.archiveSaving.set(false);
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

  onDeleteRule(rule: Rule): void {
    this.pendingRule.set(rule);
    this.deleteRuleOpen.set(true);
  }

  async onConfirmDeleteRule(): Promise<void> {
    const rule = this.pendingRule();
    if (!rule) return;
    this.deleteRuleSaving.set(true);
    try {
      await this.store.deleteRule(this.planId, rule.id);
      this.toast.show('PLANS.TOAST_RULE_DELETED', 'success');
      this.deleteRuleOpen.set(false);
      this.pendingRule.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.deleteRuleSaving.set(false);
    }
  }

  measurementLabel(type: MeasurementType): string {
    return `PLANS.MEASUREMENT_${String(type).toUpperCase()}`;
  }

  rateTableLabel(type: RateTableType): string {
    return `PLANS.RATE_TABLE_${String(type).toUpperCase()}`;
  }
}
