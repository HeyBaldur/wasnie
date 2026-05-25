import {
  Component,
  ElementRef,
  HostListener,
  ViewChild,
  inject,
  input,
  model,
  output,
} from '@angular/core';

@Component({
  selector: 'ws-popover',
  standalone: true,
  templateUrl: './ws-popover.component.html',
  styleUrl: './ws-popover.component.scss',
})
export class WsPopoverComponent {
  @ViewChild('popover') popoverRef!: ElementRef<HTMLElement>;

  private readonly host = inject(ElementRef);

  readonly isOpen = model(false);
  readonly placement = input<'bottom' | 'top' | 'bottom-end' | 'top-end'>('bottom');
  readonly openedChange = output<boolean>();

  toggle(): void {
    this.isOpen.update(v => !v);
    this.openedChange.emit(this.isOpen());
  }

  open(): void {
    this.isOpen.set(true);
    this.openedChange.emit(true);
  }

  close(): void {
    this.isOpen.set(false);
    this.openedChange.emit(false);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    if (!this.isOpen()) return;
    if (!this.host.nativeElement.contains(event.target)) {
      this.close();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen()) this.close();
  }

  stopProp(event: Event): void {
    event.stopPropagation();
  }
}
