import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { WsPageHeaderComponent, WsEmptyStateComponent } from '../../shared/ui';

@Component({
  selector: 'app-payouts',
  standalone: true,
  imports: [TranslatePipe, AppShellComponent, WsPageHeaderComponent, WsEmptyStateComponent],
  templateUrl: './payouts.component.html',
  styleUrl: './payouts.component.scss',
})
export class PayoutsComponent {}
