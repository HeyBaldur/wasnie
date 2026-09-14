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
import { WsGuideStep, WsGuideStepperComponent } from '../../shared/ui';
import { WsSelectComponent } from '../../shared/ui/ws-select/ws-select.component';
import { WsPopoverComponent } from '../../shared/ui/ws-popover/ws-popover.component';
import { WsDatePickerComponent } from '../../shared/ui/ws-date-picker/ws-date-picker.component';
import { WsDateRangePickerComponent } from '../../shared/ui/ws-date-range-picker/ws-date-range-picker.component';

@Component({
  selector: 'app-design-system',
  standalone: true,
  imports: [
    IconComponent,
    WsGuideStepperComponent,
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
    WsPopoverComponent,
    WsDatePickerComponent,
    WsDateRangePickerComponent,
  ],
  templateUrl: './design-system.component.html',
  styleUrl: './design-system.component.scss',
})
export class DesignSystemComponent {
  /**
   * A sample of the stroked set — not the whole thing, which would turn this page into a catalogue
   * nobody scrolls. The point of the section above it is the two RASTER icons; these are here so the
   * difference between the two kinds is visible side by side.
   */
  readonly sampleIcons = [
    'plus', 'edit', 'trash', 'search', 'check-circle', 'x-circle', 'alert-triangle',
    'info', 'chevron-down', 'chevron-right', 'more-vertical', 'download', 'ban', 'arrow-left',
  ];

  readonly exportMenuOpen = signal(false);

  readonly guidePicked = signal('plan');


  /** Muestra del panel de guía: un paso de cada estado, que es lo que hay que poder comparar. */

  readonly guideSteps: WsGuideStep[] = [

    { id: 'plan', title: 'Create a plan', description: 'The container for the rules that decide what a sale pays.', state: 'done' },

    { id: 'rule', title: 'Add a rule', description: 'The rate table: how much commission a sale generates.', state: 'current' },

    { id: 'payee', title: 'Create a payee', description: 'The person who earns the commission.', state: 'available' },

    { id: 'payrun', title: 'Run a pay run', description: 'Closes the period for everyone at once.', state: 'blocked', blockedReason: 'You need a calculated transaction first.' },

  ];


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
