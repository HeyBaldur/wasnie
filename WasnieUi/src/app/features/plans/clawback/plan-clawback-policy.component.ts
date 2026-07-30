import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom } from 'rxjs';
import { Plan } from '../models/plan.model';
import { PlansApiService } from '../services/plans.api.service';
import { WsCardComponent } from '../../../shared/ui/ws-card/ws-card.component';
import { WsInputComponent } from '../../../shared/ui/ws-input/ws-input.component';
import { WsButtonComponent } from '../../../shared/ui/ws-button/ws-button.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';

/**
 * The clawback switch for one plan.
 *
 * Both fields empty means the plan claws nothing back — the state every plan is in until someone
 * deliberately fills them, which is why the hint says filling them ACTIVATES clawbacks rather than
 * describing them as settings.
 *
 * No money is computed here: the form stores a policy, and the engine reads it when a pay run is
 * settled.
 */
@Component({
  selector: 'app-plan-clawback-policy',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    TranslateModule,
    WsCardComponent,
    WsInputComponent,
    WsButtonComponent,
    IconComponent,
  ],
  templateUrl: './plan-clawback-policy.component.html',
  styleUrl: './plan-clawback-policy.component.scss',
})
export class PlanClawbackPolicyComponent {
  readonly plan = input.required<Plan>();
  readonly canEdit = input(false);
  readonly saved = output<void>();

  private readonly api = inject(PlansApiService);
  private readonly fb = inject(FormBuilder);

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly savedOk = signal(false);

  readonly form = this.fb.nonNullable.group({
    maturationDays: [null as number | null, [Validators.min(1)]],
    capPercent: [null as number | null, [Validators.min(0), Validators.max(100)]],
  });

  readonly isActive = computed(() => this.plan().clawbackMaturationDays != null);

  constructor() {
    effect(() => {
      const p = this.plan();
      this.form.setValue({
        maturationDays: p.clawbackMaturationDays ?? null,
        capPercent: p.clawbackCapPercent ?? null,
      });
      if (!this.canEdit()) this.form.disable();
    });
  }

  async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.savedOk.set(false);
    try {
      const raw = this.form.getRawValue();
      await firstValueFrom(
        this.api.setClawbackPolicy(this.plan().id, {
          maturationDays: raw.maturationDays,
          capPercent: raw.capPercent,
        }),
      );
      this.savedOk.set(true);
      this.saved.emit();
    } catch {
      this.error.set('PLANS.CLAWBACK_SAVE_ERROR');
    } finally {
      this.saving.set(false);
    }
  }
}
