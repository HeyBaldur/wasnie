import { Component, DestroyRef, inject, input, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { timer, Subscription, of } from 'rxjs';
import { switchMap, catchError } from 'rxjs/operators';
import { TranslateModule } from '@ngx-translate/core';
import { ImportProgressComponent } from '../../shared/import-progress.component';
import { TransactionsApiService } from '../../../../features/transactions/services/transactions.api.service';
import { TransactionUpdateResult } from '../models/transaction-update.models';

@Component({
  selector: 'app-tx-update-progress-step',
  standalone: true,
  imports: [TranslateModule, ImportProgressComponent],
  template: `
    <app-import-progress
      [title]="'IMPORTS.UPDATE.PROGRESS_TITLE' | translate"
      [subtitle]="progressSubtitle"
      [progress]="determinateProgress"
      [errorMessage]="failureMessage()"
      [netError]="netError()"
      (retry)="onRetry()"
    />
  `,
})
export class TxUpdateProgressStepComponent implements OnInit {
  private readonly txApi = inject(TransactionsApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly jobId = input.required<string>();
  readonly completed = output<TransactionUpdateResult>();
  readonly retryRequested = output<void>();

  readonly status = signal<{ state: string; progressCurrent: number; progressTotal: number; errorMessage: string | null; resultSummary: string | null } | null>(null);
  readonly netError = signal(false);
  readonly failureMessage = signal<string | null>(null);

  private _polling: Subscription | null = null;

  ngOnInit(): void {
    this._polling = timer(0, 3000).pipe(
      takeUntilDestroyed(this.destroyRef),
      switchMap(() =>
        this.txApi.getJobStatus(this.jobId()).pipe(
          catchError(() => {
            this.netError.set(true);
            return of(null as any);
          }),
        ),
      ),
    ).subscribe(s => {
      if (!s) return;
      this.netError.set(false);
      this.status.set(s);
      if (s.state === 'Succeeded') {
        this._polling?.unsubscribe();
        const raw = s.resultSummary ? JSON.parse(s.resultSummary) : null;
        const result: TransactionUpdateResult = raw
          ? {
              updated: raw.Updated ?? raw.updated ?? 0,
              skippedNoChanges: raw.SkippedNoChanges ?? raw.skippedNoChanges ?? 0,
              skippedErrors: raw.SkippedErrors ?? raw.skippedErrors ?? 0,
            }
          : { updated: s.progressCurrent, skippedNoChanges: 0, skippedErrors: 0 };
        this.completed.emit(result);
      } else if (s.state === 'Failed') {
        this._polling?.unsubscribe();
        this.failureMessage.set(s.errorMessage);
      }
    });
  }

  get determinateProgress(): number | null {
    const s = this.status();
    if (!s || s.state === 'Pending') return null;
    if (s.progressTotal === 0) return 0;
    return Math.round((s.progressCurrent / s.progressTotal) * 100);
  }

  get progressSubtitle(): string {
    const s = this.status();
    if (!s || s.state === 'Pending') return 'Queued…';
    return `Updating ${s.progressCurrent} of ${s.progressTotal} transactions…`;
  }

  onRetry(): void { this.retryRequested.emit(); }
}
