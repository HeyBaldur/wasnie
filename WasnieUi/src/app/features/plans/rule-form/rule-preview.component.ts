import { Component, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe, LowerCasePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { CurrencyFormatPipe } from '../../../shared/pipes/currency-format.pipe';
import { WsBadgeComponent, WsInputComponent } from '../../../shared/ui';
import { RuleDefinitionForm } from './rule-definition-form';

/**
 * Live Preview and the simulator: what the rule on screen is, and what it would pay.
 *
 * ★ THE SAME CARD ON BOTH SCREENS. In the guided tour this is the half that answers the question
 * people come to the sandbox with — "if I build the rule like this, how much does it pay?" — and it
 * answers it with the real engine, through whichever plan the host says the rule belongs to.
 */
@Component({
  selector: 'app-rule-preview',
  standalone: true,
  imports: [
    FormsModule,
    DecimalPipe,
    LowerCasePipe,
    TranslatePipe,
    IconComponent,
    CurrencyFormatPipe,
    WsBadgeComponent,
    WsInputComponent,
  ],
  templateUrl: './rule-preview.component.html',
  styleUrl: './rule-preview.component.scss',
})
export class RulePreviewComponent {
  readonly model = input.required<RuleDefinitionForm>();

  get d(): RuleDefinitionForm {
    return this.model();
  }
}
