import { Component, input } from '@angular/core';
import { NgClass } from '@angular/common';
import { IconComponent } from '../../components/icon/icon.component';

export type StatCardVariant = 'default' | 'success' | 'warning' | 'danger';

@Component({
  selector: 'ws-stat-card',
  standalone: true,
  imports: [NgClass, IconComponent],
  templateUrl: './ws-stat-card.component.html',
  styleUrl: './ws-stat-card.component.scss',
})
export class WsStatCardComponent {
  readonly value = input.required<number | string>();
  readonly label = input.required<string>();
  readonly variant = input<StatCardVariant>('default');
  readonly icon = input('');
}
