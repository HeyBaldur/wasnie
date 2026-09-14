import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent } from '../../../shared/ui';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';

/**
 * The quotas empty state: a quota cannot exist on its own — it is set for a payee inside a plan — so
 * someone landing here with neither would hit a form with two empty pickers. The card tells the order
 * (payee → plan → quota) and links straight to each screen.
 *
 * Built on the same card as the plan lock intro (DESIGN_SYSTEM → Pattern — celebration / welcome modal,
 * card form). It replaces the old looping clip (`/videos/quotas.mp4`).
 *
 * ★ THE PLAN STEP NAMES THE ASSIGNMENT — the create form accepts a payee with no active assignment to the
 * plan, but that quota measures no attainment until the assignment exists. The card says so up front.
 */
@Component({
  selector: 'app-quota-prereq-intro',
  standalone: true,
  imports: [RouterLink, TranslatePipe, WsButtonComponent, IconComponent, HasPermissionPipe],
  templateUrl: './quota-prereq-intro.component.html',
  styleUrl: './quota-prereq-intro.component.scss',
})
export class QuotaPrereqIntroComponent {
  readonly steps = [
    { route: '/payees', title: 'QUOTAS.PREREQ_INTRO.STEP_PAYEE_TITLE', desc: 'QUOTAS.PREREQ_INTRO.STEP_PAYEE_DESC' },
    { route: '/plans', title: 'QUOTAS.PREREQ_INTRO.STEP_PLAN_TITLE', desc: 'QUOTAS.PREREQ_INTRO.STEP_PLAN_DESC' },
    { route: null, title: 'QUOTAS.PREREQ_INTRO.STEP_QUOTA_TITLE', desc: 'QUOTAS.PREREQ_INTRO.STEP_QUOTA_DESC' },
  ] as const;
}
