import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';

/**
 * What creating a plan commits you to, said before you create it: once a payout is calculated under a
 * plan, it is locked — and why, and what to do when something must change (clone it as a new version).
 *
 * Built on the celebration pattern, card form (DESIGN_SYSTEM → Pattern — celebration / welcome modal).
 * It replaces the old "immutability notice", whose copy it keeps: the rule itself did not change, only
 * how it is told.
 *
 * ★ PERMANENT — it sits in its own column beside the form and cannot be dismissed: the rule applies to
 * every plan created, not only the first one.
 */
@Component({
  selector: 'app-plan-lock-intro',
  standalone: true,
  imports: [TranslatePipe, IconComponent],
  templateUrl: './plan-lock-intro.component.html',
  styleUrl: './plan-lock-intro.component.scss',
})
export class PlanLockIntroComponent {
  readonly points = [
    { icon: 'shield-check', key: 'PLANS.LOCK_INTRO.POINT_LOCKED' },
    { icon: 'alert-triangle', key: 'PLANS.LOCK_INTRO.POINT_WHY' },
    { icon: 'plans', key: 'PLANS.LOCK_INTRO.POINT_CLONE' },
  ] as const;
}
