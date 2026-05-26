import { Component, input } from '@angular/core';

@Component({
  selector: 'ws-wizard-step',
  standalone: true,
  template: '',
  host: { style: 'display: none' },
})
export class WsWizardStepComponent {
  readonly name = input.required<string>();
  readonly title = input.required<string>();
  readonly icon = input('');
}
