import { Component, computed, effect, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { WsPageLayoutComponent, WsWizardComponent, WsWizardStepComponent, WsConfirmationModalComponent } from '../../../shared/ui';
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
import { PayeesStore } from '../../payees/state/payees.store';
import { SubscriptionStateService } from '../../subscription/services/subscription-state.service';
import { TIER_LIMITS } from '../../../shared/services/tier-limits';

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
    WsConfirmationModalComponent,
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
  private readonly payeesStore = inject(PayeesStore);
  private readonly subState = inject(SubscriptionStateService);
  private readonly router = inject(Router);

  readonly currentStep = signal<WizardStep>('upload');
  readonly cancelConfirmOpen = signal(false);

  private get payeesTierLimit(): number {
    const tier = this.subState.subscription()?.tier ?? 'Free';
    return TIER_LIMITS[tier]?.maxPayees ?? -1;
  }

  readonly atPayeesLimit = computed(() => {
    const limit = this.payeesTierLimit;
    if (limit < 0) return false;
    return this.payeesStore.unfilteredTotal() >= limit;
  });

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

  // Cancel = direct escape from the wizard. If there is work in progress, ask for a
  // light confirmation first so an accidental click doesn't discard the import.
  private hasWorkInProgress(): boolean {
    return this.parseResult() !== null || this.columnMapping() !== null;
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

  // Wipe ALL in-progress import state (file, mapping, preview, errors; the consent
  // checkbox lives in the preview step and is destroyed with it) and leave the wizard.
  private doCancel(): void {
    sessionStorage.removeItem(STORAGE_KEY);
    this.parseResult.set(null);
    this.columnMapping.set(null);
    this.validateResponse.set(null);
    this.importResult.set(null);
    this.skipWarnings.set(false);
    this.currentStep.set('upload');
    void this.router.navigate(['/payees']);
  }
}
