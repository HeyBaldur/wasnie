import { Component } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { WsPageHeaderComponent } from '../../shared/ui';
import { HasPermissionDirective } from '../../shared/directives/has-permission.directive';
import { FieldRequirementsComponent } from './field-requirements/field-requirements.component';
import { AppearanceCardComponent } from './appearance-card/appearance-card.component';

@Component({
  selector: 'app-admin',
  standalone: true,
  imports: [
    TranslatePipe,
    AppShellComponent,
    WsPageHeaderComponent,
    HasPermissionDirective,
    FieldRequirementsComponent,
    AppearanceCardComponent,
  ],
  templateUrl: './admin.component.html',
  styleUrl: './admin.component.scss',
})
export class AdminComponent {}
