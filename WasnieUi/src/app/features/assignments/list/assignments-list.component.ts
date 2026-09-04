import { Component, DestroyRef, HostListener, OnInit, computed, inject, signal } from '@angular/core';
import { createRowMenu } from '../../../shared/utils/row-menu';
import { bindFiltersToUrl } from '../../../shared/state/bind-filters-to-url';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { OverlapWarningComponent } from '../../../shared/components/overlap-warning/overlap-warning.component';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { AssignmentsStore } from '../state/assignments.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { AssignmentStatus, BlockedAssignmentDto } from '../models/assignment.model';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { OverlapRow } from '../../../shared/models/overlap-row.model';
import {
  WsButtonComponent,
  WsInputComponent,
  WsBadgeComponent,
  WsSegmentedControlComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsEmptyStateComponent,
  WsConfirmationModalComponent,
  WsPaginationComponent,
  type SegOption,
  type BadgeVariant,
} from '../../../shared/ui';

@Component({
  selector: 'app-assignments-list',
  standalone: true,
  imports: [
    AppShellComponent,
    RefreshOnEnterDirective,
    IconComponent,
    RouterLink,
    TranslateModule,
    DateFormatPipe,
    HasPermissionDirective,
    HasPermissionPipe,
    OverlapWarningComponent,
    WsButtonComponent,
    WsInputComponent,
    WsBadgeComponent,
    WsSegmentedControlComponent,
    WsPageLayoutComponent,
    WsTableComponent,
    WsTableEmptyComponent,
    WsEmptyStateComponent,
    WsConfirmationModalComponent,
    WsPaginationComponent,
  ],
  templateUrl: './assignments-list.component.html',
  styleUrl: './assignments-list.component.scss',
})
export class AssignmentsListComponent implements OnInit {
  readonly store = inject(AssignmentsStore);
  private readonly toast = inject(ToastService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);

  // ★ The "⋯" menu follows its row while the page scrolls — see RowMenuController. These four
  // list screens each carried an identical measure-once copy, and so the identical defect.
  private readonly rowMenu = createRowMenu();
  readonly openMenuId = this.rowMenu.openMenuId;
  readonly menuPosition = this.rowMenu.menuPosition;

  // ── Single activate ────────────────────────────────────────────────────────
  readonly activateOpen = signal(false);
  readonly activateSaving = signal(false);
  readonly pendingActivateId = signal<string | null>(null);

  // ── Single deactivate ──────────────────────────────────────────────────────
  readonly deactivateOpen = signal(false);
  readonly deactivateSaving = signal(false);
  readonly pendingDeactivateId = signal<string | null>(null);

  // ── Bulk actions ───────────────────────────────────────────────────────────
  readonly bulkActivateOpen = signal(false);
  readonly bulkDeactivateOpen = signal(false);
  readonly bulkDeleteOpen = signal(false);
  readonly bulkSaving = signal(false);

  // ── Blocked-delete modal ───────────────────────────────────────────────────
  readonly blockedOpen = signal(false);
  readonly blockedRows = signal<OverlapRow[]>([]);

  readonly statusOptions: SegOption[] = [
    { value: '', label: 'ASSIGNMENTS.FILTER_ALL' },
    { value: 'Active', label: 'ASSIGNMENTS.STATUS_ACTIVE' },
    { value: 'Deactivated', label: 'ASSIGNMENTS.STATUS_DEACTIVATED' },
  ];

  get statusFilter(): string {
    return this.store.listParams().status ?? '';
  }

  set statusFilter(value: string) {
    this.onStatusFilter(value === '' ? null : (value as AssignmentStatus));
  }

  /**
   * Label for the active payee filter. Read off the loaded rows (they already carry the payee's name
   * and code) so no extra request is needed just to render the chip. Null while loading or when the
   * filter matched nothing — the chip then falls back to a generic label so it stays dismissible.
   */
  readonly payeeFilterLabel = computed(() => {
    if (!this.store.payeeId()) return null;
    const first = this.store.assignments()[0];
    return first ? `${first.payeeFullName} (${first.payeeEmployeeCode})` : null;
  });

  ngOnInit(): void {
    // Deep-link from a payee's Assignments card ("View all") arrives pre-filtered. SUBSCRIBE, don't
    // snapshot — see bindFiltersToUrl. Loop-safe only because clearPayeeFilter was converted from
    // router.navigate to history.replaceState in this same change.
    bindFiltersToUrl(this.route, this.destroyRef, {
      apply: qp => this.store.loadFromQueryParams(qp),
      // Default = no deep-link filters. NOT a full clear: `search` is the user's own typing and is
      // not carried in the URL, so the URL must not wipe it.
      reset: () => this.store.clearUrlFilters(),
    });
    // First load handled by the store's constructor effect; re-entry refresh by [refreshOnEnter].
  }

