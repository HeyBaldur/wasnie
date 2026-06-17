import { Component, HostListener, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
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

  readonly openMenuId = signal<string | null>(null);
  readonly menuPosition = signal<{ top: number; right: number } | null>(null);

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

  ngOnInit(): void {
    this.store.loadAssignments();
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
    event.stopPropagation();
    const isOpening = this.openMenuId() !== id;
    this.openMenuId.update((cur) => (cur === id ? null : id));
    if (isOpening) {
      const btn = event.currentTarget as HTMLElement;
      const rect = btn.getBoundingClientRect();
      this.menuPosition.set({ top: rect.bottom + 4, right: window.innerWidth - rect.right });
    } else {
      this.menuPosition.set(null);
    }
  }

  closeMenu(): void {
    this.openMenuId.set(null);
    this.menuPosition.set(null);
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
