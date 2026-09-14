import {
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  ViewChild,
  computed,
  effect,
  inject,
  input,
  model,
  output,
} from '@angular/core';

@Component({
  selector: 'ws-modal',
  standalone: true,
  templateUrl: './ws-modal.component.html',
  styleUrl: './ws-modal.component.scss',
})
export class WsModalComponent {
  @ViewChild('dialog') private dialogRef!: ElementRef<HTMLElement>;

  /**
   * The open modal's root node, kept so it can be animated OUT after Angular has removed it.
   *
   * ★★ WHY NOT `animate.leave`. It was the first attempt and it only animates an element removed by its
   * OWN `@if`. Most screens instead remove the whole modal from outside — `@if (closeTarget()) {
   * <ws-modal [isOpen]="true"> }` in Reconciliation, `@if (selectedTransaction())` around the transaction
   * modals — and there Angular destroys the component with no exit at all. A copy covers both paths:
   * this setter sees the inner `@if` close (it is called with undefined), DestroyRef sees the outer one.
   */
  @ViewChild('root') private set root(ref: ElementRef<HTMLElement> | undefined) {
    if (ref) {
      this.openRoot = ref.nativeElement;
    } else if (this.openRoot) {
      this.playExit(this.openRoot);
      this.openRoot = null;
    }
  }
  private openRoot: HTMLElement | null = null;

  readonly isOpen = model(false);
  readonly size = input<'sm' | 'md' | 'lg' | 'xl'>('md');
  readonly title = input('');
  readonly description = input('');
  readonly closable = input(true);
  readonly closeOnBackdrop = input(true);
  readonly heroImageUrl = input('');
  readonly heroImageAlt = input('');
  /**
   * `hero`: sin cabecera con borde — el contenido (una ilustración a sangre, un título centrado) llega
   * hasta el borde superior y la X flota encima. Para momentos de bienvenida, no para formularios.
   */
  readonly variant = input<'default' | 'hero'>('default');
  readonly closed = output<void>();

  readonly dialogClass = computed(() =>
    `ws-modal__dialog ws-modal__dialog--${this.size()}` + (this.variant() === 'hero' ? ' ws-modal__dialog--hero' : ''));

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      if (this.openRoot) {
        this.playExit(this.openRoot);
        this.openRoot = null;
        // The parent destroyed the modal while it was open, so isOpen never became false and the effect
        // below never released the page. Without this the page stayed scroll-locked after closing
        // (measured on /reconciliation: body overflow still "hidden" after Cancel).
        document.body.style.overflow = '';
      }
    });

    effect(() => {
      if (this.isOpen()) {
        document.body.style.overflow = 'hidden';
        setTimeout(() => this.focusFirst(), 10);
      } else {
        document.body.style.overflow = '';
      }
    });
  }

  close(): void {
    this.isOpen.set(false);
    this.closed.emit();
  }

  onBackdropClick(): void {
    if (this.closeOnBackdrop()) {
      this.close();
    }
  }

  @HostListener('keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (!this.isOpen()) return;

    if (event.key === 'Escape') {
      event.preventDefault();
      // Un modal sin botón de cerrar tampoco se cierra con Escape: se sale por sus propias opciones.
      if (this.closable()) this.close();
      return;
    }

    if (event.key === 'Tab') {
      this.trapFocus(event);
    }
  }

  /**
   * Animates an inert COPY of the closed modal out, then removes it.
   *
   * ★ A copy, never the node Angular removed: re-attaching a node Angular owns would let a later render
   * touch it. The copy keeps the component's encapsulation attributes, so the same styles apply. It is
   * inert — no listeners, `aria-hidden`, no pointer events — so nothing on it can be clicked or read twice.
   * Form values are carried over by hand because `cloneNode` copies attributes, not what was typed.
   *
   * ★ REMOVED BY TIMER, NOT BY `animationend`. A background tab or reduced motion may never fire it,
   * and a leftover full-screen copy would sit over the page. 400ms is longer than the longest exit.
   */
  private playExit(node: HTMLElement): void {
    if (typeof document === 'undefined') return;

    const ghost = node.cloneNode(true) as HTMLElement;
    const sourceFields = node.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>('input, textarea');
    ghost.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>('input, textarea')
      .forEach((field, i) => { field.value = sourceFields[i]?.value ?? ''; });

    ghost.removeAttribute('role');
    ghost.removeAttribute('aria-modal');
    ghost.setAttribute('aria-hidden', 'true');
    ghost.setAttribute('inert', '');
    ghost.classList.add('ws-modal--leaving');
    document.body.appendChild(ghost);

    setTimeout(() => ghost.remove(), WsModalComponent.EXIT_MS);
  }

  /** Longer than the longest exit animation (220ms), so the copy is never cut mid-fade. */
  static readonly EXIT_MS = 400;

  private focusFirst(): void {
    const el = this.dialogRef?.nativeElement;
    if (!el) return;
    const focusable = this.getFocusable(el);
    focusable[0]?.focus();
  }

  private trapFocus(event: KeyboardEvent): void {
    const el = this.dialogRef?.nativeElement;
    if (!el) return;
    const focusable = this.getFocusable(el);
    if (!focusable.length) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    if (event.shiftKey) {
      if (document.activeElement === first) {
        event.preventDefault();
        last.focus();
      }
    } else {
      if (document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }
  }

  private getFocusable(container: HTMLElement): HTMLElement[] {
    return Array.from(
      container.querySelectorAll<HTMLElement>(
        'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
      )
    );
  }
}
