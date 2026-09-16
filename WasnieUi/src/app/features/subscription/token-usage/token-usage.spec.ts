/**
 * KAN-80 / KAN-83 — the assistant's token usage as the screens show it.
 *
 * ★ THE FIXTURE IS THE SHAPE `GET /api/subscription/access` SENDS (§A4): `assistantTokensUsed`, `assistantTokenLimit`
 * and the boost figures as numbers, `assistantTokensSince` and `assistantBoostExpiresAt` as ISO strings or null.
 */
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';

import { AccountAccess } from '../services/subscription.service';
import { TokenUsageMeterComponent } from './token-usage-meter.component';
import { tokenUsageView } from './token-usage';

const access = (overrides: Partial<AccountAccess>): AccountAccess => ({
  state: 'Trial',
  lockReason: null,
  trialEndsAt: '2026-09-21T08:59:45Z',
  trialDaysRemaining: 6,
  trialLengthDays: 7,
  assistantTokensUsed: 250_000,
  assistantTokenLimit: 1_000_000,
  assistantTokensSince: null,
  assistantBoostRemaining: 0,
  assistantBoostExpiresAt: null,
  assistantBoostExpired: 0,
  ...overrides,
});

/** A paying account, which since KAN-83 has an allowance of its own. */
const paying = (overrides: Partial<AccountAccess> = {}): AccountAccess =>
  access({
    state: 'Active',
    trialDaysRemaining: null,
    trialLengthDays: null,
    assistantTokensUsed: 1_000_000,
    assistantTokenLimit: 3_000_000,
    assistantTokensSince: '2026-09-01T00:00:00Z',
    ...overrides,
  });

describe('tokenUsageView', () => {
  it('a trial reads as used of limit, with its share', () => {
    expect(tokenUsageView(access({}))).toEqual({
      kind: 'trial', used: 250_000, limit: 1_000_000, fraction: 0.25, exhausted: false, nearLimit: false,
    });
  });

  it('warns from 80% and marks the allowance used up at the limit', () => {
    expect(tokenUsageView(access({ assistantTokensUsed: 800_000 }))).toEqual(jasmine.objectContaining({ nearLimit: true, exhausted: false }));
    expect(tokenUsageView(access({ assistantTokensUsed: 1_000_000 }))).toEqual(jasmine.objectContaining({ exhausted: true }));
  });

  it('★ caps the bar at full when a last turn overshot the limit', () => {
    expect(tokenUsageView(access({ assistantTokensUsed: 1_012_345 }))).toEqual(
      jasmine.objectContaining({ fraction: 1, exhausted: true }));
  });

  it('a paying account reads as used of the tokens its plan includes this period', () => {
    expect(tokenUsageView(paying())).toEqual({
      kind: 'period',
      used: 1_000_000,
      limit: 3_000_000,
      fraction: 1 / 3,
      since: '2026-09-01T00:00:00Z',
      addonRemaining: 0,
      addonExpiresAt: null,
      addonExpired: 0,
      exhausted: false,
      nearLimit: false,
    });
  });

  it('★ a paying account with its included tokens gone but boost left is NOT out of tokens', () => {
    // The refusal on the server is "included AND boost gone". Saying "out of tokens" here while a paid boost still
    // answers would send a customer to buy what they already have.
    const view = tokenUsageView(paying({ assistantTokensUsed: 3_000_000, assistantBoostRemaining: 2_000_000 }));

    expect(view).toEqual(jasmine.objectContaining({ exhausted: false, addonRemaining: 2_000_000, fraction: 1 }));
  });

  it('is out of tokens only once the included allowance and the boost are both gone', () => {
    expect(tokenUsageView(paying({ assistantTokensUsed: 3_000_000, assistantBoostRemaining: 0 })))
      .toEqual(jasmine.objectContaining({ exhausted: true }));
  });

  it('★ the bar measures the INCLUDED share, not included plus boost', () => {
    // Spreading it across both would show a tenant with a big boost as barely used on the day their month ran out.
    const view = tokenUsageView(paying({ assistantTokensUsed: 1_500_000, assistantBoostRemaining: 12_000_000 }));

    expect(view).toEqual(jasmine.objectContaining({ fraction: 0.5 }));
  });

  it('warns when what is left of everything is down to the last fifth of a period', () => {
    const view = tokenUsageView(paying({ assistantTokensUsed: 2_500_000, assistantBoostRemaining: 0 }));

    expect(view).toEqual(jasmine.objectContaining({ nearLimit: true, exhausted: false }));
  });

  it('★★ warns AT the threshold exactly, not only past it', () => {
    // Found on screen: 600,000 left of 3,000,000 is exactly 20%, and deriving the bound as `1 - 0.8` made it
    // 0.19999999999999996 — so the workspace sitting precisely on the line got no warning at all.
    const view = tokenUsageView(paying({ assistantTokensUsed: 2_400_000, assistantBoostRemaining: 0 }));

    expect(view).toEqual(jasmine.objectContaining({ nearLimit: true, exhausted: false }));
  });

  it('★ an add-on balance counts towards the warning, so a topped-up workspace is not warned', () => {
    const view = tokenUsageView(paying({ assistantTokensUsed: 2_400_000, assistantBoostRemaining: 3_000_000 }));

    expect(view).toEqual(jasmine.objectContaining({ nearLimit: false }));
  });

  it('carries the boost expiry and what expired unused', () => {
    const view = tokenUsageView(paying({
      assistantBoostRemaining: 500_000,
      assistantBoostExpiresAt: '2027-03-01T00:00:00Z',
      assistantBoostExpired: 250_000,
    }));

    expect(view).toEqual(jasmine.objectContaining({
      addonExpiresAt: '2027-03-01T00:00:00Z', addonExpired: 250_000,
    }));
  });

  it('a locked account, or no usage yet from the server, shows nothing', () => {
    expect(tokenUsageView(access({ state: 'Locked', assistantTokensUsed: null, assistantTokenLimit: null }))).toEqual({ kind: 'none' });
    expect(tokenUsageView(null)).toEqual({ kind: 'none' });
  });
});

