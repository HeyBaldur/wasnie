import { Component, computed, input, output, TemplateRef } from '@angular/core';
import { NgClass, NgTemplateOutlet } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';

export interface WsColumn<T = Record<string, unknown>> {
  key: string;
  header: string;
  icon?: string;
  width?: string;
  align?: 'left' | 'right' | 'center';
  truncate?: boolean;
  cellTemplate?: TemplateRef<{ $implicit: T; index: number }>;
}

@Component({
  selector: 'ws-data-table',
  standalone: true,
  imports: [NgClass, NgTemplateOutlet, TranslatePipe, IconComponent],
  templateUrl: './ws-data-table.component.html',
  styleUrl: './ws-data-table.component.scss',
})
export class WsDataTableComponent<T extends Record<string, unknown> = Record<string, unknown>> {
  readonly columns = input.required<WsColumn<T>[]>();
  readonly data = input<T[]>([]);
  readonly isLoading = input(false);
  readonly emptyKey = input('COMMON.EMPTY');
  readonly maxHeight = input('');
  readonly rowClick = output<T>();

  readonly skeletonRows = computed(() => Array.from({ length: 4 }));

  getCellValue(row: T, key: string): string {
    const val = row[key];
    return val === null || val === undefined ? '' : String(val);
  }

  shouldTruncate(col: WsColumn<T>): boolean {
    return col.truncate !== false;
  }

  onRowClick(row: T): void {
    this.rowClick.emit(row);
  }
}
