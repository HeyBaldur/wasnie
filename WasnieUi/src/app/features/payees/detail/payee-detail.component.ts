import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { PayeesStore } from '../state/payees.store';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { PayeesApiService } from '../services/payees.api.service';
import { Assignment } from '../../assignments/models/assignment.model';
import { QuotaSummary } from '../../quotas/models/quota.model';
import { Payee, PayeeStatus } from '../models/payee.model';
import { PayeeFormComponent } from '../form/payee-form.component';
import { PagedResult } from '../../../shared/models/pagination.models';
import {
  WsPageLayoutComponent,
  WsBadgeComponent,
  WsButtonComponent,
  WsDatePickerComponent,
  WsModalComponent,
  WsConfirmationModalComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsPaginationComponent,
  type BadgeVariant,
} from '../../../shared/ui';

type Tab = 'profile' | 'assignments' | 'quotas' | 'activity';

@Component({
  selector: 'app-payee-detail',
  standalone: true,
  imports: [
    AppShellComponent,
    IconComponent,
    RouterLink,
    ReactiveFormsModule,
    TranslateModule,
    DateFormatPipe,
    CurrencyFormatPipe,
    PayeeFormComponent,
    WsPageLayoutComponent,
    WsBadgeComponent,
    WsButtonComponent,
    WsDatePickerComponent,
    WsModalComponent,
    WsConfirmationModalComponent,
    WsTableComponent,
    WsTableEmptyComponent,
    WsPaginationComponent,
  ],
  templateUrl: './payee-detail.component.html',
  styleUrl: './payee-detail.component.scss',
})
export class PayeeDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  private readonly payeesApi = inject(PayeesApiService);
  readonly store = inject(PayeesStore);
  private readonly toast = inject(ToastService);

  readonly PayeeStatus = PayeeStatus;

  readonly payeeId = this.route.snapshot.paramMap.get('payeeId')!;
  readonly activeTab = signal<Tab>('profile');
  readonly editModalOpen = signal(false);
  readonly terminateModalOpen = signal(false);
  readonly saving = signal(false);

  readonly payeeAssignmentsResult = signal<PagedResult<Assignment> | null>(null);
  readonly assignmentsLoading = signal(false);
  readonly assignmentsPage = signal(1);
  readonly payeeQuotasResult = signal<PagedResult<QuotaSummary> | null>(null);
  readonly quotasLoading = signal(false);
  readonly quotasPage = signal(1);

  readonly terminateForm = this.fb.nonNullable.group({
    terminationDate: ['', Validators.required],
  });

  ngOnInit(): void {
    this.store.loadPayee(this.payeeId);
  }

  setTab(tab: Tab): void {
    this.activeTab.set(tab);
    if (tab === 'assignments' && !this.assignmentsLoading() && !this.payeeAssignmentsResult()) {
      this.loadPayeeAssignments(1);
    }
    if (tab === 'quotas' && !this.quotasLoading() && !this.payeeQuotasResult()) {
      this.loadPayeeQuotas(1);
    }
  }

  async loadPayeeAssignments(page: number): Promise<void> {
    this.assignmentsPage.set(page);
    this.assignmentsLoading.set(true);
    try {
      const result = await firstValueFrom(this.payeesApi.getPayeeAssignments(this.payeeId, { page, pageSize: 10, sortBy: 'effectivestart', sortOrder: 'desc' }));
      this.payeeAssignmentsResult.set(result);
    } catch {
      // silent — tab will show empty state
    } finally {
      this.assignmentsLoading.set(false);
    }
  }

  async loadPayeeQuotas(page: number): Promise<void> {
    this.quotasPage.set(page);
    this.quotasLoading.set(true);
    try {
      const result = await firstValueFrom(this.payeesApi.getPayeeQuotas(this.payeeId, { page, pageSize: 10, sortBy: 'periodstart', sortOrder: 'desc' }));
      this.payeeQuotasResult.set(result);
    } catch {
      // silent — tab will show empty state
    } finally {
      this.quotasLoading.set(false);
    }
  }

  openEdit(): void {
    this.editModalOpen.set(true);
  }

  onEditSaved(_payee: Payee): void {
    this.editModalOpen.set(false);
  }

  async onMarkActive(): Promise<void> {
    try {
      await this.store.markAsActive(this.payeeId);
      this.toast.show('PAYEES.TOAST_MARKED_ACTIVE', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  async onMarkOnLeave(): Promise<void> {
    try {
      await this.store.markAsOnLeave(this.payeeId);
      this.toast.show('PAYEES.TOAST_MARKED_ON_LEAVE', 'success');
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    }
  }

  openTerminate(): void {
    const today = new Date();
    const yyyy = today.getFullYear();
    const mm = String(today.getMonth() + 1).padStart(2, '0');
    const dd = String(today.getDate()).padStart(2, '0');
    this.terminateForm.reset({ terminationDate: `${yyyy}-${mm}-${dd}` });
    this.terminateModalOpen.set(true);
  }

  async onConfirmTerminate(): Promise<void> {
    if (this.terminateForm.invalid) {
      this.terminateForm.markAllAsTouched();
      return;
    }
    const { terminationDate } = this.terminateForm.getRawValue();
    this.saving.set(true);
    try {
      await this.store.markAsTerminated(this.payeeId, terminationDate);
      this.toast.show('PAYEES.TOAST_TERMINATED', 'success');
      this.terminateModalOpen.set(false);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.saving.set(false);
    }
  }

  payeeStatusVariant(status: PayeeStatus): BadgeVariant {
    switch (status) {
      case PayeeStatus.Active: return 'success';
      case PayeeStatus.OnLeave: return 'warning';
      case PayeeStatus.Terminated: return 'neutral';
    }
  }

  payeeStatusKey(status: PayeeStatus): string {
    switch (status) {
      case PayeeStatus.Active: return 'PAYEES.STATUS_ACTIVE';
      case PayeeStatus.OnLeave: return 'PAYEES.STATUS_ON_LEAVE';
      case PayeeStatus.Terminated: return 'PAYEES.STATUS_TERMINATED';
    }
  }

  assignmentStatusVariant(status: string): BadgeVariant {
    return status === 'Active' ? 'success' : 'neutral';
  }

  quotaStatusVariant(status: string): BadgeVariant {
    switch (status) {
      case 'Active': return 'success';
      case 'Draft': return 'neutral';
      default: return 'warning';
    }
  }

  hasTerminateError(field: string, error: string): boolean {
    const ctrl = this.terminateForm.get(field);
    return !!(ctrl?.touched && ctrl.hasError(error));
  }
}
