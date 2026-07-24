import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom, Subject } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { toSignal } from '@angular/core/rxjs-interop';
import { CategoryMappingsApiService } from '../services/category-mappings.api.service';
import {
  CategoryMapping,
  CreateCategoryMappingRequest,
  UpdateCategoryMappingRequest,
} from '../models/category-mapping.model';
import { PagedResult, PaginationParams } from '../../../shared/models/pagination.models';

@Injectable({ providedIn: 'root' })
export class CategoryMappingsStore {
  private readonly api = inject(CategoryMappingsApiService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly sortBy = signal('category');
  readonly sortOrder = signal<'asc' | 'desc'>('asc');

  private readonly searchSubject$ = new Subject<string>();
  readonly search = toSignal(this.searchSubject$.pipe(debounceTime(300)), { initialValue: '' });
  private _rawSearch = '';

  readonly pagedResult = signal<PagedResult<CategoryMapping> | null>(null);
  readonly mappings = computed(() => this.pagedResult()?.items ?? []);
  readonly totalCount = computed(() => this.pagedResult()?.totalCount ?? 0);
  readonly totalPages = computed(() => this.pagedResult()?.totalPages ?? 1);

  get rawSearch(): string {
    return this._rawSearch;
  }

  constructor() {
    effect(() => {
      const p = this.page();
      const ps = this.pageSize();
      const sb = this.sortBy();
      const so = this.sortOrder();
      const srch = this.search();
      void this._loadInternal(p, ps, sb, so, srch);
    });
  }

  private async _loadInternal(
    page: number,
    pageSize: number,
    sortBy: string,
    sortOrder: 'asc' | 'desc',
    search: string,
  ): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      const params: PaginationParams = {
        page,
        pageSize,
        sortBy,
        sortOrder,
        search: search || undefined,
      };
      const data = await firstValueFrom(this.api.list(params));
      this.pagedResult.set(data);
    } catch {
      this.error.set('ERRORS.GENERIC');
    } finally {
      this.loading.set(false);
    }
  }

  /** RefreshableStore — reload on route re-entry. */
  refresh(): Promise<void> {
    return this.loadMappings();
  }

  async loadMappings(): Promise<void> {
    await this._loadInternal(
      this.page(), this.pageSize(), this.sortBy(), this.sortOrder(), this.search());
  }

  async create(request: CreateCategoryMappingRequest): Promise<CategoryMapping> {
    const created = await firstValueFrom(this.api.create(request));
    await this.loadMappings();
    return created;
  }

  async update(id: string, request: UpdateCategoryMappingRequest): Promise<CategoryMapping> {
    const updated = await firstValueFrom(this.api.update(id, request));
    await this.loadMappings();
    return updated;
  }

  async remove(id: string): Promise<void> {
    await firstValueFrom(this.api.delete(id));
    await this.loadMappings();
  }

  setSearch(value: string): void {
    this._rawSearch = value;
    this.page.set(1);
    this.searchSubject$.next(value);
  }

  setPage(value: number): void {
    this.page.set(value);
  }

  setPageSize(value: number): void {
    this.pageSize.set(value);
    this.page.set(1);
  }
}
