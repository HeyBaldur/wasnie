/**
 * El motor que comparten los fondos animados de las pantallas de acceso.
 *
 * Cada pantalla dibuja una cosa distinta —una esfera, una órbita, un barrido, un archipiélago— pero
 * todas necesitan exactamente lo mismo por debajo: medirse, seguir al tema, no pintar cuando nadie
 * mira y soltarlo todo al desmontarse.
 *
 * ★ ESTO ES UNA CLASE, NO UN COMPONENTE BASE. Heredar de un componente de Angular arrastra su
 * plantilla, sus estilos y su ciclo de vida a cada hijo; aquí lo único que se comparte es la
 * fontanería del lienzo, y lo que cada pantalla aporta es una función que pinta.
 *
 * ★ LAS CUATRO REGLAS QUE SE CUMPLEN UNA SOLA VEZ, PARA TODAS:
 *   1. `prefers-reduced-motion` deja un cuadro fijo pintado, nunca un panel vacío.
 *   2. Con la pestaña de fondo el bucle se detiene (batería).
 *   3. El color de marca se relee al cambiar de tema, no sólo al redimensionar.
 *   4. Al parar se sueltan el rAF y los dos observadores; la pantalla de acceso se monta y
 *      desmonta en cada cierre de sesión, y un bucle vivo pintaría para siempre sobre un lienzo
 *      que ya no está.
 */

/** Lo que el motor le da al dibujo en cada cuadro. */
export interface FieldFrame {
  readonly ctx: CanvasRenderingContext2D;
  readonly width: number;
  readonly height: number;
  /** Cuadros transcurridos. El dibujo decide a qué velocidad traduce eso en movimiento. */
  readonly time: number;
  /** El azul de marca del tema activo, como `'r, g, b'`, listo para `rgba(...)`. Vacío si no se pudo leer. */
  readonly brand: string;
}

export interface FieldPainter {
  /** Se llama en cada medida (montaje y `resize`), antes de pintar. */
  layout?(width: number, height: number): void;
  paint(frame: FieldFrame): void;
}

export class CanvasField {
  private frame = 0;
  private time = 0;
  private context: CanvasRenderingContext2D | null = null;
  private resizeObserver?: ResizeObserver;
  private themeObserver?: MutationObserver;
  private width = 0;
  private height = 0;
  private brand = '';
  private readonly onVisibility = (): void => this.sync();

  constructor(
    private readonly host: HTMLElement,
    private readonly canvas: HTMLCanvasElement,
    private readonly painter: FieldPainter,
  ) {}

  start(): void {
    this.context = this.canvas.getContext('2d');
    if (!this.context) return;

    this.measure();

    this.resizeObserver = new ResizeObserver(() => {
      this.measure();
      // Con el bucle parado nadie repintaría el tamaño nuevo, y el lienzo se quedaría con la
      // imagen estirada del anterior.
      if (!this.frame) this.render();
    });
    this.resizeObserver.observe(this.host);

    this.themeObserver = new MutationObserver(() => {
      this.readBrand();
      if (!this.frame) this.render();
    });
    this.themeObserver.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme'],
    });

    document.addEventListener('visibilitychange', this.onVisibility);

    this.render();
    this.sync();
  }

  stop(): void {
    cancelAnimationFrame(this.frame);
    this.frame = 0;
    this.resizeObserver?.disconnect();
    this.themeObserver?.disconnect();
    document.removeEventListener('visibilitychange', this.onVisibility);
  }

  private measure(): void {
    const rect = this.host.getBoundingClientRect();
    const ratio = Math.min(window.devicePixelRatio || 1, 2);

    this.width = Math.max(1, Math.round(rect.width));
    this.height = Math.max(1, Math.round(rect.height));
    this.canvas.width = Math.round(this.width * ratio);
    this.canvas.height = Math.round(this.height * ratio);
    this.context?.setTransform(ratio, 0, 0, ratio, 0, 0);

    this.readBrand();
    this.painter.layout?.(this.width, this.height);
  }

  private readBrand(): void {
    this.brand = readChannels(getComputedStyle(this.host).getPropertyValue('--color-brand'));
  }

  private sync(): void {
    const reduced = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    if (reduced || document.hidden) {
      cancelAnimationFrame(this.frame);
      this.frame = 0;
      return;
    }
    if (this.frame) return;

    const loop = (): void => {
      this.time += 1;
      this.render();
      this.frame = requestAnimationFrame(loop);
    };
    this.frame = requestAnimationFrame(loop);
  }

  private render(): void {
    const ctx = this.context;
    if (!ctx) return;
    ctx.clearRect(0, 0, this.width, this.height);
    this.painter.paint({
      ctx,
      width: this.width,
      height: this.height,
      time: this.time,
      brand: this.brand,
    });
  }
}

/**
 * El token de marca, reducido a los tres canales que un degradado de canvas sabe leer con alfa.
 *
 * ★ NO SE LE PASA EL TOKEN CRUDO AL CANVAS. `color-mix(...)` como parada de degradado depende del
 * navegador, y una parada que el motor no entiende no falla: se ignora en silencio y el resplandor
 * desaparece sin un solo error en consola. Si el token llegara en un formato que esto no reconoce,
 * el degradado se omite entero en lugar de pintarse a medias.
 */
export function readChannels(token: string): string {
  const value = token.trim();

  const hex = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(value);
  if (hex) {
    const digits = hex[1].length === 3 ? hex[1].replace(/./g, (d) => d + d) : hex[1];
    const int = Number.parseInt(digits, 16);
    return `${(int >> 16) & 255}, ${(int >> 8) & 255}, ${int & 255}`;
  }

  const rgb = /^rgba?\(([^)]+)\)$/i.exec(value);
  if (rgb) {
    const parts = rgb[1].split(/[\s,/]+/).filter(Boolean).slice(0, 3);
    if (parts.length === 3 && parts.every((part) => /^\d+(\.\d+)?$/.test(part))) {
      return parts.map((part) => Math.round(Number(part))).join(', ');
    }
  }

  return '';
}

/**
 * El resplandor que separa el dibujo del fondo del panel. Compartido por los cuatro.
 *
 * `channels` permite pintarlo con otro color que el azul de marca —el acceso y el alta usan el
 * violeta del logo— sin que cada campo tenga que repetir el degradado.
 */
export function paintGlow(
  frame: FieldFrame,
  centerX: number,
  centerY: number,
  radius: number,
  strength = 0.22,
  channels = frame.brand,
): void {
  if (!channels || radius <= 0) return;
  const glow = frame.ctx.createRadialGradient(centerX, centerY, 0, centerX, centerY, radius);
  glow.addColorStop(0, `rgba(${channels}, ${strength})`);
  glow.addColorStop(0.55, `rgba(${channels}, ${(strength * 0.32).toFixed(3)})`);
  glow.addColorStop(1, `rgba(${channels}, 0)`);
  frame.ctx.fillStyle = glow;
  frame.ctx.fillRect(0, 0, frame.width, frame.height);
}

/** Estilos comunes de los cuatro componentes de fondo. */
export const FIELD_STYLES = `
  :host {
    display: block;
    position: relative;
    overflow: hidden;
    pointer-events: none;
  }

  canvas {
    display: block;
    width: 100%;
    height: 100%;
  }
`;
