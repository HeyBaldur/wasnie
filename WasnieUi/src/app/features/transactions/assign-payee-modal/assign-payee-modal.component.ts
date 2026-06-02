import { Component, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { PayeesApiService } from '../../payees/services/payees.api.service';
import { TransactionsStore } from '../state/transactions.store';
import { Transaction } from '../models/transaction.model';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import {
  WsModalComponent,
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  type SelectOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-assign-payee-modal',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    WsModalComponent,
    WsButtonComponent,
    WsInputComponent,
    WsSelectComponent,
    CurrencyFormatPipe,
  ],
  templateUrl: './assign-payee-modal.component.html',
  styleUrl: './assign-payee-modal.component.scss',
})
export class AssignPayeeModalComponent {
  private readonly fb = inject(FormBuilder);
  private readonly payeesApi = inject(PayeesApiService);
  private readonly store = inject(TransactionsStore);
  private readonly toast = inject(ToastService);

  readonly transaction = input.required<Transaction>();
  readonly isOpen = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly saving = signal(false);

  readonly form = this.fb.group({
    payeeId: ['', Validators.required],
    comment: [''],
  });

  readonly payeeSearchFn = (query: string): Observable<SelectOption[]> =>
    this.payeesApi.getPayees({ page: 1, pageSize: 20, search: query || undefined }).pipe(
      map(r => r.items.map(p => ({ value: p.id, label: `${p.fullName} (${p.employeeCode})` })))
    );

  async onSubmit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    try {
      await this.store.assignPayee(this.transaction().id, {
        payeeId: this.form.value.payeeId!,
        comment: this.form.value.comment || null,
      });
      this.toast.show('TRANSACTIONS.TOAST_ASSIGNED', 'success');
      this.form.reset();
      this.saved.emit();
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.saving.set(false);
    }
  }

  onClose(): void {
    this.form.reset();
    this.closed.emit();
  }
}
