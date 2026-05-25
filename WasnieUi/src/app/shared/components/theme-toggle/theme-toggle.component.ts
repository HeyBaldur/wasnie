import {
  Component,
  computed,
  HostListener,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { ThemeMode, ThemeService } from '../../../core/theme/theme.service';

interface ThemeOption {
  mode: ThemeMode;
  labelKey: string;
  icon: string;
}

const OPTIONS: ThemeOption[] = [
  { mode: 'light', labelKey: 'THEME.LIGHT', icon: '☀️' },
  { mode: 'soft', labelKey: 'THEME.SOFT', icon: '🌤' },
  { mode: 'dark', labelKey: 'THEME.DARK', icon: '🌙' },
  { mode: 'system', labelKey: 'THEME.SYSTEM', icon: '💻' },
];

@Component({
  selector: 'app-theme-toggle',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  templateUrl: './theme-toggle.component.html',
  styleUrl: './theme-toggle.component.scss',
})
export class ThemeToggleComponent {
  private readonly themeService = inject(ThemeService);

  readonly options = OPTIONS;
  readonly currentMode = this.themeService.mode;
  readonly resolvedTheme = this.themeService.resolvedTheme;
  readonly isOpen = signal(false);
  readonly focusedIndex = signal(0);

  readonly currentIcon = computed(() => {
    const mode = this.currentMode();
    return OPTIONS.find((o) => o.mode === mode)?.icon ?? '💻';
  });

  toggle(): void {
    const opening = !this.isOpen();
    this.isOpen.set(opening);
    if (opening) {
      this.focusedIndex.set(OPTIONS.findIndex((o) => o.mode === this.currentMode()));
    }
  }

  select(mode: ThemeMode): void {
    this.themeService.setMode(mode);
    this.isOpen.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (!this.isOpen()) return;

    switch (event.key) {
      case 'Escape':
        this.isOpen.set(false);
        break;
      case 'ArrowDown':
        event.preventDefault();
        this.focusedIndex.update((i) => (i + 1) % OPTIONS.length);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.focusedIndex.update((i) => (i - 1 + OPTIONS.length) % OPTIONS.length);
        break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        this.select(OPTIONS[this.focusedIndex()].mode);
        break;
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (!target.closest('app-theme-toggle')) {
      this.isOpen.set(false);
    }
  }
}
