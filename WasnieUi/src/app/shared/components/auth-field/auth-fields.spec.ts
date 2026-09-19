import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Type } from '@angular/core';
import { HorizonFieldComponent } from './horizon-field.component';
import { SweepFieldComponent } from './sweep-field.component';
import { DataStreamFieldComponent } from './data-stream-field.component';
import { readChannels, type FieldFrame, type FieldPainter } from './canvas-field';

/**
 * Los tres fondos de las otras pantallas de acceso: horizonte (registro), barrido (contraseña) y caudal
 * de datos (identificador de organización).
 *
 * ★ SE LLAMA AL DIBUJO DIRECTAMENTE, CON UN RELOJ DE MENTIRA. Montar el componente y esperar al
 * `requestAnimationFrame` haría el test lento y, sobre todo, dependiente del reloj: `time` avanza a
 * la velocidad que quiera el navegador. Pintando dos cuadros elegidos a mano se puede afirmar lo
 * único que importa de una animación — que el cuadro 120 NO es el cuadro 0 — sin esperar a nadie.
 *
 * ★ Y SE MIRAN LOS PÍXELES (§A3). Un test sobre el estado interno pasaría igual con el lienzo en
 * blanco, que es el fallo que nadie ve venir en una decoración: la pantalla sale entera, sin dibujo.
 */
describe('Fondos animados de las pantallas de acceso', () => {
  const WIDTH = 480;
  const HEIGHT = 360;

  function painterOf<T extends FieldPainter>(type: Type<T>): { painter: T; fixture: ComponentFixture<T> } {
    TestBed.configureTestingModule({ imports: [type] });
    const fixture = TestBed.createComponent(type);
    const painter = fixture.componentInstance;
    painter.layout?.(WIDTH, HEIGHT);
    return { painter, fixture };
  }

  /** Pinta un cuadro en un lienzo propio y devuelve la suma de alfa: cuánta tinta hay. */
  function inkAt(painter: FieldPainter, time: number): number {
    const canvas = document.createElement('canvas');
    canvas.width = WIDTH;
    canvas.height = HEIGHT;
    const ctx = canvas.getContext('2d')!;
    const frame: FieldFrame = { ctx, width: WIDTH, height: HEIGHT, time, brand: '31, 78, 121' };

    painter.paint(frame);

    const data = ctx.getImageData(0, 0, WIDTH, HEIGHT).data;
    let ink = 0;
    for (let i = 3; i < data.length; i += 4) ink += data[i];
    return ink;
  }

  /** Firma del cuadro: cambia si cambia cualquier píxel. */
  function signatureAt(painter: FieldPainter, time: number): string {
    const canvas = document.createElement('canvas');
    canvas.width = WIDTH;
    canvas.height = HEIGHT;
    const ctx = canvas.getContext('2d')!;
    painter.paint({ ctx, width: WIDTH, height: HEIGHT, time, brand: '31, 78, 121' });
    return canvas.toDataURL();
  }

  const FIELDS: [string, Type<FieldPainter>][] = [
    ['horizonte (registro)', HorizonFieldComponent],
    ['barrido (contraseña)', SweepFieldComponent],
    ['caudal de datos (identificador)', DataStreamFieldComponent],
  ];

  for (const [name, type] of FIELDS) {
    it(`${name} pinta algo en el primer cuadro`, () => {
      const { painter, fixture } = painterOf(type);

      expect(inkAt(painter, 0)).toBeGreaterThan(0);

      fixture.destroy();
    });

    it(`${name} se mueve: el cuadro 120 no es el cuadro 0`, () => {
      const { painter, fixture } = painterOf(type);

      expect(signatureAt(painter, 120)).not.toEqual(signatureAt(painter, 0));

      fixture.destroy();
    });
  }

  it('el caudal no se vacía nunca, por lejos que se mire', () => {
    const { painter, fixture } = painterOf(DataStreamFieldComponent);

    // ★ EL MODO DE FALLO REAL DE UN CAUDAL QUE DA LA VUELTA ES EL HUECO: un `wrap` mal puesto deja
    // el panel vacío durante unos segundos cada ciclo, y eso no se ve mirando un cuadro. Se mira
    // muy lejos en el tiempo, que es donde se acumula el error.
    for (const time of [0, 137, 900, 5000, 40000]) {
      expect(inkAt(painter, time)).toBeGreaterThan(0);
    }

    fixture.destroy();
  });

  it('el token de marca se traduce a canales, y lo que no se entiende no se pinta a medias', () => {
    expect(readChannels('#1f4e79')).toBe('31, 78, 121');
    expect(readChannels(' #abc ')).toBe('170, 187, 204');
    expect(readChannels('rgb(90, 160, 224)')).toBe('90, 160, 224');
    // Un `color-mix(...)` o cualquier otra forma que el canvas no sabría leer se descarta entera.
    expect(readChannels('color-mix(in srgb, blue 40%, black)')).toBe('');
  });
});
