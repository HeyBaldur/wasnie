import { Component, input } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';

/**
 * The header of an import step: what this step is for, with an animated illustration of it.
 *
 * `map`    — columns of the file flowing into Incentra's fields.
 * `review` — rows being checked one by one, each getting its tick or its flag.
 *
 * Shared by the payee, transaction and transaction-update wizards so every step speaks the same language as
 * the upload step. The illustration is decorative (aria-hidden); the title and description carry the meaning.
 */
@Component({
  selector: 'app-import-step-hero',
  standalone: true,
  imports: [TranslatePipe],
  templateUrl: './import-step-hero.component.html',
  styleUrl: './import-step-hero.component.scss',
})
export class ImportStepHeroComponent {
  readonly kind = input.required<'map' | 'review'>();
  readonly titleKey = input.required<string>();
  readonly descKey = input.required<string>();
}
