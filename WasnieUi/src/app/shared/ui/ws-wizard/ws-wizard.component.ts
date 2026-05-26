import { Component, computed, contentChildren, input } from '@angular/core';
import { IconComponent } from '../../components/icon/icon.component';
import { WsWizardStepComponent } from './ws-wizard-step.component';

@Component({
  selector: 'ws-wizard',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './ws-wizard.component.html',
  styleUrl: './ws-wizard.component.scss',
})
export class WsWizardComponent {
  readonly currentStep = input.required<string>();
  readonly hideOnComplete = input<string>('');

  readonly stepDefs = contentChildren(WsWizardStepComponent);

  readonly stepsWithState = computed(() => {
    const current = this.currentStep();
    const steps = this.stepDefs();
    const currentIdx = steps.findIndex(s => s.name() === current);
    return steps.map((s, i) => ({
      name: s.name(),
      title: s.title(),
      icon: s.icon(),
      state: i < currentIdx ? 'done' : i === currentIdx ? 'active' : 'upcoming',
    }));
  });

  readonly showIndicator = computed(() => {
    const hide = this.hideOnComplete();
    if (!hide) return true;
    return this.currentStep() !== hide;
  });
}
