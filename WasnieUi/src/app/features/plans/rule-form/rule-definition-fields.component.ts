import { Component, input } from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import {
  WsButtonComponent,
  WsCategoryPickerComponent,
  WsInputComponent,
  WsSelectComponent,
} from '../../../shared/ui';
import { WsTooltipDirective } from '../../../shared/ui/ws-tooltip/ws-tooltip.directive';
import { RuleDefinitionForm } from './rule-definition-form';

/**
 * The sections of a rule: basics, measurement, rate table, trigger, modifier, cap and floor.
 *
 * ★★ RENDERS A MODEL IT DOES NOT OWN. The definition lives in {@link RuleDefinitionForm}, so the
 * Plans rule page and the guided tour's sandbox show the same fields with the same behaviour — the
 * tour is where people try a rule before paying anybody with it, and a second, simpler form there
 * would let them try a rule the real screen cannot build.
 */
@Component({
  selector: 'app-rule-definition-fields',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    DecimalPipe,
    TranslatePipe,
    IconComponent,
    WsButtonComponent,
    WsCategoryPickerComponent,
    WsInputComponent,
    WsSelectComponent,
    WsTooltipDirective,
  ],
  templateUrl: './rule-definition-fields.component.html',
  styleUrl: './rule-definition-fields.component.scss',
})
export class RuleDefinitionFieldsComponent {
  readonly model = input.required<RuleDefinitionForm>();

  get d(): RuleDefinitionForm {
    return this.model();
  }
}
