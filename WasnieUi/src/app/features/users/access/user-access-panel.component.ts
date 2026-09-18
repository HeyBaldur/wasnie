import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { WsBadgeComponent, WsButtonComponent, WsCardComponent, WsGaugeComponent } from '../../../shared/ui';
import type { GaugeAccent } from '../../../shared/ui/ws-gauge/ws-gauge.component';
import { TenantUser, roleTranslationKey } from '../models/user.model';
import { ACCESS_AREAS, AccessArea, TOTAL_CAPABILITIES } from './access-map';

/** One area with its capabilities already resolved against this person's permissions. */
export interface ResolvedArea {
  readonly key: string;
  readonly titleKey: string;
  readonly icon: string;
  readonly granted: number;
  readonly total: number;
  readonly capabilities: readonly { labelKey: string; allowed: boolean }[];
}

/**
 * ★★ EIGHT ROWS INSTEAD OF THIRTY-ONE. The first version stacked every capability of every area,
 * which came to roughly 1,300px of panel — a list somebody has to scroll rather than a summary they can
 * read. Each area is now ONE row carrying a meter, and the named lines open only for the area being
 * asked about.
 *
 * ★ THE METER IS THE DETAIL, NOT DECORATION. One segment per capability, filled or not, so "1 of 5"
 * and "5 of 5" are told apart without reading either number — which is the whole job of this panel at
 * a glance, before anybody expands anything.
 */

/**
 * What one person can and cannot do, next to the four things an administrator can do about it.
 *
 * ★★ THE SCREEN USED TO ANSWER NEITHER QUESTION. The users table showed a role NAME — "Rep",
 * "CompManager" — and nothing else, so the one decision this page exists for (should this person hold
 * this role?) had to be made from a word. The four actions lived behind a ⋮ that had to be opened to
 * be read. An administrator could not find out what a role meant without reading the C# source.
 *
 * ★★ EVERY ANSWER COMES FROM THE SERVER. `permissions` is `RolePermissions.cs` over HTTP; this
 * component decides only the wording and the order (`access-map.ts`). Nothing here knows which role
 * holds what, and that is deliberate: a component that did would be a second authority on
 * authorisation, and it would be wrong the first time a permission moved.
 *
 * ★★ "NOT LOADED" IS NOT "DENIED". With the map missing, every line would render as a red cross and
 * the panel would state, confidently, that this person can do nothing. That is the false zero on a
 * screen about access, so `rolesLoaded` gets its own branch and its own sentence.
 *
 * ★ THE DENIED LINES ARE SHOWN, NOT HIDDEN. "What they cannot do" was half the question, and a list
 * of only the granted ones cannot answer it — an administrator comparing two roles needs to see the
 * gap, not infer it from a shorter list.
 */
@Component({
  selector: 'app-user-access-panel',
  standalone: true,
  imports: [
    TranslateModule,
    IconComponent,
    WsBadgeComponent,
    WsButtonComponent,
    WsCardComponent,
    WsGaugeComponent,
  ],
  templateUrl: './user-access-panel.component.html',
  styleUrl: './user-access-panel.component.scss',
})
export class UserAccessPanelComponent {
  readonly user = input.required<TenantUser>();
  readonly permissions = input.required<ReadonlySet<string>>();
  readonly rolesLoaded = input.required<boolean>();
  /** Whether the reader is looking at their own row — the three authority actions are hidden then. */
  readonly isSelf = input.required<boolean>();
  /** Users.Manage. Without it the panel is a read-only explanation, which is still worth opening. */
  readonly canManage = input.required<boolean>();

  readonly closed = output<void>();
  readonly linkPayee = output<TenantUser>();
  readonly unlinkPayee = output<TenantUser>();
  readonly changeRole = output<TenantUser>();
  readonly deactivate = output<TenantUser>();
  readonly reactivate = output<TenantUser>();
  readonly remove = output<TenantUser>();

  readonly totalCapabilities = TOTAL_CAPABILITIES;

  readonly areas = computed<ResolvedArea[]>(() => {
    const held = this.permissions();

    return ACCESS_AREAS.map((area: AccessArea) => {
      // ANY of the listed keys grants it — see AccessCapability for why it is a list.
      const capabilities = area.capabilities.map((c) => ({
        labelKey: c.labelKey,
        allowed: c.permissions.some((key) => held.has(key)),
      }));

      return {
        key: area.key,
        titleKey: area.titleKey,
        icon: area.icon,
        granted: capabilities.filter((c) => c.allowed).length,
        total: capabilities.length,
        capabilities,
      };
    });
  });

  readonly grantedCount = computed(() =>
    this.areas().reduce((n, a) => n + a.granted, 0),
  );

  /**
   * How full the ring is drawn, 0–1.
   *
   * ★ IT IS A SHAPE, NOT A SCORE. A Rep at 6 of 31 is not "38% of an administrator" and the panel
   * never prints a percentage — the figure underneath is "6 of 31", which is countable and checkable.
   * The ring exists so two roles can be told apart at a glance before either list is read.
   */
  readonly coverage = computed(() =>
    this.totalCapabilities === 0 ? 0 : this.grantedCount() / this.totalCapabilities,
  );

  /**
   * ★★ A BAND, NOT A VERDICT — which is why this never passes `attainment` to the gauge. That ladder
   * climbs danger → warning → success and is right for a quota; here it would paint a rep at 5 of 31
   * RED, which reads as "this role is broken" about a role that is doing its job. Wide authority is
   * not good and narrow authority is not bad; the three colours only separate them at a glance.
   */
  readonly ringAccent = computed<GaugeAccent>(() => {
    const c = this.coverage();
    if (c > 0.66) return 'brand';
    if (c > 0.25) return 'blue';
    return 'violet';
  });

  /** What a screen reader hears instead of the ring: the count, in words it can use. */
  readonly ringAriaLabel = computed(() =>
    this.rolesLoaded()
      ? `${this.grantedCount()} / ${this.totalCapabilities}`
      : '');

  readonly roleKey = computed(() => roleTranslationKey(this.user().role));

  /**
   * Which area's named capabilities are open, or null.
   *
   * ★ ONE AT A TIME. Letting several open at once rebuilds the scroll this redesign exists to remove;
   * an administrator is comparing ONE area against a role, not reading the whole map.
   */
  readonly expandedArea = signal<string | null>(null);

  toggleArea(key: string): void {
    this.expandedArea.update((open) => (open === key ? null : key));
  }

  /**
   * ★ THE OPEN AREA RESETS WHEN THE PERSON DOES. The panel stays mounted while the selected row
   * changes, so without this the second user inherits whatever was expanded for the first — which looks
   * harmless and is how somebody reads one person's permissions under another person's name.
   */
  constructor() {
    effect(() => {
      this.user().userId;
      untracked(() => this.expandedArea.set(null));
    });
  }

  initials(user: TenantUser): string {
    const first = user.firstName?.trim()?.[0] ?? '';
    const last = user.lastName?.trim()?.[0] ?? '';
    const pair = `${first}${last}`.toUpperCase();
    return pair || (user.email[0]?.toUpperCase() ?? '?');
  }

  displayName(user: TenantUser): string {
    const full = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
    return full || user.email;
  }
}
