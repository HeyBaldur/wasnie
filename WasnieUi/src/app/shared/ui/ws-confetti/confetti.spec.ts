import { launchConfetti } from './confetti';

describe('launchConfetti', () => {
  const canvas = (): HTMLCanvasElement | null => document.querySelector('canvas.ws-confetti');

  beforeEach(() => document.documentElement.style.setProperty('--color-accent-violet', '#7c3aed'));
  afterEach(() => {
    document.querySelectorAll('canvas.ws-confetti').forEach(c => c.remove());
    document.documentElement.style.removeProperty('--color-accent-violet');
  });

  it('pinta un canvas a pantalla completa que no bloquea clics ni se anuncia, y detenerlo lo retira', () => {
    const stop = launchConfetti();

    expect(canvas()).not.toBeNull();
    expect(canvas()!.style.position).toBe('fixed');
    expect(canvas()!.style.pointerEvents).toBe('none');
    expect(canvas()!.getAttribute('aria-hidden')).toBe('true');

    stop();
    expect(canvas()).toBeNull();
    stop(); // idempotente
  });

  it('un confeti largo sigue en pantalla pasado el primer disparo, y se retira solo al terminar', async () => {
    launchConfetti({ durationMs: 900 });
    await new Promise(resolve => setTimeout(resolve, 300));
    expect(canvas()).not.toBeNull();

    await new Promise(resolve => setTimeout(resolve, 1100));
    expect(canvas()).toBeNull();
  });

  it('★ quien pidió menos movimiento no ve confeti', () => {
    spyOn(window, 'matchMedia').and.returnValue({ matches: true } as MediaQueryList);

    launchConfetti()();

    expect(canvas()).toBeNull();
  });

  it('sin colores resueltos no pinta nada (nunca un confeti negro por defecto)', () => {
    launchConfetti({ colorTokens: ['--color-que-no-existe'] })();

    expect(canvas()).toBeNull();
  });
});
