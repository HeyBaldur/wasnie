import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map, startWith } from 'rxjs/operators';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { CurrentUserService } from '../../../core/auth/current-user.service';
import { SidebarStateService } from '../../../core/services/sidebar-state.service';
import { SidebarBadgesStore } from '../../../core/navigation/sidebar-badges.store';
import { SessionExitService } from '../../../core/services/session-exit.service';
import { IconComponent } from '../icon/icon.component';
import { HasPermissionDirective } from '../../directives/has-permission.directive';
import { SubscriptionStateService } from '../../../features/subscription/services/subscription-state.service';
import { HubSpotSyncBannerComponent } from '../../../features/integrations/components/hubspot-sync-banner/hubspot-sync-banner.component';
import { AssistantStore } from '../../../features/assistant/state/assistant.store';

interface NavItem {
  path: string;
  labelKey: string;
  icon: string;
  permission: string;
}

interface NavGroupEntry {
  type: 'group';
  key: string;
  labelKey: string;
  icon: string;
  permission: string;
  children: NavItem[];
}

type NavEntry = NavItem | NavGroupEntry;

interface NavSection {
  sectionKey: string;
  items: NavEntry[];
}

/** Separación entre el rail y el panel del submenú. El SCSS no puede saberla: la posición se calcula aquí. */
const FLYOUT_GAP_PX = 6;

/** Margen para cruzar ese hueco con el puntero antes de que el panel empiece a cerrarse. */
const FLYOUT_CLOSE_DELAY_MS = 160;

/**
 * Lo que dura la animación de salida, y por eso existe.
 *
 * ★★ SIN ESTO NO HAY SALIDA ANIMADA. Un `@if` desmonta el nodo en el acto: la animación de entrada se
 * ve y la de cierre no existe, porque el elemento que debería desvanecerse ya no está en el DOM. El
 * panel se queda montado con la clase `--leaving` durante estos milisegundos y recién entonces se
 * desmonta. Debe coincidir con la duración de la transición del SCSS.
 */
