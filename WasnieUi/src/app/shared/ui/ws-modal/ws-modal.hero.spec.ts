import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { WsModalComponent } from './ws-modal.component';

@Component({
  standalone: true,
  imports: [WsModalComponent],
  template: `<ws-modal variant="hero" [isOpen]="true"><p class="body">Body</p><div slot="footer">Footer</div></ws-modal>`,
})
class HeroHostComponent {}

@Component({
  standalone: true,
  imports: [WsModalComponent],
  template: `<ws-modal title="Title" [isOpen]="true"><p class="body">Body</p></ws-modal>`,
})
class DefaultHostComponent {}

@Component({
  standalone: true,
  imports: [WsModalComponent],
  template: `<ws-modal variant="hero" [closable]="false" [closeOnBackdrop]="false" [(isOpen)]="open"><p>Body</p></ws-modal>`,
})
class LockedHostComponent {
  open = true;
}

/**
 * La variante `hero`: una ilustración o un título centrado llegan hasta el borde superior del modal.
 * Por eso no hay cabecera con borde; la X sigue estando, flotando encima.
 */
describe('WsModal · variant hero', () => {
  afterEach(() => (document.body.style.overflow = ''));

  it('no pinta la cabecera, y la X de cerrar flota sobre el contenido', async () => {
    await TestBed.configureTestingModule({ imports: [HeroHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(HeroHostComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('.ws-modal__dialog--hero')).not.toBeNull();
    expect(el.querySelector('.ws-modal__header')).toBeNull();
    expect(el.querySelector('.ws-modal__close--floating')).not.toBeNull();
    expect(el.querySelector('.ws-modal__body .body')?.textContent).toBe('Body');
  });

  it('★ sin botón de cerrar no hay forma de esquivarlo: ni X, ni clic fuera, ni Escape', async () => {
    await TestBed.configureTestingModule({ imports: [LockedHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(LockedHostComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('.ws-modal__close')).toBeNull();

    (el.querySelector('.ws-modal__overlay') as HTMLElement).click();
    el.querySelector('ws-modal')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(fixture.componentInstance.open).toBeTrue();
    expect(el.querySelector('.ws-modal')).not.toBeNull();
  });

  it('por defecto conserva su cabecera con el título', async () => {
    await TestBed.configureTestingModule({ imports: [DefaultHostComponent] }).compileComponents();
    const fixture = TestBed.createComponent(DefaultHostComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;

    expect(el.querySelector('.ws-modal__dialog--hero')).toBeNull();
    expect(el.querySelector('.ws-modal__header .ws-modal__title')?.textContent).toBe('Title');
    expect(el.querySelector('.ws-modal__close--floating')).toBeNull();
  });
});
