import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  afterNextRender,
  inject,
  input,
  viewChild,
} from '@angular/core';
import { CanvasField, FIELD_STYLES, readChannels, type FieldFrame, type FieldPainter } from './canvas-field';

/**
 * REGISTRO — el amanecer sobre el borde de un planeta.
 *
 * Un arco enorme cruza el panel en diagonal: por dentro, el cuerpo oscuro; por fuera, la atmósfera
 * encendida, con un punto caliente que recorre el filo despacio. Es una sola forma y casi nada de
 * detalle, que es de donde sale la sensación de escala.
 *
 * Lo que dice la pantalla: algo empieza. Ninguna metáfora de producto —ni órbitas, ni una ciudad, ni
 * un mapa de rutas— decía eso tan bien como un amanecer, y las tres se quedaron por el camino
 * intentándolo.
 *
 * ★ EL CENTRO DEL CÍRCULO ESTÁ MUY LEJOS DEL LIENZO. Un arco cuyo centro se ve se lee como una bola
 * en una caja; con el centro fuera y un radio mayor que el panel, la curvatura es casi recta y lo
 * que se ve es el borde de algo enorme. Es el mismo principio que el planeta del acceso, llevado
 * más lejos.
 *
 * ★ LOS VIOLETAS SON LOS DEL LOGO, no un naranja de referencia. El resplandor es lo más saturado de
 * toda la aplicación y es lo único que lo autoriza: si no fuera el color de la marca, sería
 * decoración que contradice al resto del producto.
 *
 * ★ EL CUERPO SE PINTA DESPUÉS DEL RESPLANDOR. El halo se dibuja a brochazos anchos a los dos lados
 * del filo y luego el disco lo tapa por dentro: así el borde sale limpio como un corte, sin tener
 * que recortar nada ni depender de `ctx.filter`, que no está en todos los navegadores.
 */
@Component({
  selector: 'app-horizon-field',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<canvas #canvas aria-hidden="true"></canvas>',
  styles: [FIELD_STYLES],
})
export class HorizonFieldComponent implements OnDestroy, FieldPainter {
  /** Centro del planeta, en fracciones del panel. Fuera del lienzo a propósito. */
  readonly centerXRatio = input(-0.32);
  readonly centerYRatio = input(1.42);
  readonly starCount = input(90);

  private readonly canvasRef = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly host = inject(ElementRef<HTMLElement>);
  private field?: CanvasField;

  private readonly stars = buildStars(this.starCount());

  private centerX = 0;
  private centerY = 0;
  /** Las estrellas viven en coordenadas normalizadas; esto las lleva a píxeles sin re-sembrarlas. */
  private lastWidth = 0;
  private lastHeight = 0;
  private radius = 0;
  /** El arco visible, en radianes: sólo se recorre el tramo que cruza el panel. */
  private fromAngle = 0;
  private toAngle = 0;
  private violet = '';
  private violetSoft = '';

  constructor() {
    afterNextRender(() => {
      this.field = new CanvasField(this.host.nativeElement, this.canvasRef().nativeElement, this);
      this.field.start();
    });
  }

  ngOnDestroy(): void {
    this.field?.stop();
  }

  layout(width: number, height: number): void {
    this.lastWidth = width;
    this.lastHeight = height;
    this.centerX = width * this.centerXRatio();
    this.centerY = height * this.centerYRatio();

    // El radio se ajusta para que el filo pase entre las dos esquinas que cruza: la de arriba a la
    // izquierda y la de abajo a la derecha. Así el arco entra y sale del panel siempre, sea cual
    // sea la forma de la ventana.
    const toTopLeft = Math.hypot(0 - this.centerX, 0 - this.centerY);
    const toBottomRight = Math.hypot(width - this.centerX, height - this.centerY);
    this.radius = (toTopLeft + toBottomRight) / 2;

    // Sólo se dibuja el tramo que puede verse; el resto del círculo es trabajo tirado.
    const corners: [number, number][] = [[0, 0], [width, 0], [0, height], [width, height]];
    const angles = corners.map(([x, y]) => Math.atan2(y - this.centerY, x - this.centerX));
    this.fromAngle = Math.min(...angles) - 0.15;
    this.toAngle = Math.max(...angles) + 0.15;

    // Los acentos valen lo mismo en los tres temas, así que basta con leerlos al medir.
    const styles = getComputedStyle(this.host.nativeElement);
    this.violet = readChannels(styles.getPropertyValue('--color-accent-violet'));
    this.violetSoft = readChannels(styles.getPropertyValue('--color-accent-violet-soft'));
  }

