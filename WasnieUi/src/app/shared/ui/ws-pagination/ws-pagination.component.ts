import { Component, computed, input, output } from '@angular/core';
import { DecimalPipe } from '@angular/common';

@Component({
  selector: 'ws-pagination',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './ws-pagination.component.html',
  styleUrl: './ws-pagination.component.scss',
})
export class WsPaginationComponent {
  readonly currentPage = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly pageSizeOptions = input<number[]>([10, 25, 50, 100]);
  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();

  readonly rangeStart = computed(() => (this.currentPage() - 1) * this.pageSize() + 1);
  readonly rangeEnd = computed(() => Math.min(this.currentPage() * this.pageSize(), this.totalCount()));

  readonly pageNumbers = computed<(number | '...')[]>(() => {
    const current = this.currentPage();
    const total = this.totalPages();
    if (total <= 1) return total === 1 ? [1] : [];

    const result: (number | '...')[] = [];
    const low = Math.max(1, current - 2);
    const high = Math.min(total, current + 2);

    // Always show page 1
    if (low > 1) {
      result.push(1);
      if (low > 2) result.push('...');
    }

    for (let i = low; i <= high; i++) {
      result.push(i);
    }

    // Always show last page
    if (high < total) {
      if (high < total - 1) result.push('...');
      result.push(total);
    }

    return result;
  });

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages() || page === this.currentPage()) return;
    this.pageChange.emit(page);
  }

  onPageSizeChange(size: number): void {
    this.pageSizeChange.emit(size);
  }

  isEllipsis(item: number | '...'): item is '...' {
    return item === '...';
  }
}
