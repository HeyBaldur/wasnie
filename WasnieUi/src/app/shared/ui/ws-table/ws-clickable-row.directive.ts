import { Directive } from '@angular/core';

@Directive({
  selector: 'tr[routerLink]',
  standalone: true,
  host: { class: 'clickable' },
})
export class WsClickableRowDirective {}
