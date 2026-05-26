import { Component, HostListener, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { AssignmentsStore } from '../state/assignments.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { AssignmentStatus } from '../models/assignment.model';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
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

  readonly deactivateOpen = signal(false);
  readonly deactivateSaving = signal(false);
  readonly pendingDeactivateId = signal<string | null>(null);

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
