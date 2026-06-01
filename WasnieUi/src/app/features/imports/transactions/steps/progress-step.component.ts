import { Component, DestroyRef, inject, input, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { timer, Subscription, of } from 'rxjs';
import { switchMap, catchError } from 'rxjs/operators';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { ImportProgressComponent } from '../../shared/import-progress.component';
import { TransactionImportService } from '../services/transaction-import.service';
import { ImportJobStatus, TransactionImportResult } from '../models/transaction-import.models';

@Component({
  selector: 'app-tx-progress-step',
  standalone: true,
  imports: [TranslateModule, ImportProgressComponent],
  templateUrl: './progress-step.component.html',
  styleUrl: './progress-step.component.scss',
})
export class TxProgressStepComponent implements OnInit {
  private readonly importService = inject(TransactionImportService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly translate = inject(TranslateService);

  readonly jobId = input.required<string>();
  readonly completed = output<TransactionImportResult>();
  readonly retryRequested = output<void>();

  readonly status = signal<ImportJobStatus | null>(null);
  readonly netError = signal(false);
  readonly failureMessage = signal<string | null>(null);

  private _polling: Subscription | null = null;

  ngOnInit(): void {
    this._polling = timer(0, 3000).pipe(
      takeUntilDestroyed(this.destroyRef),
      switchMap(() =>
        this.importService.getJobStatus(this.jobId()).pipe(
          catchError(() => {
            this.netError.set(true);
            return of(null as ImportJobStatus | null);
          }),
        ),
      ),
    ).subscribe(s => {
      if (!s) return;
      this.netError.set(false);
      this.status.set(s);
      if (s.state === 'Succeeded') {
        this._polling?.unsubscribe();
        this.completed.emit({ totalRows: s.progressTotal, processedRows: s.progressCurrent });
      } else if (s.state === 'Failed') {
        this._polling?.unsubscribe();
        this.failureMessage.set(s.errorMessage);
      }
    });
  }

  get progressPct(): number {
    const s = this.status();
    if (!s || s.progressTotal === 0) return 0;
    return Math.round((s.progressCurrent / s.progressTotal) * 100);
  }

  get progressTitle(): string {
    return this.translate.instant('IMPORTS.TRANSACTIONS.PROGRESS_TITLE');
  }

  get progressSubtitle(): string {
    const s = this.status();
    if (!s || s.state === 'Pending') {
      return this.translate.instant('IMPORTS.TRANSACTIONS.PROGRESS_QUEUED');
    }
    return this.translate.instant('IMPORTS.TRANSACTIONS.PROGRESS_RUNNING', {
      processed: s.progressCurrent,
      total: s.progressTotal,
    });
  }

  get determinateProgress(): number | null {
    const s = this.status();
    if (!s || s.state === 'Pending') return null; // indeterminate while queued
    return this.progressPct;
  }

  onRetry(): void { this.retryRequested.emit(); }
}