  paint(frame: FieldFrame): void {
    const { ctx, time } = frame;
    if (!this.violet || !this.violetSoft) return;

    // El punto caliente recorre el filo, va y vuelve. Nunca llega a los extremos: el amanecer
    // ocurre en el trozo de borde que se ve, no entra y sale de cuadro.
    const travel = (Math.sin(time * 0.0016) + 1) / 2;
    const hotAngle = this.fromAngle + (this.toAngle - this.fromAngle) * (0.28 + travel * 0.44);
    const hot = {
      x: this.centerX + Math.cos(hotAngle) * this.radius,
      y: this.centerY + Math.sin(hotAngle) * this.radius,
    };
    // Una respiración larguísima: el resplandor nunca está dos veces igual, y no se nota mirándolo.
    const breath = 0.88 + 0.12 * Math.sin(time * 0.006);

    ctx.globalCompositeOperation = 'lighter';

    this.paintStars(ctx, time);

    // La atmósfera: capas cada vez más anchas y más tenues sobre el mismo filo. Lo que en un editor
    // sería un desenfoque gaussiano, aquí son seis brochazos — y a cambio funciona en todas partes.
    const halo = [
      { width: this.radius * 0.055, alpha: 0.1 },
      { width: this.radius * 0.032, alpha: 0.14 },
      { width: this.radius * 0.018, alpha: 0.2 },
      { width: this.radius * 0.009, alpha: 0.3 },
      { width: this.radius * 0.004, alpha: 0.45 },
    ];
    for (const layer of halo) {
      ctx.lineWidth = layer.width;
      ctx.strokeStyle = this.limbGradient(ctx, hot, layer.alpha * breath);
      ctx.beginPath();
      ctx.arc(this.centerX, this.centerY, this.radius, this.fromAngle, this.toAngle);
      ctx.stroke();
    }

    // El cuerpo del planeta, encima del halo: es lo que convierte una línea difusa en un BORDE.
    ctx.globalCompositeOperation = 'source-over';
    ctx.fillStyle = 'rgba(0, 0, 0, 0.94)';
    ctx.beginPath();
    ctx.arc(this.centerX, this.centerY, this.radius, 0, Math.PI * 2);
    ctx.fill();

    ctx.globalCompositeOperation = 'lighter';

    // El filo. Dos trazos: el color de marca y, encima y más fino, el blanco del núcleo.
    ctx.lineWidth = Math.max(1.6, this.radius * 0.0022);
    ctx.strokeStyle = this.limbGradient(ctx, hot, 0.85 * breath);
    ctx.beginPath();
    ctx.arc(this.centerX, this.centerY, this.radius, this.fromAngle, this.toAngle);
    ctx.stroke();

    ctx.lineWidth = Math.max(0.8, this.radius * 0.0009);
    ctx.strokeStyle = this.coreGradient(ctx, hot, breath);
    ctx.beginPath();
    ctx.arc(this.centerX, this.centerY, this.radius, this.fromAngle, this.toAngle);
    ctx.stroke();

    // El derrame del punto caliente sobre el cielo. Es lo último y lo que da el aire de fotografía:
    // la luz no se queda dentro de la línea.
    const bloom = ctx.createRadialGradient(hot.x, hot.y, 0, hot.x, hot.y, this.radius * 0.42);
    bloom.addColorStop(0, `rgba(${this.violetSoft}, ${(0.3 * breath).toFixed(3)})`);
    bloom.addColorStop(0.35, `rgba(${this.violet}, ${(0.12 * breath).toFixed(3)})`);
    bloom.addColorStop(1, `rgba(${this.violet}, 0)`);
    ctx.fillStyle = bloom;
    ctx.fillRect(0, 0, frame.width, frame.height);

    ctx.globalCompositeOperation = 'source-over';
  }

  /**
   * El degradado que recorre el filo: encendido junto al punto caliente y apagándose a los lados.
   * Es radial y centrado en el punto caliente, así que basta con moverlo para que el amanecer
   * avance por el borde.
   */
  private limbGradient(
    ctx: CanvasRenderingContext2D,
    hot: { x: number; y: number },
    alpha: number,
  ): CanvasGradient {
    const gradient = ctx.createRadialGradient(hot.x, hot.y, 0, hot.x, hot.y, this.radius * 0.85);
    gradient.addColorStop(0, `rgba(${this.violetSoft}, ${alpha.toFixed(3)})`);
    gradient.addColorStop(0.35, `rgba(${this.violet}, ${(alpha * 0.55).toFixed(3)})`);
    gradient.addColorStop(1, `rgba(${this.violet}, 0)`);
    return gradient;
  }

  private coreGradient(
    ctx: CanvasRenderingContext2D,
    hot: { x: number; y: number },
    breath: number,
  ): CanvasGradient {
    const gradient = ctx.createRadialGradient(hot.x, hot.y, 0, hot.x, hot.y, this.radius * 0.45);
    gradient.addColorStop(0, `rgba(255, 255, 255, ${(0.95 * breath).toFixed(3)})`);
    gradient.addColorStop(0.3, `rgba(${this.violetSoft}, ${(0.5 * breath).toFixed(3)})`);
    gradient.addColorStop(1, `rgba(${this.violetSoft}, 0)`);
    return gradient;
  }

  /** Estrellas: pocas y tenues. El planeta las tapa porque se pinta encima. */
  private paintStars(ctx: CanvasRenderingContext2D, time: number): void {
    for (const star of this.stars) {
      const twinkle = 0.55 + 0.45 * Math.sin(time * star.speed + star.phase);
      ctx.fillStyle = `rgba(255, 255, 255, ${(star.base * twinkle).toFixed(3)})`;
      ctx.fillRect(star.x * this.lastWidth, star.y * this.lastHeight, star.size, star.size);
    }
  }
}

function buildStars(count: number) {
  const random = seededRandom(20260919);
  return Array.from({ length: Math.max(8, count) }, () => ({
    x: random(),
    y: random(),
    size: random() > 0.85 ? 1.6 : 1,
    base: 0.15 + random() * 0.35,
    speed: 0.004 + random() * 0.012,
    phase: random() * Math.PI * 2,
  }));
}

/** Generador con semilla: el mismo cielo en cada medida, y el mismo en cada visita. */
function seededRandom(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state * 1664525 + 1013904223) >>> 0;
    return state / 0x100000000;
  };
}
