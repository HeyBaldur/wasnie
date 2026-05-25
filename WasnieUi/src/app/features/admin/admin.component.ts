import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../shared/components/icon/icon.component';
import { WsPageHeaderComponent } from '../../shared/ui';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [TranslatePipe, AppShellComponent, IconComponent, WsPageHeaderComponent],
  templateUrl: './admin.component.html',
  styleUrl: './admin.component.scss',
})
export class AdminComponent {}
