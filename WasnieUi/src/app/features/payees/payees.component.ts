import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { WsPageHeaderComponent, WsButtonComponent, WsEmptyStateComponent } from '../../shared/ui';

@Component({
  selector: 'app-payees',
  standalone: true,
  imports: [TranslatePipe, AppShellComponent, WsPageHeaderComponent, WsButtonComponent, WsEmptyStateComponent],
  templateUrl: './payees.component.html',
  styleUrl: './payees.component.scss',
})
export class PayeesComponent {}
