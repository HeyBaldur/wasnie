import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of } from 'rxjs';
import { IntegrationsComponent } from './integrations.component';
import { HubSpotApiService } from './services/hubspot.api.service';
import { HubSpotConnectionStatus } from './models/hubspot.model';
import enTranslations from '../../../assets/i18n/en.json';
import esTranslations from '../../../assets/i18n/es.json';
import plTranslations from '../../../assets/i18n/pl.json';

/** The shipped INTEGRATIONS sections, keyed by language. */
const BUNDLES: Record<string, Record<string, unknown>> = {
  en: (enTranslations as unknown as Record<string, Record<string, unknown>>)['INTEGRATIONS'],
  es: (esTranslations as unknown as Record<string, Record<string, unknown>>)['INTEGRATIONS'],
  pl: (plTranslations as unknown as Record<string, Record<string, unknown>>)['INTEGRATIONS'],
};

const NAV: Record<string, Record<string, string>> = {
  en: (enTranslations as unknown as Record<string, Record<string, string>>)['NAV'],
  es: (esTranslations as unknown as Record<string, Record<string, string>>)['NAV'],
  pl: (plTranslations as unknown as Record<string, Record<string, string>>)['NAV'],
};

const tutorial = (language: string): Record<string, string> =>
  BUNDLES[language]['TUTORIAL'] as Record<string, string>;

/**
 * The HubSpot connector page: the rename, and the tutorial that fills the space the tile wall left.
 *
 * ★ WHAT IS WORTH TESTING HERE IS NOT THE LAYOUT. The grid and the card styling are SCSS and a test
 * asserting pixel intent would only restate the stylesheet. Two things carry real risk: that the copy
 * exists in all three languages (a missing key renders the KEY at the user, in a screen a finance admin
 * reads once and follows), and that the tutorial stays INFORMATION — the moment somebody adds a helpful
 * "Connect now" button to it there are two controls for one action.
 */
