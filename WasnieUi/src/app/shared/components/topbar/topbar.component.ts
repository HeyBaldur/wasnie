import { Component, computed, HostListener, inject, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { IconComponent } from '../icon/icon.component';
import { AssistantTriggerComponent } from '../../../features/assistant/trigger/assistant-trigger.component';
import { Router } from '@angular/router';
import { RouterLink } from '@angular/router';
import { HasPermissionPipe } from '../../pipes/has-permission.pipe';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [TranslatePipe, IconComponent, AssistantTriggerComponent, RouterLink, HasPermissionPipe],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent {
  private readonly router = inject(Router);
  readonly authService = inject(AuthService);

  readonly dropdownOpen = signal(false);

  readonly userInitial = computed(() => {
    const email = this.authService.currentUser()?.email ?? '';
    return email.charAt(0).toUpperCase();
  });

  readonly tenantSlug = computed(() => {
    const slug = this.authService.currentUser()?.tenantSlug ?? '';
    return slug.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
  });

  goToProfile(): void {
    this.dropdownOpen.set(false);
    void this.router.navigateByUrl('/profile');
  }

  goToSettings(): void {
    void this.router.navigateByUrl('/admin');
  }

  /**
   * The audit trail (KAN-19).
   *
   * ★ IT CLOSES THE DROPDOWN FIRST, like goToProfile and unlike goToSettings. Leaving the menu open
   * over the page it just navigated to is the small bug the settings entry already has; it is not
   * copied here.
   */
  goToAuditLogs(): void {
    this.dropdownOpen.set(false);
    void this.router.navigateByUrl('/audit-logs');
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
