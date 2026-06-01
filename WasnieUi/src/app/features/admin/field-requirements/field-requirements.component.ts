import { Component, OnInit, inject, signal } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { SettingsApiService, FieldRequirement } from '../services/settings.api.service';
import { ToastService } from '../../../shared/services/toast.service';
import {
  WsSegmentedControlComponent,
  type SegOption,
} from '../../../shared/ui';

@Component({
  selector: 'app-field-requirements',
  standalone: true,
  imports: [
    TranslateModule,
    WsSegmentedControlComponent,
  ],
  templateUrl: './field-requirements.component.html',
  styleUrl: './field-requirements.component.scss',
})
export class FieldRequirementsComponent implements OnInit {
  private readonly settingsApi = inject(SettingsApiService);
  private readonly toast = inject(ToastService);

  readonly requirements = signal<FieldRequirement[]>([]);
  readonly loading = signal(true);

  readonly reqOptions: SegOption[] = [
    { value: 'required', label: 'SETTINGS.FIELD_REQUIRED' },
    { value: 'optional', label: 'SETTINGS.FIELD_OPTIONAL' },
  ];

  ngOnInit(): void {
    this.settingsApi.getFieldRequirements().subscribe({
      next: (data) => {
        this.requirements.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  segValue(req: FieldRequirement): string {
    return req.isRequired ? 'required' : 'optional';
  }

  onToggle(req: FieldRequirement, value: string): void {
    const isRequired = value === 'required';
    if (req.isRequired === isRequired) return;

    this.settingsApi.updateFieldRequirement(req.entityName, req.fieldName, isRequired).subscribe({
      next: () => {
        this.requirements.update(list =>
          list.map(r =>
            r.entityName === req.entityName && r.fieldName === req.fieldName
              ? { ...r, isRequired }
              : r
          )
        );
        this.toast.show('SETTINGS.TOAST_SAVED', 'success');
      },
      error: () => {
        this.toast.show('ERRORS.GENERIC', 'error');
      },
    });
  }

  fieldLabel(fieldName: string): string {
    return `SETTINGS.FIELD_${fieldName.toUpperCase()}`;
  }
}
