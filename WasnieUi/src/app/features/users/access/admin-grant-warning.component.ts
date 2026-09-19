import { Component, input } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { WsCardComponent } from '../../../shared/ui';

/**
 * The warning shown whenever an administrator is about to hand the administrator role to somebody
 * else — by changing their role or by inviting them as one.
 *
 * ★★ IT SAYS WHAT THE ROLE DOES, NOT THAT THE ROLE IS "POWERFUL" (§C3). TenantAdmin holds every
 * permission in `RolePermissions.cs`, so "everything you can do" is literally true, and the list names
 * the parts that cost the most to hand over by mistake: the people (including demoting YOU), the
 * money, and the bill. A vague "be careful" is the sentence people learn to click past.
 *
 * ★ ONE COMPONENT FOR BOTH PLACES. The invite form and the change-role modal ask the same question of
 * the same person; two copies of the wording would drift apart the first time one was edited.
 *
 * ★ A WARNING, NOT A BLOCK. Making a second administrator is legitimate — it is how the first one
 * leaves the company without stranding the workspace — so the screen informs and lets them decide.
 */
@Component({
  selector: 'app-admin-grant-warning',
  standalone: true,
  imports: [TranslateModule, WsCardComponent],
  templateUrl: './admin-grant-warning.component.html',
  styleUrl: './admin-grant-warning.component.scss',
})
export class AdminGrantWarningComponent {
  /** Who would receive the role. Null when it is not known yet (an invitation to an address). */
  readonly name = input<string | null>(null);
}
