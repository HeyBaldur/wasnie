/**
 * The wizard's reload restore.
 *
 * ★ WHAT IS PINNED IS THAT A SERVER ANSWER SAVED UNDER AN OLDER CONTRACT IS NOT BROUGHT BACK. Reproduced
 * 2026-09-15: a review saved before `amount`/`currency` existed came back on reload and the Amount column
 * showed raw numbers with the new API already running. The template is stubbed — only the restore is tested.
 */
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

import { TransactionImportWizardComponent } from './transaction-import-wizard.component';

const LEGACY_KEY = 'wasnie:import-wizard:transactions';
const CURRENT_KEY = 'wasnie:import-wizard:transactions:v2';

function savedPreview(): string {
  return JSON.stringify({
    step: 'preview',
    parseResult: { fileId: 'f-1', headers: [], rowCount: 1, sampleRows: [], fileName: 'a.csv', fileSize: 1 },
    columnMapping: null,
    validateResponse: { totalRows: 1, errorCount: 0, warningCount: 0, validRowCount: 1, rowResults: [] },
  });
}

describe('TransactionImportWizardComponent — reload restore', () => {
  function create(): TransactionImportWizardComponent {
    TestBed.configureTestingModule({
      imports: [TransactionImportWizardComponent, TranslateModule.forRoot()],
      providers: [
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
      ],
    });
    TestBed.overrideComponent(TransactionImportWizardComponent, { set: { template: '<div></div>', imports: [] } });

    const component = TestBed.createComponent(TransactionImportWizardComponent).componentInstance;
    component.ngOnInit();
    return component;
  }

  beforeEach(() => {
    sessionStorage.removeItem(LEGACY_KEY);
    sessionStorage.removeItem(CURRENT_KEY);
  });

  afterEach(() => {
    sessionStorage.removeItem(LEGACY_KEY);
    sessionStorage.removeItem(CURRENT_KEY);
  });

  it('★ drops a review saved under the old key instead of showing it', () => {
    sessionStorage.setItem(LEGACY_KEY, savedPreview());

    const wizard = create();

    expect(wizard.currentStep()).toBe('upload');
    expect(wizard.validateResponse()).toBeNull();
    expect(sessionStorage.getItem(LEGACY_KEY)).toBeNull();
  });

  it('still restores a review saved under the current key', () => {
    sessionStorage.setItem(CURRENT_KEY, savedPreview());

    const wizard = create();

    expect(wizard.currentStep()).toBe('preview');
    expect(wizard.validateResponse()).not.toBeNull();
  });
});
