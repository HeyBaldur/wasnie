import { Component, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';
import { WsButtonComponent } from '../ws-button/ws-button.component';
import { WsVideoComponent } from '../ws-video/ws-video.component';

const ILLUSTRATIONS: Record<string, string> = {
  'plans-empty': `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path stroke="none" d="M0 0h24v24H0z" fill="none" />
      <path d="M10 19h-6a1 1 0 0 1 -1 -1v-14a1 1 0 0 1 1 -1h6a2 2 0 0 1 2 2a2 2 0 0 1 2 -2h6a1 1 0 0 1 1 1v14a1 1 0 0 1 -1 1h-6a2 2 0 0 0 -2 2a2 2 0 0 0 -2 -2" />
      <path d="M12 5v16" />
      <path d="M7 7h1" />
      <path d="M7 11h1" />
      <path d="M16 7h1" />
      <path d="M16 11h1" />
      <path d="M16 15h1" />
    </svg>`,
  'payees-empty': `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path stroke="none" d="M0 0h24v24H0z" fill="none" />
      <path d="M5 7a4 4 0 1 0 8 0a4 4 0 1 0 -8 0" />
      <path d="M3 21v-2a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4v2" />
      <path d="M16 3.13a4 4 0 0 1 0 7.75" />
      <path d="M21 21v-2a4 4 0 0 0 -3 -3.85" />
    </svg>`,
  'transactions-empty': `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path stroke="none" d="M0 0h24v24H0z" fill="none" />
      <path d="M5 21v-16a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v16l-3 -2l-2 2l-2 -2l-2 2l-2 -2l-3 2" />
      <path d="M15 7.8c-.523 -.502 -1.172 -.8 -1.875 -.8c-1.727 0 -3.125 1.791 -3.125 4s1.398 4 3.125 4c.703 0 1.352 -.298 1.874 -.8" />
      <path d="M9 11h4" />
    </svg>`,
  'payouts-empty': `
    <svg viewBox="0 0 160 120" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <circle cx="80" cy="58" r="38" fill="var(--color-brand)" fill-opacity="0.07" stroke="currentColor" stroke-width="1.5"/>
      <circle cx="80" cy="58" r="26" fill="var(--color-brand)" fill-opacity="0.10" stroke="var(--color-brand)" stroke-width="1.5"/>
      <path d="M73 48 h5 a6 6 0 1 1 0 12 a6 6 0 1 0 0 12 h5" stroke="var(--color-brand)" stroke-width="1.75" stroke-linecap="round"/>
    </svg>`,
  'quotas-empty': `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path stroke="none" d="M0 0h24v24H0z" fill="none" />
      <path d="M11 6a5 3 0 1 0 10 0a5 3 0 1 0 -10 0" />
      <path d="M11 6v4c0 1.657 2.239 3 5 3s5 -1.343 5 -3v-4" />
      <path d="M11 10v4c0 1.657 2.239 3 5 3s5 -1.343 5 -3v-4" />
      <path d="M11 14v4c0 1.657 2.239 3 5 3s5 -1.343 5 -3v-4" />
      <path d="M7 9h-2.5a1.5 1.5 0 0 0 0 3h1a1.5 1.5 0 0 1 0 3h-2.5" />
      <path d="M5 15v1m0 -8v1" />
    </svg>`,
  // The terminated-accounts queue empty is a GOOD state — everyone who left is settled — so the
  // illustration is a person with a tick, not the "nothing here yet" shrug the other screens use.
  'terminated-empty': `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path stroke="none" d="M0 0h24v24H0z" fill="none" />
      <path d="M8 7a4 4 0 1 0 8 0a4 4 0 0 0 -8 0" />
      <path d="M6 21v-2a4 4 0 0 1 4 -4h4" />
      <path d="M15 19l2 2l4 -4" />
    </svg>`,
  'assignments-empty': `
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
      <path stroke="none" d="M0 0h24v24H0z" fill="none" />
      <path d="M3 16a1 1 0 0 1 1 -1h2a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1l0 -2" />
      <path d="M10 16a1 1 0 0 1 1 -1h2a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1l0 -2" />
      <path d="M17 16a1 1 0 0 1 1 -1h2a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1h-2a1 1 0 0 1 -1 -1l0 -2" />
      <path d="M5 11v-3a3 3 0 0 1 3 -3h8a3 3 0 0 1 3 3v3" />
      <path d="M16.5 8.5l2.5 2.5l2.5 -2.5" />
    </svg>`,
};

@Component({
  selector: 'ws-empty-state',
  standalone: true,
  imports: [TranslatePipe, IconComponent, RouterLink, WsButtonComponent, WsVideoComponent],
  templateUrl: './ws-empty-state.component.html',
  styleUrl: './ws-empty-state.component.scss',
})
export class WsEmptyStateComponent {
  private readonly sanitizer = inject(DomSanitizer);

  readonly illustration = input('');
  /**
   * A looping clip used INSTEAD of `illustration`/`icon`, e.g. "/videos/quotas.mp4". Takes precedence
   * over both: a screen that has a clip has no use for the static fallback underneath it.
   */
  readonly video = input('');
  /** Still frame for the clip — what reduced-motion users see in place of the animation. */
  readonly videoPoster = input('');
  readonly icon = input('');
  readonly titleKey = input.required<string>();
  readonly descKey = input('');
  readonly actionKey = input('');
  readonly actionRoute = input('');
  readonly secondaryActionKey = input('');
  readonly secondaryActionRoute = input('');
  readonly actionClick = output<void>();

  /**
   * SAFETY NOTE (ARCHITECTURE.md Rule 4.6.1):
   * This bypassSecurityTrustHtml call is safe because:
   * 1. The input (ILLUSTRATIONS[key]) is a compile-time constant dictionary defined in this file.
   * 2. No user-provided content is ever passed to this method.
   * 3. The SVG strings are hardcoded illustration graphics reviewed for safety.
   *
   * DO NOT modify this pattern to accept user input without first introducing
   * DOMPurify sanitization. See WI-13 / F-020 audit finding.
   */
  readonly illustrationHtml = (): SafeHtml | null => {
    const key = this.illustration();
    if (!key || !ILLUSTRATIONS[key]) return null;
    return this.sanitizer.bypassSecurityTrustHtml(ILLUSTRATIONS[key]);
  };
}
