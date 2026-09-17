import { Routes } from '@angular/router';
import { planGuard } from './core/guards/plan.guard';
import { hasPermissionGuard } from './core/auth/guards/has-permission.guard';
import { subscriptionGuard } from './core/guards/subscription.guard';
import { authGuard } from './core/guards/auth.guard';
import { companyDashboardGuard, landingRedirect } from './core/guards/landing.guard';
import { environment } from '../environments/environment';

export const routes: Routes = [
  {
    // KAN-92: the landing depends on what the person can see. See landingRedirect.
    path: '',
    redirectTo: landingRedirect,
    pathMatch: 'full',
  },
  {
    path: 'auth',
    title: 'AUTH.LOGIN',
    loadChildren: () =>
      import('./features/auth/auth.routes').then((m) => m.authRoutes),
  },
  {
    path: 'onboarding',
    title: 'NAV.ONBOARDING',
    loadChildren: () =>
      import('./features/subscription/subscription.routes').then((m) => m.subscriptionRoutes),
  },
  {
    // El recorrido guiado: entrada permanente, no sólo la primera vez. Lleva los mismos guardas que
    // el resto del producto — practica dentro de la empresa del usuario, con su sesión.
    path: 'guided-tour',
    title: 'GUIDED.TITLE',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/guided-tour/guided-tour.component').then(
        (m) => m.GuidedTourComponent
      ),
  },
  {
    path: 'dashboard',
    title: 'NAV.DASHBOARD',
    canActivate: [planGuard, subscriptionGuard, companyDashboardGuard],
    loadComponent: () =>
      import('./features/dashboard/dashboard.component').then(
        (m) => m.DashboardComponent
      ),
  },
  {
    // The personal dashboard (KAN-92). No permission guard: it shows the caller their OWN figures and
    // the server resolves whose they are from the token, so there is nothing here to be entitled to.
    path: 'my-dashboard',
    title: 'NAV.MY_DASHBOARD',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/my-dashboard/my-dashboard.component').then(
        (m) => m.MyDashboardComponent
      ),
  },
  {
    path: 'plans',
    title: 'NAV.PLANS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Plans.Read')],
    loadChildren: () =>
      import('./features/plans/plans.routes').then((m) => m.plansRoutes),
  },
  {
    path: 'payees',
    title: 'NAV.PAYEES',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Payees.Read')],
    loadChildren: () =>
      import('./features/payees/payees.routes').then((m) => m.payeesRoutes),
  },
  {
    path: 'transactions',
    title: 'NAV.TRANSACTIONS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Transactions.Read')],
    loadChildren: () =>
      import('./features/transactions/transactions.routes').then((m) => m.transactionsRoutes),
  },
  {
    path: 'credits',
    title: 'NAV.CREDITS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Credits.Read')],
    loadChildren: () =>
      import('./features/credits/credits.routes').then((m) => m.creditsRoutes),
  },
  {
    path: 'pay-runs',
    title: 'NAV.PAY_RUNS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Payouts.Read')],
    loadChildren: () =>
      import('./features/pay-runs/pay-runs.routes').then((m) => m.payRunsRoutes),
  },
  {
    // The Reconciliation Centre. Reports.ViewAll, the same permission the Financials group carries:
    // it is a finance-wide view of money, not a per-payee record.
    path: 'reconciliation',
    title: 'NAV.RECONCILIATION',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Reports.ViewAll')],
    loadChildren: () =>
      import('./features/reconciliation/reconciliation.routes').then((m) => m.reconciliationRoutes),
  },
  {
    // The audit trail (KAN-19). Audit.Read, NOT Reports.ViewAll: this is a record of people — who
    // signed in, whose permission was denied, which admin changed what and from where — rather than
    // a view of money. The guard is what makes hiding the menu entry more than decoration: without
    // it the page would still answer to a typed URL.
    path: 'audit-logs',
    title: 'NAV.AUDIT_LOGS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Audit.Read')],
    loadChildren: () =>
      import('./features/audit-logs/audit-logs.routes').then((m) => m.auditLogsRoutes),
  },
  {
    // KAN-32. Users.Read, not Users.Manage: a CompManager may see the roster and cannot change it.
    // The guard is what makes hiding the menu entry more than decoration — without it a typed URL
    // would still render a page full of colleagues' addresses.
    path: 'users',
    title: 'NAV.USERS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Users.Read')],
    loadChildren: () =>
      import('./features/users/users.routes').then((m) => m.usersRoutes),
  },
  {
    path: 'payouts',
    title: 'NAV.PAYOUTS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Payouts.Read')],
    loadChildren: () =>
      import('./features/payouts/payouts.routes').then((m) => m.payoutsRoutes),
  },
  {
    // Finance's work queue: people who left with an account still open. Ledger.Read to see it;
    // closing an account needs Ledger.Adjust and happens on the payee's own ledger.
    path: 'terminated-accounts',
    title: 'LEDGER.TERMINATED_TITLE',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Ledger.Read')],
    loadChildren: () =>
      import('./features/ledger/ledger.routes').then((m) => m.ledgerRoutes),
  },
  {
    path: 'quotas',
    title: 'NAV.QUOTAS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Quotas.Read')],
    loadChildren: () =>
      import('./features/quotas/quotas.routes').then((m) => m.quotasRoutes),
  },
  {
    path: 'assignments',
    title: 'NAV.ASSIGNMENTS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Assignments.Read')],
    loadChildren: () =>
      import('./features/assignments/assignments.routes').then((m) => m.assignmentsRoutes),
  },
  {
    path: 'category-mappings',
    title: 'NAV.CATEGORY_MAPPINGS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('CategoryMappings.Read')],
    loadChildren: () =>
      import('./features/category-mappings/category-mappings.routes').then((m) => m.categoryMappingsRoutes),
  },
  // ── KAN-77: Billing and Pricing are two places now ─────────────────────────────────────────────
  // Pricing: what the plan costs, for the trial that decides to pay. Visible to every signed-in user; only
  // Subscription.Manage sees the button (hidden, not disabled).
  {
    path: 'pricing',
    title: 'NAV.PRICING',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/subscription/pricing/pricing.component').then((m) => m.PricingComponent),
  },
  // The paywall: authentication only. planGuard/subscriptionGuard would send a locked account right back here.
  {
    path: 'billing/paywall',
    title: 'PAYWALL.PAGE_TITLE',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/subscription/paywall/paywall.component').then((m) => m.PaywallComponent),
  },
  // Manage billing, in Settings: the customer who already pays.
  {
    path: 'billing',
    title: 'NAV.MANAGE_BILLING',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Subscription.Manage')],
    loadComponent: () =>
      import('./features/subscription/billing/manage-billing.component').then((m) => m.ManageBillingComponent),
  },
  // Old addresses keep working: Stripe portal sessions, bookmarks and e-mails still point at them.
  { path: 'subscription/reactivate', redirectTo: 'billing/paywall', pathMatch: 'full' },
  { path: 'subscription', redirectTo: 'billing', pathMatch: 'full' },
  {
    path: 'profile',
    title: 'NAV.PROFILE',
    canActivate: [planGuard, subscriptionGuard],
    loadChildren: () =>
      import('./features/profile/profile.routes').then(m => m.profileRoutes),
  },
  {
    path: 'profile/confirm-email-change',
    title: 'NAV.PROFILE',
    loadComponent: () =>
      import('./features/profile/confirm-email-change/confirm-email-change.component').then(
        m => m.ConfirmEmailChangeComponent,
      ),
  },
  {
    path: 'admin',
    title: 'NAV.ADMIN',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Subscription.Manage')],
    loadComponent: () =>
      import('./features/admin/admin.component').then(
        (m) => m.AdminComponent
      ),
  },
  {
    path: 'integrations',
    title: 'NAV.INTEGRATIONS',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Integrations.Manage')],
    loadComponent: () =>
      import('./features/integrations/integrations.component').then(
        (m) => m.IntegrationsComponent
      ),
  },
  {
    path: 'integrations/hubspot/owners',
    title: 'INTEGRATIONS.HUBSPOT.OWNERS.TITLE',
    canActivate: [planGuard, subscriptionGuard, hasPermissionGuard('Integrations.Manage')],
    loadComponent: () =>
      import('./features/integrations/owner-mapping/owner-mapping.component').then(
        (m) => m.CrmOwnerMappingComponent
      ),
  },
  {
    // The user manual. NO hasPermissionGuard on purpose: the manual documents the product and holds no
    // tenant data, so a valid session is the whole gate — hiding the instructions from the users with
    // the fewest rights would be exactly backwards. The guards it DOES carry are the standard pair, so
    // an unauthenticated visit lands on /auth/login like every other screen.
    path: 'manual',
    title: 'NAV.MANUAL',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/manual/manual.component').then((m) => m.ManualComponent),
  },
  {
    // The assistant with room to read. Same guard pair as /manual: a valid session and an active
    // subscription are the gate, and the ENTITLEMENT (the assistant is not part of every plan) is
    // answered by the page itself — there is no assistant guard, and a route that redirected on a
    // signal still loading would bounce the user off their own bookmark.
    //
    // Two paths, one component: the bare route shows the welcome, and :conversationId is what lets a
    // refresh come back to the same thread instead of an empty chat.
    path: 'assistant',
    title: 'ASSISTANT.TITLE',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/assistant/page/assistant-page.component').then((m) => m.AssistantPageComponent),
  },
  {
    path: 'assistant/:conversationId',
    title: 'ASSISTANT.TITLE',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/assistant/page/assistant-page.component').then((m) => m.AssistantPageComponent),
  },
  {
    path: 'forbidden',
    title: 'ERRORS.FORBIDDEN_TITLE',
    canActivate: [planGuard, subscriptionGuard],
    loadComponent: () =>
      import('./features/forbidden/forbidden.component').then(
        (m) => m.ForbiddenComponent
      ),
  },
  ...(!environment.production
    ? [
        {
          path: '__design-system',
          loadComponent: () =>
            import('./features/design-system/design-system.component').then(
              (m) => m.DesignSystemComponent
            ),
        },
      ]
    : []),
  {
    path: '**',
    redirectTo: 'dashboard',
  },
];
