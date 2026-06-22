import { Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { WsPageLayoutComponent, WsWizardComponent, WsWizardStepComponent, WsConfirmationModalComponent } from '../../../shared/ui';
import { TxUploadStepComponent } from './steps/upload-step.component';
import { TxMappingStepComponent } from './steps/mapping-step.component';
import { TxPreviewStepComponent } from './steps/preview-step.component';
import { TxProgressStepComponent } from './steps/progress-step.component';
import { TxCompleteStepComponent } from './steps/complete-step.component';
import { TxUpdateUploadStepComponent } from './steps/update-upload-step.component';
import { TxUpdateMappingStepComponent } from './steps/update-mapping-step.component';
import { TxUpdatePreviewStepComponent } from './steps/update-preview-step.component';
import { TxUpdateProgressStepComponent } from './steps/update-progress-step.component';
import { TxUpdateCompleteStepComponent } from './steps/update-complete-step.component';
import {
  TransactionImportColumnMapping,
  TransactionImportResult,
  ParseResponse,
  TransactionValidateResponse,
} from './models/transaction-import.models';
import {
  TransactionUpdateColumnMapping,
  TransactionUpdateValidateResponse,
  TransactionUpdateResult,
} from './models/transaction-update.models';

type WizardStep = 'upload' | 'map' | 'preview' | 'progress' | 'complete';
type WizardMode = 'create' | 'update';

const STORAGE_KEY_CREATE = 'wasnie:import-wizard:transactions';
const STORAGE_KEY_UPDATE = 'wasnie:update-wizard:transactions';

interface PersistedCreateState {
  step: WizardStep;
  parseResult: (ParseResponse & { fileName: string; fileSize: number }) | null;
  columnMapping: TransactionImportColumnMapping | null;
  validateResponse: TransactionValidateResponse | null;
}

@Component({
  selector: 'app-transaction-import-wizard',
  standalone: true,
  imports: [
    AppShellComponent,
    TranslateModule,
    WsPageLayoutComponent,
    WsWizardComponent,
    WsWizardStepComponent,
    WsConfirmationModalComponent,
    // CREATE steps
    TxUploadStepComponent,
    TxMappingStepComponent,
    TxPreviewStepComponent,
    TxProgressStepComponent,
    TxCompleteStepComponent,
    // UPDATE steps
    TxUpdateUploadStepComponent,
    TxUpdateMappingStepComponent,
    TxUpdatePreviewStepComponent,
    TxUpdateProgressStepComponent,
    TxUpdateCompleteStepComponent,
  ],
  templateUrl: './transaction-import-wizard.component.html',
  styleUrl: './transaction-import-wizard.component.scss',
})
export class TransactionImportWizardComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly currentStep = signal<WizardStep>('upload');
  readonly cancelConfirmOpen = signal(false);

  // Mode driven by ?mode=update query param; defaults to 'create'
  readonly mode = computed<WizardMode>(() => {
    const m = this.route.snapshot.queryParamMap.get('mode');
    return m === 'update' ? 'update' : 'create';
  });

  // ── CREATE mode state ──────────────────────────────────────────────────────
  parseResult = signal<(ParseResponse & { fileName: string; fileSize: number }) | null>(null);
  columnMapping = signal<TransactionImportColumnMapping | null>(null);
  validateResponse = signal<TransactionValidateResponse | null>(null);
  jobId = signal<string | null>(null);
  importResult = signal<TransactionImportResult | null>(null);

  // ── UPDATE mode state ──────────────────────────────────────────────────────
  updateParseResult = signal<(ParseResponse & { fileName: string; fileSize: number }) | null>(null);
  updateColumnMapping = signal<TransactionUpdateColumnMapping | null>(null);
  updateValidateResponse = signal<TransactionUpdateValidateResponse | null>(null);
  updateJobId = signal<string | null>(null);
  updateResult = signal<TransactionUpdateResult | null>(null);

  constructor() {
    // Persist CREATE mode state to sessionStorage so a page reload restores progress.
    effect(() => {
      if (this.mode() !== 'create') return;
      const step = this.currentStep();
      if (step === 'complete') {
        sessionStorage.removeItem(STORAGE_KEY_CREATE);
        return;
      }
      const state: PersistedCreateState = {
        step,
        parseResult: this.parseResult(),
        columnMapping: this.columnMapping(),
        validateResponse: this.validateResponse(),
      };
      try { sessionStorage.setItem(STORAGE_KEY_CREATE, JSON.stringify(state)); } catch { /* quota */ }
    });
  }

  ngOnInit(): void {
    if (this.mode() !== 'create') return;
    const raw = sessionStorage.getItem(STORAGE_KEY_CREATE);
    if (!raw) return;
    try {
      const state = JSON.parse(raw) as PersistedCreateState;
      if (state.parseResult) this.parseResult.set(state.parseResult);
      if (state.columnMapping) this.columnMapping.set(state.columnMapping);
      if (state.validateResponse) this.validateResponse.set(state.validateResponse);
      if (state.step && state.step !== 'upload' && state.step !== 'progress') {
        this.currentStep.set(state.step);
      }
    } catch {
      sessionStorage.removeItem(STORAGE_KEY_CREATE);
    }
  }

  // ── CREATE handlers ────────────────────────────────────────────────────────

  onParsed(result: ParseResponse & { fileName: string; fileSize: number }): void {
    this.parseResult.set(result);
    this.columnMapping.set(null);
    this.validateResponse.set(null);
    this.currentStep.set('map');
  }

  onValidated(event: { response: TransactionValidateResponse; mapping: TransactionImportColumnMapping }): void {
    this.columnMapping.set(event.mapping);
    this.validateResponse.set(event.response);
    this.currentStep.set('preview');
  }

  onExecuted(jobId: string): void {
    this.jobId.set(jobId);
    this.currentStep.set('progress');
  }

  onCompleted(result: TransactionImportResult): void {
    this.importResult.set(result);
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

  onImportMore(): void {
    sessionStorage.removeItem(STORAGE_KEY_CREATE);
    this.currentStep.set('upload');
    this.parseResult.set(null);
    this.columnMapping.set(null);
    this.validateResponse.set(null);
    this.jobId.set(null);
    this.importResult.set(null);
  }

  // ── UPDATE handlers ────────────────────────────────────────────────────────

  onUpdateParsed(result: ParseResponse & { fileName: string; fileSize: number }): void {
    this.updateParseResult.set(result);
    this.currentStep.set('map');
  }

  onUpdateValidated(event: { response: TransactionUpdateValidateResponse; mapping: TransactionUpdateColumnMapping }): void {
    this.updateColumnMapping.set(event.mapping);
    this.updateValidateResponse.set(event.response);
    this.currentStep.set('preview');
  }

  onUpdateExecuted(jobId: string): void {
    this.updateJobId.set(jobId);
    this.currentStep.set('progress');
  }

  onUpdateCompleted(result: TransactionUpdateResult): void {
    this.updateResult.set(result);
    this.currentStep.set('complete');
  }

  onUpdateBackToUpload(): void {
    this.currentStep.set('upload');
    this.updateParseResult.set(null);
  }

  onUpdateBackToMap(): void {
    this.currentStep.set('map');
    this.updateValidateResponse.set(null);
  }

  onUpdateRetry(): void {
    this.currentStep.set('preview');
    this.updateJobId.set(null);
  }

  onDone(): void {
    void this.router.navigate(['/transactions']);
  }

  // ── Cancel ─────────────────────────────────────────────────────────────────
  // Direct escape from the wizard. If there is work in progress, ask for a light
  // confirmation first so an accidental click doesn't discard the import/update.

  private hasWorkInProgress(): boolean {
    return this.parseResult() !== null
      || this.columnMapping() !== null
      || this.updateParseResult() !== null
      || this.updateColumnMapping() !== null;
  }

  requestCancel(): void {
    if (this.hasWorkInProgress()) {
      this.cancelConfirmOpen.set(true);
    } else {
      this.doCancel();
    }
  }

  confirmCancel(): void {
    this.cancelConfirmOpen.set(false);
    this.doCancel();
  }

  // Wipe ALL in-progress state for both modes (file, mapping, preview, errors; the
  // consent checkbox lives in the preview step and is destroyed with it) and leave.
  private doCancel(): void {
    sessionStorage.removeItem(STORAGE_KEY_CREATE);
    sessionStorage.removeItem(STORAGE_KEY_UPDATE);
    this.parseResult.set(null);
    this.columnMapping.set(null);
    this.validateResponse.set(null);
    this.jobId.set(null);
    this.importResult.set(null);
    this.updateParseResult.set(null);
    this.updateColumnMapping.set(null);
    this.updateValidateResponse.set(null);
    this.updateJobId.set(null);
    this.updateResult.set(null);
    this.currentStep.set('upload');
    void this.router.navigate(['/transactions']);
  }
}