  /**
   * Drops the payee filter and strips it from the URL so a refresh doesn't bring it back.
   *
   * Writes with `history.replaceState`, NOT `router.navigate`. This screen now subscribes to
   * `queryParams`, and the router observes its own navigations: a navigate here would echo straight
   * back into that subscription, letting the URL re-assert itself as the authority mid-interaction
   * and wipe state the URL does not carry — the debounced `search` the user had typed. replaceState
   * is invisible to the router, so the write cannot re-trigger the read. Same convention as
   * Transactions, Credits and Payouts.
   */
  clearPayeeFilter(): void {
    this.store.clearPayeeFilter();
    const qp = new URLSearchParams(window.location.search);
    qp.delete('payeeId');
    const suffix = qp.toString() ? '?' + qp.toString() : '';
    window.history.replaceState(null, '', window.location.pathname + suffix);
  }

  onSearch(value: string): void {
    this.store.setSearch(value);
  }

  onStatusFilter(status: AssignmentStatus | null): void {
    this.store.setStatus(status);
  }

  goToPage(page: number): void {
    this.store.setPage(page);
  }

  goToPageSize(size: number): void {
    this.store.setPageSize(size);
  }

  toggleMenu(id: string, event: Event): void {
    this.rowMenu.toggle(id, event);
  }

  closeMenu(): void {
    this.rowMenu.close();
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    this.closeMenu();
  }

  // ── Single activate ────────────────────────────────────────────────────────

  onActivate(assignmentId: string): void {
    this.closeMenu();
    this.pendingActivateId.set(assignmentId);
    this.activateOpen.set(true);
  }

  async onConfirmActivate(): Promise<void> {
    const id = this.pendingActivateId();
    if (!id) return;
    this.activateSaving.set(true);
    try {
      await this.store.activateAssignment(id);
      this.toast.show('ASSIGNMENTS.TOAST_ACTIVATED', 'success');
      this.activateOpen.set(false);
      this.pendingActivateId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.activateSaving.set(false);
    }
  }

  // ── Single deactivate ──────────────────────────────────────────────────────

  onDeactivate(assignmentId: string): void {
    this.closeMenu();
    this.pendingDeactivateId.set(assignmentId);
    this.deactivateOpen.set(true);
  }

  async onConfirmDeactivate(): Promise<void> {
    const id = this.pendingDeactivateId();
    if (!id) return;
    this.deactivateSaving.set(true);
    try {
      await this.store.deactivateAssignment(id);
      this.toast.show('ASSIGNMENTS.TOAST_DEACTIVATED', 'success');
      this.deactivateOpen.set(false);
      this.pendingDeactivateId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.deactivateSaving.set(false);
    }
  }

  // ── Bulk activate ──────────────────────────────────────────────────────────

  async onConfirmBulkActivate(): Promise<void> {
    const ids = [...this.store.selectedIds()];
    if (!ids.length) return;
    this.bulkSaving.set(true);
    try {
      await this.store.bulkActivate(ids);
      this.toast.show('ASSIGNMENTS.TOAST_BULK_ACTIVATED', 'success');
      this.bulkActivateOpen.set(false);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.bulkSaving.set(false);
    }
  }

  // ── Bulk deactivate ────────────────────────────────────────────────────────

  async onConfirmBulkDeactivate(): Promise<void> {
    const ids = [...this.store.selectedIds()];
    if (!ids.length) return;
    this.bulkSaving.set(true);
    try {
      await this.store.bulkDeactivate(ids);
      this.toast.show('ASSIGNMENTS.TOAST_BULK_DEACTIVATED', 'success');
      this.bulkDeactivateOpen.set(false);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.bulkSaving.set(false);
    }
  }

  // ── Bulk delete ────────────────────────────────────────────────────────────

  async onConfirmBulkDelete(): Promise<void> {
    const ids = [...this.store.selectedIds()];
    if (!ids.length) return;
    this.bulkSaving.set(true);
    try {
      const result = await this.store.bulkDelete(ids);
      if (result.allDeleted) {
        this.toast.show('ASSIGNMENTS.TOAST_BULK_DELETED', 'success');
        this.bulkDeleteOpen.set(false);
      } else {
        // All-or-nothing blocked: show the table of blocked items
        this.bulkDeleteOpen.set(false);
        this.blockedRows.set(this.toBlockedRows(result.blocked));
        this.blockedOpen.set(true);
      }
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.bulkSaving.set(false);
    }
  }

  private toBlockedRows(blocked: BlockedAssignmentDto[]): OverlapRow[] {
    return blocked.map(b => ({
      id: b.assignmentId,
      periodStart: b.effectiveStart,
      periodEnd: b.effectiveEnd,
      statusLabel: b.reason,
      statusVariant: 'danger' as BadgeVariant,
      col3: `${b.payeeName} → ${b.planName}`,
      amounts: [],
    }));
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  statusVariant(status: AssignmentStatus): BadgeVariant {
    switch (status) {
      case 'Active': return 'success';
      case 'Deactivated': return 'neutral';
    }
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }

  initialsFor(name: string): string {
    return name.split(' ').slice(0, 2).map((w) => w[0]).join('').toUpperCase();
  }
}
