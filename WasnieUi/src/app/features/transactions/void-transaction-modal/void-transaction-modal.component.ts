import { Component, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { TransactionsStore } from '../state/transactions.store';
import { Transaction } from '../models/transaction.model';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { WsModalComponent, WsButtonComponent } from '../../../shared/ui';

@Component({
  selector: 'app-void-transaction-modal',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TranslateModule,
    WsModalComponent,
    WsButtonComponent,
    CurrencyFormatPipe,
  ],
  templateUrl: './void-transaction-modal.component.html',
  styleUrl: './void-transaction-modal.component.scss',
})
export class VoidTransactionModalComponent {
  private readonly fb = inject(FormBuilder);
  private readonly store = inject(TransactionsStore);
  private readonly toast = inject(ToastService);

  readonly transaction = input.required<Transaction>();
  readonly isOpen = input(false);
  readonly closed = output<void>();
  readonly saved = output<void>();

  readonly saving = signal(false);

  readonly form = this.fb.group({
    reason: ['', [Validators.required, Validators.minLength(3)]],
  });

  get reasonTouched(): boolean {
    return this.form.controls.reason.touched;
  }

  get reasonInvalid(): boolean {
    return this.form.controls.reason.invalid;
  }

  get reasonError(): string {
    const ctrl = this.form.controls.reason;
    if (!ctrl.touched || !ctrl.invalid) return '';
    if (ctrl.hasError('required')) return 'TRANSACTIONS.VOID_REASON_REQUIRED';
    if (ctrl.hasError('minlength')) return 'TRANSACTIONS.VOID_REASON_MIN_LENGTH';
    return '';
  }

  async onSubmit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    try {
      await this.store.voidTransaction(this.transaction().id, {
        reason: this.form.value.reason!.trim(),
      });
      this.toast.show('TRANSACTIONS.TOAST_VOIDED', 'success');
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
