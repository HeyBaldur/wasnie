import { Component, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';
import { WsButtonComponent } from '../ws-button/ws-button.component';
import { WsVideoComponent } from '../ws-video/ws-video.component';

const ILLUSTRATIONS: Record<string, string> = {
  'plans-empty': `
    <svg viewBox="0 0 220 165" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <style>
      .pe-surface { fill: var(--color-bg-surface); stroke: var(--color-border-default); stroke-width: 1.5; }
      .pe-sunken { fill: var(--color-bg-surface-sunken); }
      .pe-line { fill: var(--color-border-default); }
      .pe-line-strong { fill: var(--color-text-tertiary); opacity: .45; }
      .pe-violet { fill: var(--color-accent-violet); }
      .pe-blue { fill: var(--color-accent-blue); }
      .pe-success { fill: var(--color-success); }
      .pe-halo { fill: color-mix(in srgb, var(--color-accent-violet) 9%, transparent); stroke: color-mix(in srgb, var(--color-accent-blue) 22%, transparent); stroke-dasharray: 3 6; }
      .pe-dashed { fill: none; stroke: var(--color-border-strong); stroke-width: 1.5; stroke-dasharray: 5 5; }
      .pe-ink { stroke: var(--color-bg-surface); stroke-width: 2.5; stroke-linecap: round; stroke-linejoin: round; fill: none; }
      .pe-float { animation: pe-float 5s ease-in-out infinite; }
      .pe-twinkle { transform-box: fill-box; transform-origin: center; animation: pe-twinkle 2.6s ease-in-out infinite; }
      .pe-pop { transform-box: fill-box; transform-origin: center; animation: pe-pop 3.2s cubic-bezier(.34,1.56,.64,1) infinite; }
      @keyframes pe-float { 0%,100% { transform: translateY(0); } 50% { transform: translateY(-4px); } }
      @keyframes pe-twinkle { 0%,100% { opacity: .25; transform: scale(.8); } 50% { opacity: .9; transform: scale(1.1); } }
      @keyframes pe-pop { 0%,15% { transform: scale(0); } 30% { transform: scale(1.15); } 40%,85% { transform: scale(1); } 100% { transform: scale(0); } }
      .pe-row { transform-box: fill-box; transform-origin: left center; animation: pe-row 4.5s ease-in-out infinite; }
      .pe-step { transform-box: fill-box; transform-origin: bottom; animation: pe-step 4.5s cubic-bezier(.34,1.3,.64,1) infinite; }
      @keyframes pe-row { 0%,8% { transform: scaleX(0); opacity: 0; } 22%,85% { transform: scaleX(1); opacity: 1; } 100% { transform: scaleX(1); opacity: 0; } }
      @keyframes pe-step { 0%,10% { transform: scaleY(0); } 30%,85% { transform: scaleY(1); } 100% { transform: scaleY(0); } }
      @media (prefers-reduced-motion: reduce) { .pe-float, .pe-twinkle, .pe-pop, .pe-row, .pe-step { animation: none !important; transform: none !important; opacity: 1 !important; } }
      </style>
      <circle class="pe-halo" cx="110" cy="84" r="70"/>
      <path class="pe-twinkle" d="M30 36l2.5 6 6 2.5-6 2.5-2.5 6-2.5-6-6-2.5 6-2.5z" style="fill: var(--color-accent-violet)"/>
      <circle class="pe-twinkle" cx="194" cy="118" r="3" style="fill: var(--color-accent-blue); animation-delay: 1s"/>
      <g class="pe-float">
        <rect class="pe-surface" x="46" y="30" width="96" height="112" rx="12"/>
        <rect class="pe-violet" x="46" y="30" width="96" height="6" rx="3" opacity=".85"/>
        <rect class="pe-line-strong" x="60" y="48" width="48" height="7" rx="3.5"/>
        <rect class="pe-line" x="60" y="61" width="30" height="5" rx="2.5"/>
        <g>
          <rect class="pe-row pe-sunken" x="58" y="76" width="72" height="14" rx="4" style="animation-delay: .2s"/>
          <rect class="pe-row pe-sunken" x="58" y="96" width="72" height="14" rx="4" style="animation-delay: .6s"/>
          <rect class="pe-row pe-sunken" x="58" y="116" width="72" height="14" rx="4" style="animation-delay: 1s"/>
          <circle class="pe-row pe-blue" cx="66" cy="83" r="3" style="animation-delay: .2s"/>
          <circle class="pe-row pe-violet" cx="66" cy="103" r="3" style="animation-delay: .6s"/>
          <circle class="pe-row pe-success" cx="66" cy="123" r="3" style="animation-delay: 1s"/>
        </g>
      </g>
      <g class="pe-float" style="animation-delay: 1.2s">
        <rect class="pe-surface" x="126" y="62" width="62" height="58" rx="10"/>
        <rect class="pe-step pe-blue" x="137" y="98" width="10" height="12" rx="2" style="animation-delay: .4s"/>
        <rect class="pe-step pe-violet" x="152" y="88" width="10" height="22" rx="2" style="animation-delay: .7s"/>
        <rect class="pe-step pe-success" x="167" y="76" width="10" height="34" rx="2" style="animation-delay: 1s"/>
      </g>
      <g class="pe-pop" style="animation-delay: .3s">
        <circle class="pe-violet" cx="146" cy="36" r="13"/>
        <path class="pe-ink" d="M146 30v12M140 36h12"/>
      </g>
    </svg>`,
  'payees-empty': `
    <svg viewBox="0 0 220 165" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <style>
      .py-surface { fill: var(--color-bg-surface); stroke: var(--color-border-default); stroke-width: 1.5; }
      .py-sunken { fill: var(--color-bg-surface-sunken); }
      .py-line { fill: var(--color-border-default); }
      .py-line-strong { fill: var(--color-text-tertiary); opacity: .45; }
      .py-violet { fill: var(--color-accent-violet); }
      .py-blue { fill: var(--color-accent-blue); }
      .py-success { fill: var(--color-success); }
      .py-halo { fill: color-mix(in srgb, var(--color-accent-violet) 9%, transparent); stroke: color-mix(in srgb, var(--color-accent-blue) 22%, transparent); stroke-dasharray: 3 6; }
      .py-dashed { fill: none; stroke: var(--color-border-strong); stroke-width: 1.5; stroke-dasharray: 5 5; }
      .py-ink { stroke: var(--color-bg-surface); stroke-width: 2.5; stroke-linecap: round; stroke-linejoin: round; fill: none; }
      .py-float { animation: py-float 5s ease-in-out infinite; }
      .py-twinkle { transform-box: fill-box; transform-origin: center; animation: py-twinkle 2.6s ease-in-out infinite; }
      .py-pop { transform-box: fill-box; transform-origin: center; animation: py-pop 3.2s cubic-bezier(.34,1.56,.64,1) infinite; }
      @keyframes py-float { 0%,100% { transform: translateY(0); } 50% { transform: translateY(-4px); } }
      @keyframes py-twinkle { 0%,100% { opacity: .25; transform: scale(.8); } 50% { opacity: .9; transform: scale(1.1); } }
      @keyframes py-pop { 0%,15% { transform: scale(0); } 30% { transform: scale(1.15); } 40%,85% { transform: scale(1); } 100% { transform: scale(0); } }
      .py-seat { animation: py-seat 3s ease-in-out infinite; }
      .py-link { stroke: var(--color-border-strong); stroke-width: 1.5; stroke-dasharray: 4 5; animation: py-dash 1.4s linear infinite; }
      @keyframes py-seat { 0%,100% { opacity: .45; } 50% { opacity: .9; } }
      @keyframes py-dash { to { stroke-dashoffset: -18; } }
      @media (prefers-reduced-motion: reduce) { .py-float, .py-twinkle, .py-pop, .py-seat, .py-link { animation: none !important; transform: none !important; opacity: 1 !important; } }
      </style>
      <circle class="py-halo" cx="110" cy="82" r="70"/>
      <path class="py-link" d="M62 84h26M132 84h26"/>
      <g class="py-seat">
        <rect class="py-dashed" x="18" y="58" width="44" height="54" rx="10"/>
        <circle class="py-dashed" cx="40" cy="78" r="9"/>
        <path class="py-dashed" d="M28 104c3-8 21-8 24 0"/>
      </g>
      <g class="py-seat" style="animation-delay: 1.5s">
        <rect class="py-dashed" x="158" y="58" width="44" height="54" rx="10"/>
        <circle class="py-dashed" cx="180" cy="78" r="9"/>
        <path class="py-dashed" d="M168 104c3-8 21-8 24 0"/>
      </g>
      <g class="py-float">
        <rect class="py-surface" x="80" y="40" width="60" height="88" rx="14"/>
        <circle cx="110" cy="70" r="16" style="fill: color-mix(in srgb, var(--color-accent-violet) 18%, var(--color-bg-surface))"/>
        <circle class="py-violet" cx="110" cy="66" r="6"/>
        <path d="M100 80c2-6 18-6 20 0" style="stroke: var(--color-accent-violet); stroke-width: 3; stroke-linecap: round"/>
        <rect class="py-line-strong" x="92" y="96" width="36" height="6" rx="3"/>
        <rect class="py-line" x="98" y="107" width="24" height="5" rx="2.5"/>
        <rect x="96" y="117" width="28" height="6" rx="3" style="fill: color-mix(in srgb, var(--color-success) 25%, transparent)"/>
      </g>
      <g class="py-pop" style="animation-delay: .4s">
        <circle class="py-blue" cx="138" cy="44" r="12"/>
        <path class="py-ink" d="M138 38.5v11M132.5 44h11"/>
      </g>
      <circle class="py-twinkle" cx="34" cy="30" r="3" style="fill: var(--color-accent-violet)"/>
      <circle class="py-twinkle" cx="190" cy="138" r="2.5" style="fill: var(--color-accent-blue); animation-delay: 1.2s"/>
    </svg>`,
  'transactions-empty': `
    <svg viewBox="0 0 220 165" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <style>
      .tx-surface { fill: var(--color-bg-surface); stroke: var(--color-border-default); stroke-width: 1.5; }
      .tx-sunken { fill: var(--color-bg-surface-sunken); }
      .tx-line { fill: var(--color-border-default); }
      .tx-line-strong { fill: var(--color-text-tertiary); opacity: .45; }
      .tx-violet { fill: var(--color-accent-violet); }
      .tx-blue { fill: var(--color-accent-blue); }
      .tx-success { fill: var(--color-success); }
      .tx-halo { fill: color-mix(in srgb, var(--color-accent-violet) 9%, transparent); stroke: color-mix(in srgb, var(--color-accent-blue) 22%, transparent); stroke-dasharray: 3 6; }
      .tx-dashed { fill: none; stroke: var(--color-border-strong); stroke-width: 1.5; stroke-dasharray: 5 5; }
      .tx-ink { stroke: var(--color-bg-surface); stroke-width: 2.5; stroke-linecap: round; stroke-linejoin: round; fill: none; }
      .tx-float { animation: tx-float 5s ease-in-out infinite; }
      .tx-twinkle { transform-box: fill-box; transform-origin: center; animation: tx-twinkle 2.6s ease-in-out infinite; }
      .tx-pop { transform-box: fill-box; transform-origin: center; animation: tx-pop 3.2s cubic-bezier(.34,1.56,.64,1) infinite; }
      @keyframes tx-float { 0%,100% { transform: translateY(0); } 50% { transform: translateY(-4px); } }
      @keyframes tx-twinkle { 0%,100% { opacity: .25; transform: scale(.8); } 50% { opacity: .9; transform: scale(1.1); } }
      @keyframes tx-pop { 0%,15% { transform: scale(0); } 30% { transform: scale(1.15); } 40%,85% { transform: scale(1); } 100% { transform: scale(0); } }
      .tx-shimmer { animation: tx-shimmer 1.8s ease-in-out infinite; }
      .tx-coin { transform-box: fill-box; transform-origin: center; animation: tx-coin 3.6s cubic-bezier(.34,1.56,.64,1) infinite; }
      .tx-sync { transform-box: fill-box; transform-origin: center; animation: tx-spin 3s linear infinite; }
      @keyframes tx-shimmer { 0%,100% { opacity: .35; } 50% { opacity: 1; } }
      @keyframes tx-coin { 0%,10% { transform: translateY(-28px) scale(.6); opacity: 0; } 30%,80% { transform: translateY(0) scale(1); opacity: 1; } 100% { transform: translateY(0) scale(1); opacity: 0; } }
      @keyframes tx-spin { to { transform: rotate(360deg); } }
      @media (prefers-reduced-motion: reduce) { .tx-float, .tx-twinkle, .tx-pop, .tx-shimmer, .tx-coin, .tx-sync { animation: none !important; transform: none !important; opacity: 1 !important; } }
      </style>
      <circle class="tx-halo" cx="110" cy="84" r="70"/>
      <g class="tx-float">
        <path class="tx-surface" d="M62 28h78a8 8 0 0 1 8 8v104l-8-6-8 6-8-6-8 6-8-6-8 6-8-6-8 6-8-6-8 6-8-6V36a8 8 0 0 1 8-8z"/>
        <rect class="tx-line-strong" x="72" y="42" width="44" height="7" rx="3.5"/>
        <rect class="tx-line" x="72" y="55" width="28" height="5" rx="2.5"/>
        <g>
          <rect class="tx-shimmer tx-sunken" x="70" y="70" width="68" height="12" rx="4"/>
          <rect class="tx-shimmer tx-sunken" x="70" y="88" width="68" height="12" rx="4" style="animation-delay: .3s"/>
          <rect class="tx-shimmer tx-sunken" x="70" y="106" width="68" height="12" rx="4" style="animation-delay: .6s"/>
          <rect class="tx-line-strong" x="118" y="74" width="14" height="4" rx="2"/>
          <rect class="tx-line-strong" x="118" y="92" width="14" height="4" rx="2"/>
          <rect class="tx-line-strong" x="118" y="110" width="14" height="4" rx="2"/>
        </g>
      </g>
      <g class="tx-coin" style="animation-delay: .5s">
        <circle class="tx-violet" cx="160" cy="54" r="18"/>
        <circle cx="160" cy="54" r="12.5" style="stroke: color-mix(in srgb, var(--color-bg-surface) 55%, transparent); stroke-width: 1.5"/>
        <path class="tx-ink" d="M165 47a8 8 0 1 0 0 14M152 52h9M152 57h9" style="stroke-width: 2"/>
      </g>
      <g transform="translate(166 110)">
        <circle r="15" class="tx-surface"/>
        <g class="tx-sync">
          <path d="M-7 -2a7.5 7.5 0 0 1 13-4M7 2a7.5 7.5 0 0 1 -13 4" style="stroke: var(--color-accent-blue); stroke-width: 2.2; stroke-linecap: round"/>
          <path d="M6 -10v4h-4M-6 10v-4h4" style="stroke: var(--color-accent-blue); stroke-width: 2.2; stroke-linecap: round; stroke-linejoin: round"/>
        </g>
      </g>
      <path class="tx-twinkle" d="M40 118l2 5 5 2-5 2-2 5-2-5-5-2 5-2z" style="fill: var(--color-accent-violet)"/>
      <circle class="tx-twinkle" cx="44" cy="40" r="3" style="fill: var(--color-accent-blue); animation-delay: 1s"/>
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
    <svg viewBox="0 0 220 165" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <style>
      .tm-surface { fill: var(--color-bg-surface); stroke: var(--color-border-default); stroke-width: 1.5; }
      .tm-sunken { fill: var(--color-bg-surface-sunken); }
      .tm-line { fill: var(--color-border-default); }
      .tm-line-strong { fill: var(--color-text-tertiary); opacity: .45; }
      .tm-violet { fill: var(--color-accent-violet); }
      .tm-blue { fill: var(--color-accent-blue); }
      .tm-success { fill: var(--color-success); }
      .tm-halo { fill: color-mix(in srgb, var(--color-accent-violet) 9%, transparent); stroke: color-mix(in srgb, var(--color-accent-blue) 22%, transparent); stroke-dasharray: 3 6; }
      .tm-dashed { fill: none; stroke: var(--color-border-strong); stroke-width: 1.5; stroke-dasharray: 5 5; }
      .tm-ink { stroke: var(--color-bg-surface); stroke-width: 2.5; stroke-linecap: round; stroke-linejoin: round; fill: none; }
      .tm-float { animation: tm-float 5s ease-in-out infinite; }
      .tm-twinkle { transform-box: fill-box; transform-origin: center; animation: tm-twinkle 2.6s ease-in-out infinite; }
      .tm-pop { transform-box: fill-box; transform-origin: center; animation: tm-pop 3.2s cubic-bezier(.34,1.56,.64,1) infinite; }
      @keyframes tm-float { 0%,100% { transform: translateY(0); } 50% { transform: translateY(-4px); } }
      @keyframes tm-twinkle { 0%,100% { opacity: .25; transform: scale(.8); } 50% { opacity: .9; transform: scale(1.1); } }
      @keyframes tm-pop { 0%,15% { transform: scale(0); } 30% { transform: scale(1.15); } 40%,85% { transform: scale(1); } 100% { transform: scale(0); } }
      .tm-check { stroke: var(--color-bg-surface); stroke-width: 3; stroke-linecap: round; stroke-linejoin: round; stroke-dasharray: 24; stroke-dashoffset: 24; animation: tm-draw 3.6s ease-out infinite; }
      .tm-beam { transform-box: fill-box; transform-origin: center; animation: tm-balance 3.6s ease-in-out infinite; }
      .tm-ring { transform-box: fill-box; transform-origin: center; animation: tm-ring 2.4s ease-out infinite; fill: none; stroke: var(--color-success); stroke-width: 2; }
      @keyframes tm-draw { 0%,20% { stroke-dashoffset: 24; } 40%,90% { stroke-dashoffset: 0; } 100% { stroke-dashoffset: 24; } }
      @keyframes tm-balance { 0% { transform: rotate(-8deg); } 35%,100% { transform: rotate(0); } }
      @keyframes tm-ring { 0% { transform: scale(.8); opacity: .7; } 100% { transform: scale(1.5); opacity: 0; } }
      @media (prefers-reduced-motion: reduce) { .tm-float, .tm-twinkle, .tm-pop, .tm-beam, .tm-ring { animation: none !important; transform: none !important; opacity: 1 !important; } }
      @media (prefers-reduced-motion: reduce) { .tm-check { stroke-dashoffset: 0; animation: none; } }
      </style>
      <circle cx="110" cy="84" r="70" style="fill: color-mix(in srgb, var(--color-success) 8%, transparent); stroke: color-mix(in srgb, var(--color-success) 25%, transparent); stroke-dasharray: 3 6"/>
      <g class="tm-float">
        <rect class="tm-surface" x="40" y="42" width="92" height="92" rx="14"/>
        <circle class="tm-sunken" cx="68" cy="70" r="13"/>
        <circle cx="68" cy="66" r="5" style="fill: var(--color-text-tertiary); opacity: .6"/>
        <path d="M60 78c2-5 14-5 16 0" style="stroke: var(--color-text-tertiary); stroke-width: 2.5; stroke-linecap: round; opacity: .6"/>
        <rect class="tm-line-strong" x="88" y="62" width="32" height="6" rx="3"/>
        <rect class="tm-line" x="88" y="73" width="22" height="5" rx="2.5"/>
        <g transform="translate(86 112)">
          <path d="M-26 0h52" style="stroke: var(--color-border-strong); stroke-width: 2"/>
          <path d="M0 0v-8" style="stroke: var(--color-border-strong); stroke-width: 2"/>
          <g class="tm-beam">
            <path d="M-30 -10h60" style="stroke: var(--color-text-tertiary); stroke-width: 2.5; stroke-linecap: round"/>
            <circle cx="-26" cy="-10" r="4" class="tm-blue"/>
            <circle cx="26" cy="-10" r="4" class="tm-violet"/>
          </g>
        </g>
      </g>
      <g transform="translate(146 58)">
        <circle class="tm-ring" r="22"/>
        <path d="M0 -24l20 8v12c0 14-9 22-20 26c-11-4-20-12-20-26v-12z" class="tm-success"/>
        <path class="tm-check" d="M-9 1l6 6l12-12"/>
      </g>
      <circle class="tm-twinkle" cx="176" cy="116" r="3" style="fill: var(--color-success)"/>
      <path class="tm-twinkle" d="M30 30l2.5 6 6 2.5-6 2.5-2.5 6-2.5-6-6-2.5 6-2.5z" style="fill: var(--color-accent-violet); animation-delay: .8s"/>
      <circle class="tm-twinkle" cx="190" cy="34" r="2.5" style="fill: var(--color-accent-blue); animation-delay: 1.5s"/>
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

/**
 * The illustrations drawn as artwork rather than a monochrome glyph: animated, multi-colour, larger. They get
 * the art frame (no grey tint, no fade, a bigger box) instead of the icon treatment the others still use.
 */
const ARTWORK_KEYS = new Set(['plans-empty', 'payees-empty', 'transactions-empty', 'terminated-empty']);

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
  /** True when the illustration is one of the animated artworks (see ARTWORK_KEYS). */
  readonly isArtwork = (): boolean => ARTWORK_KEYS.has(this.illustration());

  readonly illustrationHtml = (): SafeHtml | null => {
    const key = this.illustration();
    if (!key || !ILLUSTRATIONS[key]) return null;
    return this.sanitizer.bypassSecurityTrustHtml(ILLUSTRATIONS[key]);
  };
}
