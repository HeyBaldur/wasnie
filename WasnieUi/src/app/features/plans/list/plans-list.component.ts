import { Component, HostListener, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { extractApiError } from '../../../shared/utils/api-error';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';
import { PlansStore } from '../state/plans.store';
import { ToastService } from '../../../shared/services/toast.service';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { PlanStatus } from '../models/plan.model';
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
} from '../../../shared/ui';

@Component({
  selector: 'app-plans-list',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    TranslateModule,
    DateFormatPipe,
    HasPermissionDirective,
    HasPermissionPipe,
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
  templateUrl: './plans-list.component.html',
  styleUrl: './plans-list.component.scss',
})
export class PlansListComponent implements OnInit {
  readonly store = inject(PlansStore);
  private readonly toast = inject(ToastService);

  readonly openMenuId = signal<string | null>(null);
  readonly menuPosition = signal<{ top: number; right: number } | null>(null);

  readonly deleteOpen = signal(false);
  readonly deleteSaving = signal(false);
  readonly pendingDeleteId = signal<string | null>(null);

  readonly activateOpen = signal(false);
  readonly activateSaving = signal(false);
  readonly pendingActivateId = signal<string | null>(null);

  readonly archiveOpen = signal(false);
  readonly archiveSaving = signal(false);
  readonly pendingArchiveId = signal<string | null>(null);

  readonly statusOptions: SegOption[] = [
    { value: '', label: 'PLANS.FILTER_ALL' },
    { value: 'Draft', label: 'PLANS.STATUS_DRAFT' },
    { value: 'Active', label: 'PLANS.STATUS_ACTIVE' },
    { value: 'Archived', label: 'PLANS.STATUS_ARCHIVED' },
  ];

  get statusFilter(): string {
    return this.store.listParams().status ?? '';
  }

  set statusFilter(value: string) {
    this.onStatusFilter(value === '' ? null : value as PlanStatus);
  }

  ngOnInit(): void {
    this.store.loadPlans();
  }

  onSearch(value: string): void {
    this.store.setSearch(value);
  }

  onStatusFilter(status: PlanStatus | null): void {
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

  onDelete(planId: string): void {
    this.closeMenu();
    this.pendingDeleteId.set(planId);
    this.deleteOpen.set(true);
  }

  async onConfirmDelete(): Promise<void> {
    const id = this.pendingDeleteId();
    if (!id) return;
    this.deleteSaving.set(true);
    try {
      await this.store.deletePlan(id);
      this.toast.show('PLANS.TOAST_DELETED', 'success');
      this.deleteOpen.set(false);
      this.pendingDeleteId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.deleteSaving.set(false);
    }
  }

  async onClone(planId: string): Promise<void> {
    this.closeMenu();
    try {
      await this.store.clonePlan(planId);
      this.toast.show('PLANS.TOAST_CLONED', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  onActivate(planId: string): void {
    this.closeMenu();
    this.pendingActivateId.set(planId);
    this.activateOpen.set(true);
  }

  async onConfirmActivate(): Promise<void> {
    const id = this.pendingActivateId();
    if (!id) return;
    this.activateSaving.set(true);
    try {
      await this.store.activatePlan(id);
      this.toast.show('PLANS.TOAST_ACTIVATED', 'success');
      this.activateOpen.set(false);
      this.pendingActivateId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.activateSaving.set(false);
    }
  }

  onArchive(planId: string): void {
    this.closeMenu();
    this.pendingArchiveId.set(planId);
    this.archiveOpen.set(true);
  }

  async onConfirmArchive(): Promise<void> {
    const id = this.pendingArchiveId();
    if (!id) return;
    this.archiveSaving.set(true);
    try {
      await this.store.archivePlan(id);
      this.toast.show('PLANS.TOAST_ARCHIVED', 'success');
      this.archiveOpen.set(false);
      this.pendingArchiveId.set(null);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.archiveSaving.set(false);
    }
  }

  get skeletonRows(): number[] {
    return Array.from({ length: 8 }, (_, i) => i);
  }
}
