import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { WsTabsComponent } from './ws-tabs.component';

describe('WsTabsComponent', () => {
  let fixture: ComponentFixture<WsTabsComponent>;

  const buttons = (): HTMLButtonElement[] =>
    [...fixture.nativeElement.querySelectorAll('button[role="tab"]')];

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [WsTabsComponent, TranslateModule.forRoot()] });
    fixture = TestBed.createComponent(WsTabsComponent);
    fixture.componentRef.setInput('tabs', [
      { value: 'steps', label: 'Steps', icon: 'list' },
      { value: 'experiments', label: 'Experiments', icon: 'archive' },
    ]);
    fixture.componentRef.setInput('translateLabels', false);
    fixture.componentRef.setInput('value', 'steps');
    fixture.detectChanges();
  });

  it('renders one tab per option, marking only the open one as selected', () => {
    expect(buttons().map(b => b.textContent?.trim())).toEqual(['Steps', 'Experiments']);
    expect(buttons().map(b => b.getAttribute('aria-selected'))).toEqual(['true', 'false']);
    expect(buttons()[0].classList).toContain('ws-tabs__tab--active');
  });

  it('selects a tab on click', () => {
    buttons()[1].click();
    fixture.detectChanges();

    expect(fixture.componentInstance.value()).toBe('experiments');
    expect(buttons()[1].getAttribute('aria-selected')).toBe('true');
  });

  it('is a standalone group by default and a card header when asked', () => {
    const group = (): HTMLElement => fixture.nativeElement.querySelector('.ws-tabs');
    expect(group().classList).not.toContain('ws-tabs--header');

    fixture.componentRef.setInput('variant', 'header');
    fixture.detectChanges();

    expect(group().classList).toContain('ws-tabs--header');
  });

  it('no marca nada mientras no se pida la marca', () => {
    // El check es opt-in: encenderlo por defecto le pondría una confirmación a cada par de
    // pestañas del producto, incluidas las que sólo cambian de vista y no confirman nada.
    expect(fixture.nativeElement.querySelector('.ws-tabs__check')).toBeNull();
  });

  it('marca la pestaña elegida, y sólo esa, cuando se pide la marca', () => {
    fixture.componentRef.setInput('selectedCheck', true);
    fixture.detectChanges();

    const checks = fixture.nativeElement.querySelectorAll('.ws-tabs__check');
    expect(checks.length).toBe(1);
    // ★ DENTRO DE LA PESTAÑA ABIERTA. Contarlos en el grupo pasaría igual con la marca colgando de
    // la pestaña equivocada, que es justo el error que haría leer mal el formulario de debajo.
    expect(buttons()[0].querySelector('.ws-tabs__check')).not.toBeNull();

    buttons()[1].click();
    fixture.detectChanges();

    expect(buttons()[0].querySelector('.ws-tabs__check')).toBeNull();
    expect(buttons()[1].querySelector('.ws-tabs__check')).not.toBeNull();
  });

  it('la marca ocupa el sitio del icono de la pestaña, no se suma a él', () => {
    fixture.componentRef.setInput('selectedCheck', true);
    fixture.detectChanges();

    // Las dos pestañas declaran icono; la abierta muestra la marca EN SU LUGAR, para que el ancho
    // del botón no salte al cambiar de pestaña.
    expect(buttons()[0].querySelectorAll('app-icon').length).toBe(1);
    expect(buttons()[1].querySelectorAll('app-icon').length).toBe(1);
  });

  it('moves with the arrow keys and wraps at the ends', () => {
    buttons()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(fixture.componentInstance.value()).toBe('experiments');

    buttons()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(fixture.componentInstance.value()).toBe('steps');

    buttons()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    expect(fixture.componentInstance.value()).toBe('experiments');
  });
});
