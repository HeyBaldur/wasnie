import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { TransactionFormComponent } from '../form/transaction-form.component';
import { WsPageHeaderComponent } from '../../../shared/ui';
import { Transaction } from '../models/transaction.model';

@Component({
  selector: 'app-transaction-create',
  standalone: true,
  imports: [
    AppShellComponent,
    TranslateModule,
    TransactionFormComponent,
    WsPageHeaderComponent,
  ],
  templateUrl: './transaction-create.component.html',
  styleUrl: './transaction-create.component.scss',
})
export class TransactionCreateComponent {
  private readonly router = inject(Router);

  onSaved(_tx: Transaction): void {
    this.router.navigate(['/transactions']);
  }

  onCancelled(): void {
    this.router.navigate(['/transactions']);
  }
}
