import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { WsPageHeaderComponent, WsButtonComponent, WsEmptyStateComponent } from '../../shared/ui';

@Component({
  selector: 'app-transactions',
  standalone: true,
  imports: [TranslatePipe, AppShellComponent, WsPageHeaderComponent, WsButtonComponent, WsEmptyStateComponent],
  templateUrl: './transactions.component.html',
  styleUrl: './transactions.component.scss',
})
export class TransactionsComponent {}
