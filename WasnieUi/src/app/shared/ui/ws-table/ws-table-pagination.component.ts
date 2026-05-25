import { Component, input, output } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { IconComponent } from '../../components/icon/icon.component';

@Component({
  selector: 'ws-table-pagination',
  standalone: true,
  imports: [TranslatePipe, IconComponent],
  templateUrl: './ws-table-pagination.component.html',
  styleUrl: './ws-table-pagination.component.scss',
})
export class WsTablePaginationComponent {
  readonly page = input.required<number>();
  readonly pageSize = input(20);
  readonly totalCount = input.required<number>();
  readonly infoKey = input('PLANS.PAGINATION_INFO');
  readonly pageChange = output<number>();

  get totalPages(): number {
    return Math.ceil(this.totalCount() / this.pageSize());
  }

  get from(): number {
    return (this.page() - 1) * this.pageSize() + 1;
  }

  get to(): number {
    return Math.min(this.page() * this.pageSize(), this.totalCount());
  }

  get pages(): number[] {
    return Array.from({ length: this.totalPages }, (_, i) => i + 1);
  }

  goTo(p: number): void {
    if (p >= 1 && p <= this.totalPages) {
      this.pageChange.emit(p);
    }
  }
}
