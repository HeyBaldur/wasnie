import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { HasPermissionDirective } from '../../../shared/directives/has-permission.directive';
import { ToastService } from '../../../shared/services/toast.service';
import { extractApiError } from '../../../shared/utils/api-error';
import { CategoryMappingsStore } from '../state/category-mappings.store';
import { CategoryInputField, CategoryMapping } from '../models/category-mapping.model';
import {
  WsPageLayoutComponent,
  WsButtonComponent,
  WsInputComponent,
  WsSelectComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsPaginationComponent,
  WsModalComponent,
  WsConfirmationModalComponent,
  type SelectOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-category-mappings-list',
  standalone: true,
  imports: [
    AppShellComponent,
    RefreshOnEnterDirective,
    HasPermissionDirective,
    TranslateModule,
    ReactiveFormsModule,
    WsPageLayoutComponent,
    WsButtonComponent,
    WsInputComponent,
    WsSelectComponent,
    WsTableComponent,
    WsTableEmptyComponent,
    WsPaginationComponent,
    WsModalComponent,
    WsConfirmationModalComponent,
  ],
  templateUrl: './category-mappings-list.component.html',
  styleUrl: './category-mappings-list.component.scss',
})
export class CategoryMappingsListComponent {
  readonly store = inject(CategoryMappingsStore);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  readonly fieldOptions: SelectOption[] = [
    { value: CategoryInputField.ProductSku, label: 'CATEGORY_MAPPINGS.FIELD_PRODUCT_SKU' },
    { value: CategoryInputField.ProductName, label: 'CATEGORY_MAPPINGS.FIELD_PRODUCT_NAME' },
  ];

  get skeletonRows(): number[] {
    return Array.from({ length: 5 }, (_, i) => i);
  }

  readonly modalOpen = signal(false);
  readonly saving = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly deleteOpen = signal(false);
  readonly deleting = signal(false);

  readonly isEdit = computed(() => this.editingId() !== null);

  readonly form = this.fb.nonNullable.group({
    inputField: [CategoryInputField.ProductSku as string, Validators.required],
    inputValue: ['', [Validators.required, Validators.maxLength(500)]],
    category: ['', [Validators.required, Validators.maxLength(200)]],
  });

  onSearch(value: string): void {
    this.store.setSearch(value);
  }

  goToPage(page: number): void {
    this.store.setPage(page);
  }

  goToPageSize(size: number): void {
    this.store.setPageSize(size);
  }

  openCreate(): void {
    this.editingId.set(null);
    this.form.reset({
      inputField: CategoryInputField.ProductSku,
      inputValue: '',
      category: '',
    });
    this.modalOpen.set(true);
  }

  openEdit(mapping: CategoryMapping): void {
    this.editingId.set(mapping.id);
    this.form.reset({
      inputField: mapping.inputField,
      inputValue: mapping.inputValue,
      category: mapping.category,
    });
    this.modalOpen.set(true);
  }

  async onSubmit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toast.show('CATEGORY_MAPPINGS.TOAST_INVALID', 'error');
      return;
    }
    this.saving.set(true);
    try {
      const v = this.form.getRawValue();
      const payload = {
        inputField: v.inputField as CategoryInputField,
        inputValue: v.inputValue,
        category: v.category,
      };
      const editingId = this.editingId();
      if (editingId) {
        await this.store.update(editingId, { ...payload, id: editingId });
        this.toast.show('CATEGORY_MAPPINGS.TOAST_UPDATED', 'success');
      } else {
        await this.store.create(payload);
        this.toast.show('CATEGORY_MAPPINGS.TOAST_CREATED', 'success');
      }
      this.modalOpen.set(false);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.saving.set(false);
    }
  }

  onDeleteRequest(): void {
    this.deleteOpen.set(true);
  }

  async onConfirmDelete(): Promise<void> {
    const id = this.editingId();
    if (!id) return;
    this.deleting.set(true);
    try {
      await this.store.remove(id);
      this.toast.show('CATEGORY_MAPPINGS.TOAST_DELETED', 'success');
      this.deleteOpen.set(false);
      this.modalOpen.set(false);
    } catch (err) {
      this.toast.show(extractApiError(err), 'error');
    } finally {
      this.deleting.set(false);
    }
  }

  fieldError(control: string): string {
    const c = this.form.get(control);
    return c && c.touched && c.hasError('required') ? 'VALIDATION.REQUIRED' : '';
  }
}