const FLYOUT_LEAVE_MS = 120;

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, TranslatePipe, IconComponent, HasPermissionDirective, HubSpotSyncBannerComponent],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
})
export class SidebarComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly currentUser = inject(CurrentUserService);
  private readonly router = inject(Router);
  readonly sidebarState = inject(SidebarStateService);
  private readonly subscriptionState = inject(SubscriptionStateService);
  private readonly badgesStore = inject(SidebarBadgesStore);
  private readonly sessionExit = inject(SessionExitService);

  // ─── El submenú de un grupo (Financials) ──────────────────────────────────────────────────────
  //
  // ★★ FLYOUT AL HOVER, NO ACORDEÓN. El grupo ya no empuja el resto del rail hacia abajo: sus hijos
  // salen en un panel al costado. Eso hace que el estado deje de tener que sobrevivir a la navegación
  // (antes vivía en `SidebarGroupsService` porque el sidebar se reconstruye en cada navegación y un
  // acordeón abierto se cerraba de golpe). Un panel de hover es efímero por definición: se abre con
  // el puntero, se cierra al elegir. Por eso el estado vuelve a ser LOCAL, y por eso ya no hay efecto
  // de auto-expandir — reabrir el panel solo porque la ruta activa está dentro del grupo dejaría un
  // menú abierto que nadie pidió.
  //
  // ★ POSICIÓN FIJA, CALCULADA DEL TRIGGER. `.sidebar` tiene `overflow: hidden` y `.sidebar__scroll`
  // `overflow-y: auto`: un panel `absolute` quedaría recortado por ambos. Fijo respecto al viewport no
  // lo recorta nadie, a cambio de tener que anclarlo a mano y de cerrarlo cuando algo scrollea.
  private readonly flyoutKey = signal<string | null>(null);
  readonly flyoutTop = signal(0);
  readonly flyoutLeft = signal(0);

  /** El panel sigue montado, pero ya se está yendo: es el estado que hace posible la animación de salida. */
  readonly flyoutLeaving = signal(false);

  // ★ EL CIERRE ES DIFERIDO, y sin esto el menú es inusable. Entre el rail y el panel hay un hueco de
  // unos pocos píxeles: al cruzarlo el puntero pasa por encima del sidebar, que NO es descendiente del
  // `li`, y dispara su `mouseleave`. Un cierre inmediato mataría el panel justo cuando el usuario va
  // hacia él. El temporizador da el margen para llegar; entrar al panel lo cancela.
  private hideTimer: ReturnType<typeof setTimeout> | null = null;
  private leaveTimer: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    // ★ STARTED HERE, NOT ON EVERY NAVIGATION. The sidebar is built once per session; the store loads
    // the counts now and refreshes them on its own slow timer, or when a screen says a count changed.
    this.badgesStore.start();
  }

  /**
   * The number to draw beside a nav path, or null for none.
   *
   * ★ AN EXPLICIT MAP, NOT A LOOKUP BUILT FROM THE PATH (§C2, the same rule as the translation
   * whitelist). A route this build has never heard of gets no badge, rather than an undefined read
   * silently rendering nothing while looking like it works.
   */
  badgeFor(path: string): number | null {
    switch (path) {
      case '/reconciliation': return this.badgesStore.reconciliation();
      case '/terminated-accounts': return this.badgesStore.terminatedAccounts();
      default: return null;
    }
  }

  /** The group row's own badge: the sum of what this user can see, and nothing when it is zero. */
  groupBadgeFor(key: string): number | null {
    return key === 'pay-financials' ? this.badgesStore.financialsTotal() : null;
  }

  // Reads from the same root-singleton already loaded by AppShellComponent.
  // To revert logo gradient: remove the @if overlay blocks in the template.
  readonly tierName = computed(() => this.subscriptionState.subscription()?.tier ?? null);

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      map(() => this.router.url.split('?')[0]),
      startWith(this.router.url.split('?')[0]),
    ),
    { initialValue: this.router.url.split('?')[0] },
  );


  constructor() {
    // ★ EN CAPTURA, Y SOBRE `window`. El panel está anclado a coordenadas de viewport, así que
    // cualquier scroll lo deja flotando lejos de su fila. El scroll NO burbujea y quien scrollea aquí
    // es `.sidebar__scroll` (o la página), no `window`: sin `capture: true` este listener no se entera
    // nunca. Mismo motivo por el que el menú ⋮ de los listados escucha así.
    const dismiss = (): void => {
      if (this.flyoutKey() !== null) this.closeFlyout();
    };
    window.addEventListener('scroll', dismiss, { capture: true, passive: true });
    window.addEventListener('resize', dismiss);

    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('scroll', dismiss, { capture: true });
      window.removeEventListener('resize', dismiss);
      this.clearTimers();
    });
  }

  isNavGroup(entry: NavEntry): entry is NavGroupEntry {
    return (entry as NavGroupEntry).type === 'group';
  }

  isFlyoutOpen(key: string): boolean {
    return this.flyoutKey() === key;
  }

  /**
   * Abre el panel del grupo junto a su fila. Idempotente: re-entrar sólo reancla y cancela el cierre.
   *
   * ★ VOLVER SOBRE UN PANEL QUE SE IBA LO TRAE DE VUELTA, no lo reinicia. Se limpia `--leaving` y el
   * panel vuelve a su sitio con la transición de la clase; desmontarlo y remontarlo para relanzar la
   * animación de entrada daría un parpadeo justo cuando el usuario corrigió el rumbo.
   */
  openFlyout(key: string, trigger: HTMLElement): void {
    this.clearTimers();
    this.flyoutLeaving.set(false);
    const rect = trigger.getBoundingClientRect();
    this.flyoutTop.set(rect.top);
    this.flyoutLeft.set(rect.right + FLYOUT_GAP_PX);
    this.flyoutKey.set(key);
  }

  /** El click sobre la fila del grupo: para el táctil y el teclado, donde no hay hover. */
  toggleFlyout(key: string, trigger: HTMLElement): void {
    if (this.isFlyoutOpen(key)) this.closeFlyout();
    else this.openFlyout(key, trigger);
  }

  /** Cierre inmediato (elegir una opción, Escape, scroll): arranca la salida, no desmonta de golpe. */
  closeFlyout(): void {
    this.clearTimers();
    this.startLeaving();
  }

  /** Ver la nota de `hideTimer`: el hueco entre el rail y el panel dispara `mouseleave`. */
  scheduleCloseFlyout(): void {
    if (this.hideTimer) clearTimeout(this.hideTimer);
    this.hideTimer = setTimeout(() => {
      this.hideTimer = null;
      this.startLeaving();
    }, FLYOUT_CLOSE_DELAY_MS);
  }

  private startLeaving(): void {
    if (this.flyoutKey() === null) return;
    this.flyoutLeaving.set(true);
    this.leaveTimer = setTimeout(() => {
      this.leaveTimer = null;
      this.flyoutKey.set(null);
      this.flyoutLeaving.set(false);
    }, FLYOUT_LEAVE_MS);
  }

  private clearTimers(): void {
    if (this.hideTimer) {
      clearTimeout(this.hideTimer);
      this.hideTimer = null;
    }
    if (this.leaveTimer) {
      clearTimeout(this.leaveTimer);
      this.leaveTimer = null;
    }
  }

  isGroupActive(children: NavItem[]): boolean {
    return children.some(c => this.isNavActive(c.path));
  }

  isNavActive(path: string): boolean {
    const url = this.currentUrl();
    if (path === '/transactions') {
      // Don't highlight Transactions when the import sub-route is active
      return url.startsWith('/transactions') && !url.startsWith('/transactions/import');
    }
    return url === path || url.startsWith(path + '/');
  }

  /**
   * The assistant's entitlement, for the rail's own entry.
   *
   * ★★ IT CANNOT RIDE ON `*hasPermission` LIKE EVERY OTHER ITEM, and that is the whole reason
   * this entry is not in `navSections`. Access to the assistant is an ENTITLEMENT — a seat plus a paid
   * plan — not a role permission, and it is decided by the server. Inventing a permission string for it
   * would either show the link to people whose first click gets a 403, or hide it from people who have
   * paid for it; both are worse than one special case that reads the real gate.
   *
   * ★ HIDE, DO NOT DISABLE (Spec §5b.6): while the answer is unknown the entry renders nothing at
   * all rather than flashing a control the user may not be entitled to. Same rule the topbar trigger
   * follows, and the same signal, so the two cannot disagree.
   */
  readonly assistant = inject(AssistantStore);

  readonly navSections: NavSection[] = [
    {
      sectionKey: 'NAV.SECTION_OVERVIEW',
      items: [{ path: '/dashboard', labelKey: 'NAV.DASHBOARD', icon: 'dashboard', permission: 'Payees.Read' }],
    },
    {
      sectionKey: 'NAV.SECTION_SETUP',
      items: [
        { path: '/plans', labelKey: 'NAV.PLANS', icon: 'plans', permission: 'Plans.Read' },
        { path: '/quotas', labelKey: 'NAV.QUOTAS', icon: 'target', permission: 'Quotas.Read' },
        { path: '/payees', labelKey: 'NAV.PAYEES', icon: 'users', permission: 'Payees.Read' },
        { path: '/assignments', labelKey: 'NAV.ASSIGNMENTS', icon: 'user-check', permission: 'Assignments.Read' },
        { path: '/category-mappings', labelKey: 'NAV.CATEGORY_MAPPINGS', icon: 'tag', permission: 'CategoryMappings.Read' },
      ],
    },
    {
      sectionKey: 'NAV.SECTION_OPERATIONS',
      items: [
        { path: '/transactions', labelKey: 'NAV.TRANSACTIONS', icon: 'arrows-exchange', permission: 'Transactions.Read' },
        { path: '/credits', labelKey: 'NAV.CREDITS', icon: 'receipt', permission: 'Credits.Read' },
        {
          type: 'group',
          key: 'pay-financials',
          labelKey: 'NAV.PAY_GROUP',
          icon: 'coin',
          permission: 'Reports.ViewAll',
          children: [
            { path: '/pay-runs', labelKey: 'NAV.PAY_RUNS', icon: 'coin', permission: 'Reports.ViewAll' },
            { path: '/payouts', labelKey: 'NAV.PAYOUTS', icon: 'layers', permission: 'Reports.ViewAll' },
            { path: '/reconciliation', labelKey: 'NAV.RECONCILIATION', icon: 'alert-triangle', permission: 'Reports.ViewAll' },
            { path: '/terminated-accounts', labelKey: 'NAV.TERMINATED_ACCOUNTS', icon: 'users', permission: 'Ledger.Read' },
          ],
        },
      ],
    },
  ];

  // The manual is NOT in this menu. It moved to the topbar, beside the user: it is help, not a place in
  // the product's navigation, and it sat oddly among Subscription / Integrations / Settings.
  readonly subscriptionItem: NavItem ={ path: '/subscription', labelKey: 'NAV.SUBSCRIPTION', icon: 'brand-stripe', permission: 'Subscription.Manage' };
  readonly integrationsItem: NavItem = { path: '/integrations', labelKey: 'NAV.INTEGRATIONS', icon: 'link-2', permission: 'Integrations.Manage' };
  readonly settingsItem: NavItem = { path: '/admin', labelKey: 'NAV.ADMIN', icon: 'settings', permission: 'Subscription.Manage' };

  /**
   * ★★ SALE RECARGANDO, NO NAVEGANDO. Vaciar el token no vacía la aplicación: los servicios de raíz
   * siguen vivos con los datos del tenant anterior — este mismo sidebar mostraba el contador de otra
   * empresa después de cambiar de sesión. Ver `SessionExitService`.
   */
  logout(): void {
    this.currentUser.clear();
    this.authService.logout();
    this.sessionExit.toLogin();
  }
}