describe('IntegrationsComponent — the HubSpot connector page', () => {
  let fixture: ComponentFixture<IntegrationsComponent>;

  const DISCONNECTED: HubSpotConnectionStatus = {
    status: 'Disconnected',
  } as HubSpotConnectionStatus;

  beforeEach(async () => {
    const api = jasmine.createSpyObj<HubSpotApiService>('HubSpotApiService', ['getStatus']);
    api.getStatus.and.returnValue(of(DISCONNECTED));

    await TestBed.configureTestingModule({
      imports: [IntegrationsComponent, TranslateModule.forRoot()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: HubSpotApiService, useValue: api },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(IntegrationsComponent);
    fixture.detectChanges();
  });

  it('renders the tutorial beside the connection card', () => {
    const tutorialCard = fixture.nativeElement.querySelector('.int-tutorial');

    expect(tutorialCard).withContext('the page no longer ends at the connection card').toBeTruthy();
    expect(tutorialCard.querySelectorAll('.int-tutorial__step').length)
      .withContext('one block per step')
      .toBe(fixture.componentInstance.tutorialSteps.length);
  });

  it('★ the tutorial adds NO controls — connecting is one button, and it is not in here', () => {
    // Incentra's whole side of this flow is the Connect button in the card next door; everything after
    // it happens on HubSpot's screens. A second control here would be a second way to do one thing.
    const tutorialCard: HTMLElement = fixture.nativeElement.querySelector('.int-tutorial');

    expect(tutorialCard.querySelectorAll('button').length).withContext('no buttons').toBe(0);
    expect(tutorialCard.querySelectorAll('a').length).withContext('no links').toBe(0);
    expect(tutorialCard.querySelectorAll('input').length).withContext('no inputs').toBe(0);
  });

  /**
   * ★★ THE FACTS THE TUTORIAL ASSERTS, AND WHERE THEY COME FROM. Each was read off the server during a
   * diagnosis that followed the flow end to end — the first draft of this page was written from
   * HubSpot's generic marketplace documentation and got several of them wrong. These tests are the
   * tripwire: if the server changes, the copy is no longer merely vague, it is false.
   *
   *   paid plan        StartHubSpotConnectionCommand.cs:36   RequirePaidPlanAsync
   *   permission       StartHubSpotConnectionCommand.cs:32   Permission.IntegrationsManage
   *   10-minute link   HubSpotOptions.cs:103                 StateTtlMinutes = 10
   *   four scopes      HubSpotOptions.cs:33                  Scopes (all read)
   *   hourly sync      HubSpotSyncOptions.cs:23              CronExpression "0 * * * *"
   *   unmapped owner   CrmDealReconciler.cs:161,172          payeeId null → still ingested
   */
  it('★ states all THREE ways connecting can fail, not only HubSpot\'s', () => {
    // Two of the three are on Incentra's side and refuse the user before the browser ever reaches
    // HubSpot. The first draft mentioned neither, so a Free-plan admin would have followed the steps
    // to a dead end and blamed the connector.
    expect(tutorial('en')['PREREQ_PLAN']).toContain('Free');
    expect(tutorial('en')['PREREQ_PERMISSION']).toContain('Manage integrations');
    expect(tutorial('en')['PREREQ_HUBSPOT']).toContain('Super Admin');

    // HubSpot's permission is named in ITS OWN words in every language on purpose: someone hunting for
    // it in the HubSpot UI needs the string HubSpot actually shows them, not a translation of it.
    for (const language of ['en', 'es', 'pl']) {
      expect(tutorial(language)['PREREQ_HUBSPOT'])
        .withContext(`${language} must keep HubSpot's own wording`)
        .toContain('App Marketplace Access');
    }
  });

  it('★ names exactly the four read scopes the server requests', () => {
    // Naming them is safe because they are OUR app's, fixed in server config — not a marketplace
    // listing that can change under us. The COUNT is what this pins: a fifth scope added on the server
    // must not leave this page quietly promising four.
    const component = fixture.componentInstance;

    expect(component.tutorialScopes.length).toBe(4);
    expect([...component.tutorialScopes]).toEqual(['DEALS', 'OWNERS', 'SCHEMAS', 'LINE_ITEMS']);

    const rendered: HTMLElement = fixture.nativeElement.querySelector('.int-tutorial__scopes');
    expect(rendered).withContext('the scopes are actually shown').toBeTruthy();
    expect(rendered.querySelectorAll('li').length).toBe(4);
  });

  it('★ says Incentra only READS — nothing here may promise a write', () => {
    // Every scope on the server ends in `.read`. That nothing goes back the other way is the promise a
    // finance admin is approving, and the most load-bearing sentence on the page.
    expect(tutorial('en')['READ_ONLY_NOTE']).toContain('only reads');
    expect(tutorial('es')['READ_ONLY_NOTE']).toContain('solo lee');
    expect(tutorial('pl')['READ_ONLY_NOTE']).toContain('tylko odczytuje');
  });

  it('★ warns that the authorisation link expires, in every language', () => {
    // StateTtlMinutes = 10. It expires silently, on HubSpot's side, after the user has left Incentra —
    // which reads as the connector being broken rather than as "press Connect again".
    for (const language of ['en', 'es', 'pl']) {
      expect(tutorial(language)['STEP_2_WARNING'])
        .withContext(`${language} must mention the 10-minute window`)
        .toContain('10');
    }

    expect(fixture.nativeElement.querySelector('.int-tutorial__step-warning'))
      .withContext('and it is rendered, not merely translated')
      .toBeTruthy();
  });

  it('★ says deals arrive on their own, and that an unmapped owner still costs commission', () => {
    // The first draft implied a manual import was needed; a Hangfire job brings deals in hourly.
    expect(tutorial('en')['AFTER_SYNC_BODY']).toContain('every hour');

    // And the nuance that actually bites: the transaction IS created with a null payee, so the data
    // looks fine while nobody is being paid for it.
    expect(tutorial('en')['AFTER_OWNERS_BODY']).toContain('pays commission to nobody');
  });

  it('★ the whole tutorial exists in EN, ES and PL', () => {
    const keys = Object.keys(tutorial('en'));

    expect(keys.length).withContext('the English bundle is the reference').toBeGreaterThan(0);

    for (const language of ['es', 'pl']) {
      for (const key of keys) {
        expect(tutorial(language)[key]).withContext(`${language} is missing TUTORIAL.${key}`).toBeTruthy();
        expect(tutorial(language)[key])
          .withContext(`${language} left TUTORIAL.${key} in English`)
          .not.toBe(tutorial('en')[key]);
      }
    }
  });

  it('★ the section is named after HubSpot, not "Integrations", everywhere', () => {
    // Plural "Integrations" promised a directory. There is one connector and there will go on being
    // one, so the sidebar and the page title say which.
    for (const language of ['en', 'es', 'pl']) {
      expect(NAV[language]['INTEGRATIONS'])
        .withContext(`${language} sidebar`)
        .toContain('HubSpot');
      expect(BUNDLES[language]['TITLE'] as string)
        .withContext(`${language} page title`)
        .toContain('HubSpot');
    }
  });
});
