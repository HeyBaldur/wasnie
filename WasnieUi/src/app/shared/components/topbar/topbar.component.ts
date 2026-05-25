import { Component, computed, HostListener, inject, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { ThemeToggleComponent } from '../theme-toggle/theme-toggle.component';
import { IconComponent } from '../icon/icon.component';
import { Router } from '@angular/router';

const SUPPORTED_LANGS = ['en', 'es', 'pl'] as const;
type SupportedLang = (typeof SUPPORTED_LANGS)[number];

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [TranslatePipe, ThemeToggleComponent, IconComponent],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent {
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  readonly authService = inject(AuthService);

  readonly supportedLangs = SUPPORTED_LANGS;
  readonly currentLang = signal<SupportedLang>(
    (this.translate.currentLang as SupportedLang) ?? 'en'
  );
  readonly dropdownOpen = signal(false);

  readonly userInitial = computed(() => {
    const email = this.authService.currentUser()?.email ?? '';
    return email.charAt(0).toUpperCase();
  });

  readonly tenantSlug = computed(() => {
    const slug = this.authService.currentUser()?.tenantSlug ?? '';
    return slug.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
  });

  switchLanguage(lang: SupportedLang): void {
    this.translate.use(lang);
    this.currentLang.set(lang);
  }

  toggleDropdown(event: MouseEvent): void {
    event.stopPropagation();
    this.dropdownOpen.update((v) => !v);
  }

  @HostListener('document:click')
  closeDropdown(): void {
    this.dropdownOpen.set(false);
  }

  logout(): void {
    this.authService.logout();
    this.router.navigateByUrl('/auth/login');
  }
}
