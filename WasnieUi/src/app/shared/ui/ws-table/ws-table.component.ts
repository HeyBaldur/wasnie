import { Component, ViewEncapsulation } from '@angular/core';

@Component({
  selector: 'ws-table',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `<div class="ws-table-wrap"><ng-content /></div>`,
  styleUrl: './ws-table.component.scss',
})
export class WsTableComponent {}
