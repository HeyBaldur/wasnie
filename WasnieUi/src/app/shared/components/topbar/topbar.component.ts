import { Component, computed, HostListener, inject, OnInit, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { IconComponent } from '../icon/icon.component';
import { Router } from '@angular/router';
import { SubscriptionService } from '../../../features/subscription/services/subscription.service';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [TranslatePipe, IconComponent],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent implements OnInit {
  private readonly router = inject(Router);
  readonly authService = inject(AuthService);
  private readonly subscriptionService = inject(SubscriptionService);

  readonly dropdownOpen = signal(false);
  private readonly currentTier = signal<string | null>(null);

  readonly isFreeTier = computed(() => this.currentTier() === 'Free');

  readonly userInitial = computed(() => {
    const email = this.authService.currentUser()?.email ?? '';
    return email.charAt(0).toUpperCase();
  });

  readonly tenantSlug = computed(() => {
    const slug = this.authService.currentUser()?.tenantSlug ?? '';
    return slug.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
  });

  ngOnInit(): void {
    this.subscriptionService.getCurrent().subscribe({
      next: (sub) => this.currentTier.set(sub.tier),
      error: () => {},
    });
  }

  goToSettings(): void {
    void this.router.navigateByUrl('/admin');
  }

  goToUpgrade(): void {
    void this.router.navigateByUrl('/subscription');
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
