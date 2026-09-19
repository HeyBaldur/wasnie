import { ApplicationRef } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TopologyFieldComponent } from './topology-field.component';

/**
 * El campo de nodos del panel de acceso.
 *
 * ★ SE MIRAN LOS PÍXELES, NO EL CÓDIGO QUE DEBERÍA PINTARLOS (§A3). Un test que comprobara que
 * existe un `<canvas>` pasaría igual con el lienzo en blanco, que es exactamente el fallo que nadie
 * ve venir en una decoración: la pantalla sale entera, sólo que sin dibujo.
 */
describe('TopologyFieldComponent', () => {
  let fixture: ComponentFixture<TopologyFieldComponent>;
  let reducedMotion: boolean;

  function mount(): void {
    fixture = TestBed.createComponent(TopologyFieldComponent);
    // El componente mide su propio hueco; sin tamaño no hay nada que medir ni dónde pintar.
    const host = fixture.nativeElement as HTMLElement;
    host.style.width = '400px';
    host.style.height = '300px';

    fixture.detectChanges();
    // `afterNextRender` corre en la fase de render, que `detectChanges` por sí solo no dispara.
    TestBed.inject(ApplicationRef).tick();
  }

  function canvas(): HTMLCanvasElement {
    return (fixture.nativeElement as HTMLElement).querySelector('canvas')!;
  }

  /** Cuántos píxeles llevan tinta: la única prueba de que el cuadro se pintó de verdad. */
  function paintedPixels(): number {
    const el = canvas();
    const data = el.getContext('2d')!.getImageData(0, 0, el.width, el.height).data;
    let painted = 0;
    for (let i = 3; i < data.length; i += 4) {
      if (data[i] > 0) painted++;
    }
    return painted;
  }

  /** Suma de un canal de color del lienzo: 0 = rojo, 1 = verde, 2 = azul. */
  function channelSum(channel: number): number {
    const el = canvas();
    const data = el.getContext('2d')!.getImageData(0, 0, el.width, el.height).data;
    let total = 0;
    for (let i = channel; i < data.length; i += 4) total += data[i];
    return total;
  }

  beforeEach(() => {
    reducedMotion = false;
    spyOn(window, 'matchMedia').and.callFake((query: string) => ({
      matches: query.includes('prefers-reduced-motion') ? reducedMotion : false,
      media: query,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      addListener: () => undefined,
      removeListener: () => undefined,
      onchange: null,
      dispatchEvent: () => false,
    }) as unknown as MediaQueryList);

    TestBed.configureTestingModule({ imports: [TopologyFieldComponent] });
  });

  afterEach(() => fixture?.destroy());

  it('pinta el campo en un lienzo del tamaño del panel', () => {
    mount();

    expect(canvas().width).toBeGreaterThan(0);
    expect(paintedPixels()).toBeGreaterThan(0);
  });

  it('deja el cuadro pintado a quien pidió menos movimiento, sin animarlo', () => {
    reducedMotion = true;
    const requestFrame = spyOn(window, 'requestAnimationFrame').and.callThrough();

    mount();

    // ★ LA IMAGEN SIGUE AHÍ. Menos movimiento significa quitar el giro, no dejar un panel vacío
    // donde el resto de la gente ve el producto.
    expect(paintedPixels()).toBeGreaterThan(0);
    expect(requestFrame).not.toHaveBeenCalled();
  });

  it('anima cuando nadie pidió lo contrario', () => {
    const requestFrame = spyOn(window, 'requestAnimationFrame').and.callThrough();

    mount();

    expect(requestFrame).toHaveBeenCalled();
  });

  it('pinta el resplandor con el violeta del logo, no con el azul de marca', async () => {
    // Quieto: así lo que cambie en el lienzo sólo puede venir del color, no del giro.
    reducedMotion = true;
    document.documentElement.style.setProperty('--color-brand', '#ff0000');
    document.documentElement.style.setProperty('--color-accent-violet', '#0000ff');
    mount();
    const red = channelSum(0);

    fixture.destroy();
    // ★ EL COLOR SE LEE AL MEDIR, así que se comprueba montando de nuevo. Antes esta prueba seguía
    // a `data-theme` porque el resplandor era `--color-brand`, que cambia con el tema; el acento
    // vale lo mismo en los tres, y atarla al tema la habría dejado verde para siempre sin probar
    // nada.
    document.documentElement.style.setProperty('--color-accent-violet', '#ff0000');
    mount();

    // Con el acento en rojo hay más rojo en el lienzo. Si el resplandor hubiera seguido tomando
    // `--color-brand` —que está en rojo en los dos montajes— este número no se movería.
    expect(channelSum(0)).toBeGreaterThan(red);

    document.documentElement.style.removeProperty('--color-brand');
    document.documentElement.style.removeProperty('--color-accent-violet');
  });

  it('suelta el bucle y el observador al destruirse', () => {
    mount();
    const cancel = spyOn(window, 'cancelAnimationFrame').and.callThrough();
    const unlisten = spyOn(document, 'removeEventListener').and.callThrough();

    fixture.destroy();

    // ★ UN rAF VIVO DESPUÉS DE DESTRUIR EL COMPONENTE PINTA SOBRE UN LIENZO QUE YA NO ESTÁ, y lo
    // hace para siempre: la pantalla de acceso se monta y desmonta en cada cierre de sesión.
    expect(cancel).toHaveBeenCalled();
    expect(unlisten).toHaveBeenCalledWith('visibilitychange', jasmine.any(Function));
  });
});
