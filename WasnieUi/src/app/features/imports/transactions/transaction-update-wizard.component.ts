import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { WsPageLayoutComponent, WsWizardComponent, WsWizardStepComponent } from '../../../shared/ui';
import { TxUpdateUploadStepComponent } from './steps/update-upload-step.component';
import { TxUpdateMappingStepComponent } from './steps/update-mapping-step.component';
import { TxUpdatePreviewStepComponent } from './steps/update-preview-step.component';
import { TxUpdateProgressStepComponent } from './steps/update-progress-step.component';
import { TxUpdateCompleteStepComponent } from './steps/update-complete-step.component';
import {
  TransactionUpdateColumnMapping,
  TransactionUpdateValidateResponse,
  TransactionUpdateResult,
} from './models/transaction-update.models';
import { ParseResponse } from './models/transaction-import.models';

type WizardStep = 'upload' | 'map' | 'preview' | 'progress' | 'complete';

@Component({
  selector: 'app-transaction-update-wizard',
  standalone: true,
  imports: [
    AppShellComponent,
    TranslateModule,
    WsPageLayoutComponent,
    WsWizardComponent,
    WsWizardStepComponent,
    TxUpdateUploadStepComponent,
    TxUpdateMappingStepComponent,
    TxUpdatePreviewStepComponent,
    TxUpdateProgressStepComponent,
    TxUpdateCompleteStepComponent,
  ],
  template: `
    <app-shell>
      <ws-page-layout
        icon="refresh"
        [title]="'IMPORTS.UPDATE.TITLE' | translate"
        [subtitle]="'IMPORTS.UPDATE.SUBTITLE' | translate"
      >
        <ws-wizard [currentStep]="currentStep()">
          <ws-wizard-step name="upload" [title]="'IMPORTS.STEPS.UPLOAD' | translate">
            @if (currentStep() === 'upload') {
              <app-tx-update-upload-step (parsed)="onParsed($event)" />
            }
          </ws-wizard-step>

          <ws-wizard-step name="map" [title]="'IMPORTS.STEPS.MAP' | translate">
            @if (currentStep() === 'map' && parseResult()) {
              <app-tx-update-mapping-step
                [parseResult]="parseResult()!"
                (validated)="onValidated($event)"
                (back)="onBackToUpload()"
              />
            }
          </ws-wizard-step>

          <ws-wizard-step name="preview" [title]="'IMPORTS.STEPS.PREVIEW' | translate">
            @if (currentStep() === 'preview' && parseResult() && columnMapping() && validateResponse()) {
              <app-tx-update-preview-step
                [fileId]="parseResult()!.fileId"
                [columnMapping]="columnMapping()!"
                [validateResponse]="validateResponse()!"
                (executed)="onExecuted($event)"
                (back)="onBackToMap()"
              />
            }
          </ws-wizard-step>

          <ws-wizard-step name="progress" [title]="'IMPORTS.STEPS.PROGRESS' | translate">
            @if (currentStep() === 'progress' && jobId()) {
              <app-tx-update-progress-step
                [jobId]="jobId()!"
                (completed)="onCompleted($event)"
                (retryRequested)="onRetryFromPreview()"
              />
            }
          </ws-wizard-step>

          <ws-wizard-step name="complete" [title]="'IMPORTS.STEPS.COMPLETE' | translate">
            @if (currentStep() === 'complete') {
              <app-tx-update-complete-step
                [result]="updateResult()"
                (done)="onDone()"
              />
            }
          </ws-wizard-step>
        </ws-wizard>
      </ws-page-layout>
    </app-shell>
  `,
})
export class TransactionUpdateWizardComponent {
  private readonly router = inject(Router);

  readonly currentStep = signal<WizardStep>('upload');
  readonly parseResult = signal<(ParseResponse & { fileName: string; fileSize: number }) | null>(null);
  readonly columnMapping = signal<TransactionUpdateColumnMapping | null>(null);
  readonly validateResponse = signal<TransactionUpdateValidateResponse | null>(null);
  readonly jobId = signal<string | null>(null);
  readonly updateResult = signal<TransactionUpdateResult | null>(null);

  onParsed(result: ParseResponse & { fileName: string; fileSize: number }): void {
    this.parseResult.set(result);
    this.currentStep.set('map');
  }

  onValidated(event: { response: TransactionUpdateValidateResponse; mapping: TransactionUpdateColumnMapping }): void {
    this.columnMapping.set(event.mapping);
    this.validateResponse.set(event.response);
    this.currentStep.set('preview');
  }

  onExecuted(jobId: string): void {
    this.jobId.set(jobId);
    this.currentStep.set('progress');
  }

  onCompleted(result: TransactionUpdateResult): void {
    this.updateResult.set(result);
    this.currentStep.set('complete');
  }

  onBackToUpload(): void {
    this.currentStep.set('upload');
    this.parseResult.set(null);
  }

  onBackToMap(): void {
    this.currentStep.set('map');
    this.validateResponse.set(null);
  }

  onRetryFromPreview(): void {
    this.currentStep.set('preview');
    this.jobId.set(null);
  }

  onDone(): void {
    void this.router.navigate(['/transactions']);
  }
}
