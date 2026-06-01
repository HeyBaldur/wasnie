import { Component, effect, OnInit, signal } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { WsPageLayoutComponent, WsWizardComponent, WsWizardStepComponent } from '../../../shared/ui';
import { UploadStepComponent } from './steps/upload-step.component';
import { MappingStepComponent } from './steps/mapping-step.component';
import { PreviewStepComponent } from './steps/preview-step.component';
import { PayeeImportingStepComponent } from './steps/importing-step.component';
import { CompleteStepComponent } from './steps/complete-step.component';
import {
  PayeeImportColumnMapping,
  PayeeImportResult,
  ParseResponse,
  ValidateResponse,
} from './models/payee-import.models';

type WizardStep = 'upload' | 'map' | 'preview' | 'importing' | 'complete';

const STORAGE_KEY = 'wasnie:import-wizard:payees';

interface PersistedWizardState {
  step: WizardStep;
  parseResult: (ParseResponse & { fileName: string; fileSize: number }) | null;
  columnMapping: PayeeImportColumnMapping | null;
  validateResponse: ValidateResponse | null;
}

@Component({
  selector: 'app-payee-import-wizard',
  standalone: true,
  imports: [
    AppShellComponent,
    TranslateModule,
    WsPageLayoutComponent,
    WsWizardComponent,
    WsWizardStepComponent,
    UploadStepComponent,
    MappingStepComponent,
    PreviewStepComponent,
    PayeeImportingStepComponent,
    CompleteStepComponent,
  ],
  templateUrl: './payee-import-wizard.component.html',
  styleUrl: './payee-import-wizard.component.scss',
})
export class PayeeImportWizardComponent implements OnInit {
  readonly currentStep = signal<WizardStep>('upload');

  parseResult = signal<(ParseResponse & { fileName: string; fileSize: number }) | null>(null);
  columnMapping = signal<PayeeImportColumnMapping | null>(null);
  validateResponse = signal<ValidateResponse | null>(null);
  importResult = signal<PayeeImportResult | null>(null);
  skipWarnings = signal(false);

  readonly steps: { key: WizardStep; labelKey: string }[] = [
    { key: 'upload',    labelKey: 'IMPORTS.PAYEES.STEP_UPLOAD' },
    { key: 'map',       labelKey: 'IMPORTS.PAYEES.STEP_MAP' },
    { key: 'preview',   labelKey: 'IMPORTS.PAYEES.STEP_PREVIEW' },
    { key: 'importing', labelKey: 'IMPORTS.PAYEES.STEP_IMPORTING' },
  ];

  constructor() {
    effect(() => {
      const step = this.currentStep();
      if (step === 'complete') {
        sessionStorage.removeItem(STORAGE_KEY);
        return;
      }
      const state: PersistedWizardState = {
        step,
        parseResult: this.parseResult(),
        columnMapping: this.columnMapping(),
        validateResponse: this.validateResponse(),
      };
      try {
        sessionStorage.setItem(STORAGE_KEY, JSON.stringify(state));
      } catch {
        // quota exceeded — not critical
      }
    });
  }

  ngOnInit(): void {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return;
    try {
      const state = JSON.parse(raw) as PersistedWizardState;
      if (state.parseResult)    this.parseResult.set(state.parseResult);
      if (state.columnMapping)  this.columnMapping.set(state.columnMapping);
      if (state.validateResponse) this.validateResponse.set(state.validateResponse);
      // Never restore 'importing' step from session — the request is gone
      const safeStep = state.step === 'importing' ? 'preview' : state.step;
      if (safeStep && safeStep !== 'upload') this.currentStep.set(safeStep);
    } catch {
      sessionStorage.removeItem(STORAGE_KEY);
    }
  }

  stepIndex(step: WizardStep): number {
    const map: Record<WizardStep, number> = {
      upload: 0, map: 1, preview: 2, importing: 3, complete: 4,
    };
    return map[step];
  }

  isStepDone(step: WizardStep): boolean {
    return this.stepIndex(step) < this.stepIndex(this.currentStep());
  }

  isStepActive(step: WizardStep): boolean {
    return step === this.currentStep()
      || (step === 'importing' && this.currentStep() === 'complete');
  }

  onParsed(result: ParseResponse & { fileName: string; fileSize: number }): void {
    this.parseResult.set(result);
    this.columnMapping.set(null);
    this.validateResponse.set(null);
    this.currentStep.set('map');
  }

  onValidated(event: { response: ValidateResponse; mapping: PayeeImportColumnMapping }): void {
    this.columnMapping.set(event.mapping);
    this.validateResponse.set(event.response);
    this.currentStep.set('preview');
  }

  onImportRequested(event: { skipWarnings: boolean }): void {
    this.skipWarnings.set(event.skipWarnings);
    this.currentStep.set('importing');
  }

  onImportCompleted(result: PayeeImportResult): void {
    this.importResult.set(result);
    this.currentStep.set('complete');
  }

  onImportRetry(): void {
    this.currentStep.set('preview');
  }

  onBackToUpload(): void {
    this.currentStep.set('upload');
    this.parseResult.set(null);
  }

  onBackToMap(): void {
    this.currentStep.set('map');
    this.validateResponse.set(null);
  }

  onImportMore(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    this.currentStep.set('upload');
    this.parseResult.set(null);
    this.columnMapping.set(null);
    this.validateResponse.set(null);
    this.importResult.set(null);
    this.skipWarnings.set(false);
  }
}
