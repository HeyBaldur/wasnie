import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { PayeeFormComponent } from '../form/payee-form.component';
import { WsPageHeaderComponent } from '../../../shared/ui';
import { Payee } from '../models/payee.model';

@Component({
  selector: 'app-payee-create',
  standalone: true,
  imports: [
    AppShellComponent,
    TranslateModule,
    PayeeFormComponent,
    WsPageHeaderComponent,
  ],
  templateUrl: './payee-create.component.html',
  styleUrl: './payee-create.component.scss',
})
export class PayeeCreateComponent {
  private readonly router = inject(Router);

  onSaved(payee: Payee): void {
    this.router.navigate(['/payees', payee.id]);
  }

  onCancelled(): void {
    this.router.navigate(['/payees']);
  }
}
