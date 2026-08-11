import { Component, computed, HostListener, inject, OnInit, signal } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../../core/services/auth.service';
import { IconComponent } from '../icon/icon.component';
import { AssistantTriggerComponent } from '../../../features/assistant/trigger/assistant-trigger.component';
import { Router } from '@angular/router';
import { SubscriptionService } from '../../../features/subscription/services/subscription.service';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [TranslatePipe, IconComponent, AssistantTriggerComponent, RouterLink],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
})
export class TopbarComponent implements OnInit {
  private readonly router = inject(Router);
  readonly authService = inject(AuthService);
  private readonly subscriptionService = inject(SubscriptionService);

  readonly dropdownOpen = signal(false);
  private readonly _tier = signal<string | null>(null);

  readonly isFreeTier = computed(() => this._tier() === 'Free');
  readonly isPaidTier = computed(() => {
    const t = this._tier();
    return t === 'Starter' || t === 'Growth' || t === 'Scale';
  });
  readonly tierName = computed(() => this._tier());

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
      next: (sub) => this._tier.set(sub.tier),
      error: () => {},
    });
  }

  goToProfile(): void {
    this.dropdownOpen.set(false);
    void this.router.navigateByUrl('/profile');
  }

  goToSettings(): void {
    void this.router.navigateByUrl('/admin');
  }

  goToUpgrade(): void {
    void this.router.navigateByUrl('/subscription');
  }

  goToSubscription(): void {
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
