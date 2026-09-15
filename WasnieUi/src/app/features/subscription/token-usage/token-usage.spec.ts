/**
 * KAN-80 — the assistant's token usage as the screens show it.
 *
 * ★ THE FIXTURE IS THE SHAPE `GET /api/subscription/access` SENDS (§A4): `assistantTokensUsed` and `assistantTokenLimit`
 * as numbers, `assistantTokensSince` as an ISO string or null.
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

  it('a paying account reads as this billing period, with no limit', () => {
    expect(tokenUsageView(access({
      state: 'Active', trialDaysRemaining: null, trialLengthDays: null,
      assistantTokensUsed: 42_000, assistantTokenLimit: null, assistantTokensSince: '2026-09-01T00:00:00Z',
    }))).toEqual({ kind: 'period', used: 42_000, since: '2026-09-01T00:00:00Z' });
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

  it('shows a paying account its period usage, without a bar', () => {
    render(access({
      state: 'Active', assistantTokensUsed: 42_000, assistantTokenLimit: null, assistantTokensSince: '2026-09-01T00:00:00Z',
    }));

    expect(el().querySelector('[data-testid="token-usage-period"]')).not.toBeNull();
    expect(el().querySelector('[role="progressbar"]')).toBeNull();
    expect(el().textContent).toContain('ASSISTANT_USAGE.PERIOD_CAPTION');
  });

  it('renders nothing for a locked account', () => {
    render(access({ state: 'Locked', assistantTokensUsed: null, assistantTokenLimit: null }));

    expect(el().textContent!.trim()).toBe('');
  });
});
