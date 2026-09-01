import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { bindFiltersToUrl } from '../../../shared/state/bind-filters-to-url';
import { firstValueFrom } from 'rxjs';
import { extractApiError } from '../../../shared/utils/api-error';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PlansStore } from '../state/plans.store';
import { PlansApiService, MultiPlanPayees } from '../services/plans.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import { SubscriptionStateService } from '../../subscription/services/subscription-state.service';
import { TierLimitModalService } from '../../../shared/components/tier-limit-modal/tier-limit-modal.service';
import { TIER_LIMITS } from '../../../shared/services/tier-limits';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import {
  Rule,
  MeasurementType,
  RateTableType,
} from '../models/rule.model';
import { getPlanPermissions } from '../services/plan-permissions';
import { PlanClawbackPolicyComponent } from '../clawback/plan-clawback-policy.component';
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
  WsCopyButtonComponent,
  WsPaginationComponent,
  type BadgeVariant,
} from '../../../shared/ui';

type Tab = 'rules' | 'versions' | 'assignments' | 'clawback';

@Component({
  selector: 'app-plan-detail',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    PlanClawbackPolicyComponent,
    RouterLink,
    TranslateModule,
    DateFormatPipe,
    WsPageLayoutComponent,
    WsBadgeComponent,
    WsButtonComponent,
    WsTableComponent,
    WsEmptyStateComponent,
    WsConfirmationModalComponent,
    WsCopyButtonComponent,
    WsPaginationComponent,
    ProcessPendingComponent,
    HasPermissionDirective,
  ],
  templateUrl: './plan-detail.component.html',
  styleUrl: './plan-detail.component.scss',
})
export class PlanDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);
  private readonly plansApi = inject(PlansApiService);
  private readonly subState = inject(SubscriptionStateService);
  private readonly tierLimitModal = inject(TierLimitModalService);

  private get plansTierLimit(): number {
    const tier = this.subState.subscription()?.tier ?? 'Free';
    return TIER_LIMITS[tier]?.maxPlans ?? -1;
  }

  readonly atPlansLimit = computed(() => {
    const max = this.plansTierLimit;
    return max !== -1 && this.store.unfilteredTotal() >= max;
  });

  readonly activeTab = signal<Tab>('rules');
  readonly planId = this.route.snapshot.paramMap.get('planId')!;
  readonly planAssignmentsResult = signal<PagedResult<Assignment> | null>(null);
  readonly assignmentsLoading = signal(false);
  readonly assignmentsPage = signal(1);

  // Payees of this plan that are ALSO in another active plan (informational banner).
  readonly multiPlanPayees = signal<MultiPlanPayees | null>(null);
  readonly multiPlanDetailOpen = signal(false);
  readonly multiPlanCount = computed(() => this.multiPlanPayees()?.count ?? 0);

  readonly activateOpen = signal(false);
  readonly activateSaving = signal(false);
  readonly archiveOpen = signal(false);
  readonly archiveSaving = signal(false);

  // Archiving deactivates every active assignment of the plan, so the confirmation has to name
  // how many people that is. Two explicit keys picked by a ternary, never a key built from a
  // value: at zero, "0 assignments" is noise rather than information.
  readonly archiveAssignmentCount = computed(() => this.store.selectedPlan()?.activeAssignmentCount ?? 0);
  readonly archiveMessageKey = computed(() =>
    this.archiveAssignmentCount() > 0 ? 'PLANS.CONFIRM_ARCHIVE_MSG' : 'PLANS.CONFIRM_ARCHIVE_MSG_NONE');

  readonly deleteRuleOpen = signal(false);
  readonly deleteRuleSaving = signal(false);
  readonly pendingRule = signal<Rule | null>(null);

  readonly permissions = computed(() => getPlanPermissions(this.store.selectedPlan()?.status));

  /** Re-reads the plan after the clawback policy is saved, so the tab shows the stored state. */
  reloadPlan(): void {
    void this.store.loadPlan(this.planId);
  }

  readonly sortedRules = computed(() => {
    const plan = this.store.selectedPlan();
    if (!plan) return [];
    // Only show active rules — a deleted (soft-deactivated) rule must not appear, otherwise
    // its Edit action opens a form whose save fails with "rule not found in this plan".
    return [...plan.rules].filter((r) => r.isActive).sort((a, b) => a.sortOrder - b.sortOrder);
  });

  ngOnInit(): void {
    // SUBSCRIBE, don't snapshot. Angular reuses this component when a navigation changes only the
    // query params, so a snapshot read runs once and a later ?tab= change would leave the previous
    // tab on screen. Only acts on a REAL change: setTab lazily loads the assignments tab, so
    // re-applying the tab already showing would refetch for nothing.
    bindFiltersToUrl(this.route, this.destroyRef, {
      apply: qp => {
        const urlTab = qp['tab'] as Tab | undefined;
        const tab: Tab = urlTab && (['rules', 'versions', 'assignments'] as Tab[]).includes(urlTab)
          ? urlTab : 'rules';
        if (tab !== this.activeTab()) this.setTab(tab);
      },
      // No ?tab= means the default tab, not whatever the last visit left behind.
      reset: () => { if (this.activeTab() !== 'rules') this.setTab('rules'); },
    });
    this.store.loadPlan(this.planId).then(() => {
      const name = this.store.selectedPlan()?.name;
      if (name) this.store.loadVersions(name);
    });
  }

  setTab(tab: Tab): void {
    this.activeTab.set(tab);
    if (tab === 'assignments' && !this.planAssignmentsResult()) {
      this.loadPlanAssignments(1);
      this.loadMultiPlanPayees();
    }
  }

  private async loadMultiPlanPayees(): Promise<void> {
    try {
      const data = await firstValueFrom(this.plansApi.getMultiPlanPayees(this.planId));
      this.multiPlanPayees.set(data);
    } catch {
      // non-critical — the banner just stays hidden.
    }
  }

  toggleMultiPlanDetail(): void {
    this.multiPlanDetailOpen.update((v) => !v);
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
    if (this.atPlansLimit()) {
      const tier = this.subState.subscription()?.tier ?? 'Free';
      this.tierLimitModal.show({
        tier,
        currentCount: this.store.unfilteredTotal(),
        limit: this.plansTierLimit,
        entityKey: 'plans',
      });
      return;
    }
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
