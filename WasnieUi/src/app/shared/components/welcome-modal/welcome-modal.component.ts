import { Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { WsModalComponent, WsButtonComponent, WsVideoComponent } from '../../ui';
import { IconComponent } from '../icon/icon.component';
import { WelcomeService } from '../../../core/services/welcome.service';

/**
 * The first-run product tour: a video and a short account of what Incentra does.
 *
 * ★ IT RENDERS WHEREVER IT IS PLACED BUT OPENS FROM ANYWHERE — the state is in WelcomeService, so the
 * shell can host it while the /manual screen re-opens it, without either knowing about the other.
 *
 * ★ THE SAME MODAL FOR EVERY TENANT, by decision: no plan-dependent copy, no branching. The capability
 * list therefore describes what the PRODUCT does, including the assistant, and offers no control that
 * a Free workspace would be refused.
 */
@Component({
  selector: 'app-welcome-modal',
  standalone: true,
  imports: [TranslatePipe, WsModalComponent, WsButtonComponent, WsVideoComponent, IconComponent],
  templateUrl: './welcome-modal.component.html',
  styleUrl: './welcome-modal.component.scss',
})
export class WelcomeModalComponent {
  readonly welcome = inject(WelcomeService);

  /**
   * Four capabilities, in the order someone actually meets them: build the plan, set the targets,
   * bring the data in, then ask what it all means.
   */
  readonly capabilities = [
    { icon: 'plans',  titleKey: 'WELCOME.CAP_PLANS_TITLE',     descKey: 'WELCOME.CAP_PLANS_DESC'     },
    { icon: 'target', titleKey: 'WELCOME.CAP_QUOTAS_TITLE',    descKey: 'WELCOME.CAP_QUOTAS_DESC'    },
    { icon: 'upload', titleKey: 'WELCOME.CAP_DATA_TITLE',      descKey: 'WELCOME.CAP_DATA_DESC'      },
    // 'zap' rather than a sparkles glyph — the icon set has no sparkles, and adding one to the design
    // system is a separate decision (DESIGN_SYSTEM 10.3), not something to slip in mid-feature.
    { icon: 'zap',    titleKey: 'WELCOME.CAP_ASSISTANT_TITLE', descKey: 'WELCOME.CAP_ASSISTANT_DESC' },
  ] as const;
}