describe('TokenUsageMeterComponent', () => {
  let fixture: ComponentFixture<TokenUsageMeterComponent>;

  const el = () => fixture.nativeElement as HTMLElement;

  function render(a: AccountAccess | null): void {
    fixture.componentRef.setInput('access', a);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TokenUsageMeterComponent, TranslateModule.forRoot()] }).compileComponents();
    fixture = TestBed.createComponent(TokenUsageMeterComponent);
  });

  it('draws the trial bar to the share used, as an SVG attribute (no inline style)', () => {
    render(access({}));

    const fill = el().querySelector('.token-usage__fill')!;
    expect(fill.getAttribute('width')).toBe('25');
    expect(el().querySelector('[style]')).toBeNull();
    expect(el().querySelector('[role="progressbar"]')!.getAttribute('aria-valuenow')).toBe('250000');
  });

  it('says the allowance is used up at the limit', () => {
    render(access({ assistantTokensUsed: 1_000_000 }));

    expect(el().querySelector('[data-testid="token-usage-exhausted"]')).not.toBeNull();
    expect(el().querySelector('.token-usage--exhausted')).not.toBeNull();
  });

  it('draws a paying account its own bar over the tokens its plan includes', () => {
    render(paying());

    expect(el().querySelector('[data-testid="token-usage-period"]')).not.toBeNull();
    expect(el().querySelector('.token-usage__fill')!.getAttribute('width')).toBe('33.3');
    expect(el().textContent).toContain('ASSISTANT_USAGE.PERIOD_CAPTION');
  });

  it('shows the boost on its own line, with when it expires', () => {
    render(paying({ assistantBoostRemaining: 2_000_000, assistantBoostExpiresAt: '2027-03-01T00:00:00Z' }));

    const note = el().querySelector('[data-testid="token-usage-addon"]')!;
    expect(note).not.toBeNull();
    expect(note.textContent).toContain('ASSISTANT_USAGE.ADDON_REMAINING');
    expect(note.textContent).toContain('ASSISTANT_USAGE.ADDON_EXPIRES');
  });

  it('★ does not say "out of tokens" while a paid boost can still answer', () => {
    render(paying({ assistantTokensUsed: 3_000_000, assistantBoostRemaining: 2_000_000 }));

    expect(el().querySelector('[data-testid="token-usage-exhausted"]')).toBeNull();
    expect(el().querySelector('[data-testid="token-usage-addon"]')).not.toBeNull();
  });

  it('says a paying account is out of tokens once the boost is gone too', () => {
    render(paying({ assistantTokensUsed: 3_000_000, assistantBoostRemaining: 0 }));

    expect(el().querySelector('[data-testid="token-usage-exhausted"]')).not.toBeNull();
  });

  it('surfaces boost tokens that expired unused, so the loss is never silent', () => {
    render(paying({ assistantBoostExpired: 250_000 }));

    expect(el().querySelector('[data-testid="token-usage-addon-expired"]')).not.toBeNull();
  });

  it('renders nothing for a locked account', () => {
    render(access({ state: 'Locked', assistantTokensUsed: null, assistantTokenLimit: null }));

    expect(el().textContent!.trim()).toBe('');
  });
});
