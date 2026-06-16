import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { DateFormatPipe } from '../../pipes/date-format.pipe';
import { CurrencyFormatPipe } from '../../pipes/currency-format.pipe';
import { IconComponent } from '../icon/icon.component';
import { WsBadgeComponent } from '../../ui';
import { OverlapRow } from '../../models/overlap-row.model';

@Component({
  selector: 'app-overlap-warning',
  standalone: true,
  imports: [TranslateModule, DateFormatPipe, CurrencyFormatPipe, IconComponent, WsBadgeComponent],
  templateUrl: './overlap-warning.component.html',
  styleUrl: './overlap-warning.component.scss',
})
export class OverlapWarningComponent {
  @Input() rows: OverlapRow[] = [];
  @Input() loading = false;
  @Input() warningKey = '';
  @Input() col3HeaderKey = '';
  @Output() rowClick = new EventEmitter<string>();
}
