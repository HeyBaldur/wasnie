import { Component, DestroyRef, effect, inject, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { TranslatePipe } from '@ngx-translate/core';
import { WsModalComponent, WsButtonComponent } from '../../ui';
import { launchConfetti } from '../../ui/ws-confetti/confetti';
import { WelcomeService } from '../../../core/services/welcome.service';

/**
 * The welcome, right after registering: an illustration, one sentence, two ways to begin.
 *
 * ★ IT RENDERS WHEREVER IT IS PLACED BUT OPENS FROM ANYWHERE — the state is in WelcomeService, so the
 * shell can host it while the /manual screen re-opens it, without either knowing about the other.
 *
 * ★ THE SAME MODAL FOR EVERY TENANT, by decision: no plan-dependent copy, no branching, and no control
 * a Free workspace would be refused. The two buttons are the onboarding (the sandbox) and "just start".
 *
 * ★ CONFETTI ONLY THE FIRST TIME. It celebrates having just joined; re-opening the modal from /manual
 * is re-reading, not an event. WelcomeService says which of the two this is.
 */
@Component({
  selector: 'app-welcome-modal',
  standalone: true,
  imports: [TranslatePipe, WsModalComponent, WsButtonComponent],
  templateUrl: './welcome-modal.component.html',
  styleUrl: './welcome-modal.component.scss',
})
export class WelcomeModalComponent {
  readonly welcome = inject(WelcomeService);
  private readonly router = inject(Router);
  private stopConfetti: (() => void) | null = null;

  constructor() {
    effect(() => {
      if (this.welcome.isOpen() && this.welcome.celebrate()) {
        untracked(() => {
          this.stopConfetti?.();
          // Más largo y más denso que el valor por defecto: es LA celebración de haber llegado.
          this.stopConfetti = launchConfetti({ durationMs: 6500, particleCount: 340 });
        });
      }
    });
    inject(DestroyRef).onDestroy(() => this.stopConfetti?.());
  }

  /** Cierra el modal y lleva al onboarding guiado: la bienvenida termina donde empieza la práctica. */
  startTour(): void {
    this.welcome.close();
    void this.router.navigateByUrl('/guided-tour');
  }
}
