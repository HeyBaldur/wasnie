import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsButtonComponent } from '../../../shared/ui';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { HasPermissionPipe } from '../../../shared/pipes/has-permission.pipe';

/**
 * The assignments empty state: an assignment is nothing but the link between a payee and a plan, so with
 * neither in place the create form is two empty pickers. The card explains what the link is for and the
 * order to get there, with a shortcut to each screen.
 *
 * Same family as the plan lock intro and the quota prerequisites card, but deliberately a different
 * composition — a wide stage on top (the two pieces snap together like chain links) and a horizontal step
 * timeline below — so the empty states do not read as one repeated template. It replaces the old looping
 * clip (`/videos/assignment.mp4`).
 *
 * ★ THE COPY DOES NOT MENTION PLAN STATUS — the backend accepts Draft plans on purpose, while the create
 * form's picker currently lists only Active ones. Until those two agree, the card promises neither.
 */
@Component({
  selector: 'app-assignment-prereq-intro',
  standalone: true,
  imports: [RouterLink, TranslatePipe, WsButtonComponent, IconComponent, HasPermissionPipe],
  templateUrl: './assignment-prereq-intro.component.html',
  styleUrl: './assignment-prereq-intro.component.scss',
})
export class AssignmentPrereqIntroComponent {
  readonly steps = [
    { icon: 'user', route: '/payees', title: 'ASSIGNMENTS.PREREQ_INTRO.STEP_PAYEE_TITLE', desc: 'ASSIGNMENTS.PREREQ_INTRO.STEP_PAYEE_DESC' },
    { icon: 'plans', route: '/plans', title: 'ASSIGNMENTS.PREREQ_INTRO.STEP_PLAN_TITLE', desc: 'ASSIGNMENTS.PREREQ_INTRO.STEP_PLAN_DESC' },
    { icon: 'arrow-right-left', route: null, title: 'ASSIGNMENTS.PREREQ_INTRO.STEP_LINK_TITLE', desc: 'ASSIGNMENTS.PREREQ_INTRO.STEP_LINK_DESC' },
  ] as const;
}
