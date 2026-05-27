import { Component, inject, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';
import { TierLimitModalComponent } from './shared/components/tier-limit-modal/tier-limit-modal.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, TierLimitModalComponent],
  template: `
    <router-outlet />
    <app-tier-limit-modal />
  `,
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly translate = inject(TranslateService);

  ngOnInit(): void {
    const saved = localStorage.getItem('wasnie_lang') ?? 'en';
    this.translate.use(saved);
  }
}
