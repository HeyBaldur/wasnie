import { Component, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';
import { WsButtonComponent } from '../ws-button/ws-button.component';

const ILLUSTRATIONS: Record<string, string> = {
  'plans-empty': `
    <svg viewBox="0 0 160 120" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <rect x="22" y="20" width="72" height="88" rx="7" stroke="currentColor" stroke-width="1.5" fill="none" opacity="0.25"/>
      <rect x="30" y="14" width="72" height="88" rx="7" stroke="currentColor" stroke-width="1.5" fill="none" opacity="0.5"/>
      <rect x="38" y="8" width="72" height="88" rx="7" fill="var(--color-brand)" fill-opacity="0.06" stroke="currentColor" stroke-width="1.5"/>
      <rect x="38" y="8" width="72" height="26" rx="7" fill="var(--color-brand)" fill-opacity="0.15"/>
      <rect x="38" y="28" width="72" height="6" fill="var(--color-brand)" fill-opacity="0.08"/>
      <line x1="52" y1="50" x2="80" y2="50" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      <line x1="52" y1="62" x2="92" y2="62" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      <line x1="52" y1="74" x2="84" y2="74" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      <line x1="52" y1="86" x2="68" y2="86" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      <circle cx="124" cy="88" r="18" fill="var(--color-brand)" fill-opacity="0.10" stroke="var(--color-brand)" stroke-width="1.5" stroke-dasharray="3 3"/>
      <line x1="124" y1="80" x2="124" y2="96" stroke="var(--color-brand)" stroke-width="1.75" stroke-linecap="round"/>
      <line x1="116" y1="88" x2="132" y2="88" stroke="var(--color-brand)" stroke-width="1.75" stroke-linecap="round"/>
    </svg>`,
  'payees-empty': `
    <svg viewBox="0 0 160 120" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <circle cx="60" cy="46" r="18" fill="var(--color-brand)" fill-opacity="0.10" stroke="currentColor" stroke-width="1.5"/>
      <path d="M36 90a24 24 0 0 1 48 0" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      <circle cx="100" cy="42" r="16" fill="var(--color-brand)" fill-opacity="0.06" stroke="currentColor" stroke-width="1.5"/>
      <path d="M78 88a22 22 0 0 1 44 0" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      <circle cx="134" cy="48" r="12" fill="var(--color-brand)" fill-opacity="0.06" stroke="currentColor" stroke-width="1.5"/>
    </svg>`,
  'transactions-empty': `
    <svg viewBox="0 0 160 120" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <path d="M28 44 L92 44" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      <path d="M80 32 L92 44 L80 56" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
      <path d="M132 76 L68 76" stroke="var(--color-brand)" stroke-width="1.75" stroke-linecap="round"/>
      <path d="M80 64 L68 76 L80 88" stroke="var(--color-brand)" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round"/>
      <circle cx="132" cy="44" r="14" fill="var(--color-brand)" fill-opacity="0.10" stroke="var(--color-brand)" stroke-width="1.5"/>
    </svg>`,
  'payouts-empty': `
    <svg viewBox="0 0 160 120" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <circle cx="80" cy="58" r="38" fill="var(--color-brand)" fill-opacity="0.07" stroke="currentColor" stroke-width="1.5"/>
      <circle cx="80" cy="58" r="26" fill="var(--color-brand)" fill-opacity="0.10" stroke="var(--color-brand)" stroke-width="1.5"/>
      <path d="M73 48 h5 a6 6 0 1 1 0 12 a6 6 0 1 0 0 12 h5" stroke="var(--color-brand)" stroke-width="1.75" stroke-linecap="round"/>
    </svg>`,
  'quotas-empty': `
    <svg viewBox="0 0 160 120" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <circle cx="80" cy="60" r="46" stroke="currentColor" stroke-width="1.5" opacity="0.25"/>
      <circle cx="80" cy="60" r="33" stroke="currentColor" stroke-width="1.5" opacity="0.45"/>
      <circle cx="80" cy="60" r="20" stroke="var(--color-brand)" stroke-width="1.5" fill="var(--color-brand)" fill-opacity="0.08"/>
      <circle cx="80" cy="60" r="7" fill="var(--color-brand)" fill-opacity="0.80"/>
    </svg>`,
  'assignments-empty': `
    <svg viewBox="0 0 160 120" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <rect x="20" y="44" width="44" height="32" rx="16" fill="var(--color-brand)" fill-opacity="0.10" stroke="currentColor" stroke-width="1.5"/>
      <rect x="96" y="44" width="44" height="32" rx="16" fill="var(--color-brand)" fill-opacity="0.10" stroke="var(--color-brand)" stroke-width="1.5"/>
      <line x1="64" y1="60" x2="96" y2="60" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-dasharray="4 3"/>
      <circle cx="42" cy="60" r="8" fill="var(--color-brand)" fill-opacity="0.20"/>
      <circle cx="118" cy="60" r="8" fill="var(--color-brand)" fill-opacity="0.60"/>
    </svg>`,
};

@Component({
  selector: 'ws-empty-state',
  standalone: true,
  imports: [TranslatePipe, IconComponent, RouterLink, WsButtonComponent],
  templateUrl: './ws-empty-state.component.html',
  styleUrl: './ws-empty-state.component.scss',
})
export class WsEmptyStateComponent {
  private readonly sanitizer = inject(DomSanitizer);

  readonly illustration = input('');
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
