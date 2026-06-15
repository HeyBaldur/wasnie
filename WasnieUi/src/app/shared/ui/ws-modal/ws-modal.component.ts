import {
  Component,
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

  readonly isOpen = model(false);
  readonly size = input<'sm' | 'md' | 'lg' | 'xl'>('md');
  readonly title = input('');
  readonly description = input('');
  readonly closable = input(true);
  readonly closeOnBackdrop = input(true);
  readonly heroImageUrl = input('');
  readonly heroImageAlt = input('');
  readonly closed = output<void>();

  readonly dialogClass = computed(() => `ws-modal__dialog ws-modal__dialog--${this.size()}`);

  constructor() {
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
      this.close();
      return;
    }

    if (event.key === 'Tab') {
      this.trapFocus(event);
    }
  }

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
