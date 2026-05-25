import { Component, signal } from '@angular/core';
import { IconComponent } from '../../shared/components/icon/icon.component';
import {
  WsButtonComponent,
  WsInputComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsSegmentedControlComponent,
  WsPageHeaderComponent,
  WsModalComponent,
  WsEmptyStateComponent,
  WsTooltipDirective,
  WsTableComponent,
  WsTablePaginationComponent,
  type SegOption,
  type SelectOption,
} from '../../shared/ui';
import { WsSelectComponent } from '../../shared/ui/ws-select/ws-select.component';
import { WsDatePickerComponent } from '../../shared/ui/ws-date-picker/ws-date-picker.component';
import { WsDateRangePickerComponent } from '../../shared/ui/ws-date-range-picker/ws-date-range-picker.component';

@Component({
  selector: 'app-design-system',
  standalone: true,
  imports: [
    IconComponent,
    WsButtonComponent,
    WsInputComponent,
    WsBadgeComponent,
    WsCardComponent,
    WsSegmentedControlComponent,
    WsPageHeaderComponent,
    WsModalComponent,
    WsEmptyStateComponent,
    WsTooltipDirective,
    WsTableComponent,
    WsTablePaginationComponent,
    WsSelectComponent,
    WsDatePickerComponent,
    WsDateRangePickerComponent,
  ],
  templateUrl: './design-system.component.html',
  styleUrl: './design-system.component.scss',
})
export class DesignSystemComponent {
  readonly segValue = signal('a');
  readonly modalOpen = signal(false);

  readonly segOptions: SegOption[] = [
    { value: 'a', label: 'Option A' },
    { value: 'b', label: 'Option B' },
    { value: 'c', label: 'Option C' },
  ];

  readonly selectOptions: SelectOption[] = [
    { value: 'usd', label: 'USD — US Dollar' },
    { value: 'eur', label: 'EUR — Euro' },
    { value: 'gbp', label: 'GBP — British Pound' },
    { value: 'pln', label: 'PLN — Polish Zloty' },
  ];
}
