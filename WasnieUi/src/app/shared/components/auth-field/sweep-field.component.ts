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
import { CanvasField, FIELD_STYLES, paintGlow, type FieldFrame, type FieldPainter } from './canvas-field';

/**
 * CONTRASEÑA OLVIDADA — un barrido que recorre el campo y va encendiendo lo que encuentra.
 *
 * Lo que dice la pantalla: hay una búsqueda en marcha y va a terminar bien. El haz gira sin prisa,
 * los puntos se encienden a su paso y se apagan despacio detrás; nada parpadea ni avisa de nada.
 * Es lo contrario de una alarma: es una comprobación tranquila y en curso.
 *
 * ★ UN BARRIDO, NO UN CANDADO. La tentación es dibujar una cerradura; un icono de seguridad en la
 * pantalla de «he perdido la contraseña» habla de riesgo justo cuando quien mira ya está nervioso.
 *
 * ★ LOS PUNTOS SE SIEMBRAN UNA VEZ, en coordenadas polares normalizadas. Guardarlos en píxeles
 * obligaría a re-sembrarlos en cada `resize` y el campo entero cambiaría de sitio al mover la
 * ventana.
 */
@Component({
  selector: 'app-sweep-field',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<canvas #canvas aria-hidden="true"></canvas>',
  styles: [FIELD_STYLES],
})
export class SweepFieldComponent implements OnDestroy, FieldPainter {
  /** Dónde cae el origen del barrido, en fracciones del panel. */
  readonly centerXRatio = input(0.62);
  readonly centerYRatio = input(0.46);
  readonly pointCount = input(230);

  private readonly canvasRef = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly host = inject(ElementRef<HTMLElement>);
  private field?: CanvasField;

  /** Cada punto, en polares normalizadas respecto del origen del barrido. */
  private readonly points = buildPoints(this.pointCount());

  private centerX = 0;
  private centerY = 0;
  private reach = 0;

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
    this.centerX = width * this.centerXRatio();
    this.centerY = height * this.centerYRatio();
    // ★ EL ALCANCE SE AJUSTA AL PANEL, NO A LA DIAGONAL ENTERA. Con el disco desbordando, casi
    // todos los puntos caían fuera del lienzo y el campo se veía vacío: mucho haz y nada que
    // encender.
    this.reach = Math.hypot(width, height) * 0.62;
  }

  paint(frame: FieldFrame): void {
    const { ctx, time } = frame;
    const beam = time * 0.006;

    paintGlow(frame, this.centerX, this.centerY, this.reach * 0.75, 0.2);

    ctx.globalCompositeOperation = 'lighter';

    // Los anillos de alcance. Marcan la distancia sin números: esto no es un panel de control.
    for (const ring of [0.3, 0.55, 0.8, 1.05]) {
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.07)';
      ctx.lineWidth = 1;
      ctx.beginPath();
      ctx.arc(this.centerX, this.centerY, this.reach * ring, 0, Math.PI * 2);
      ctx.stroke();
    }

    // El haz: un abanico que se desvanece hacia atrás, con el borde de ataque marcado. El relleno
    // usa el azul de marca; el filo, blanco — es lo que da la sensación de que algo PASA por ahí.
    if (frame.brand) {
      // ★ EN TIRAS, NO EN UNA CUÑA. Una sola cuña con degradado radial se apaga hacia AFUERA pero
      // deja los dos lados rectos: el haz se ve como un triángulo pegado encima. Doce tiras con el
      // alfa cayendo hacia atrás dan la cola que hace que el haz parezca girar.
      const slices = 26;
      for (let i = 0; i < slices; i++) {
        const from = beam - BEAM_WIDTH * ((i + 1) / slices);
        const to = beam - BEAM_WIDTH * (i / slices);
        const fade = (1 - i / slices) ** 1.7;
        const wedge = ctx.createRadialGradient(
          this.centerX, this.centerY, 0,
          this.centerX, this.centerY, this.reach,
        );
        wedge.addColorStop(0, `rgba(${frame.brand}, ${(0.075 * fade).toFixed(3)})`);
        wedge.addColorStop(0.7, `rgba(${frame.brand}, ${(0.04 * fade).toFixed(3)})`);
        wedge.addColorStop(1, `rgba(${frame.brand}, 0)`);
        ctx.fillStyle = wedge;
        ctx.beginPath();
        ctx.moveTo(this.centerX, this.centerY);
        ctx.arc(this.centerX, this.centerY, this.reach, from, to + BEAM_WIDTH / slices);
        ctx.closePath();
        ctx.fill();
      }
    }

    const edge = {
      x: this.centerX + Math.cos(beam) * this.reach,
      y: this.centerY + Math.sin(beam) * this.reach,
    };
    const edgeGradient = ctx.createLinearGradient(this.centerX, this.centerY, edge.x, edge.y);
    edgeGradient.addColorStop(0, 'rgba(255, 255, 255, 0.18)');
    edgeGradient.addColorStop(1, 'rgba(255, 255, 255, 0)');
    ctx.strokeStyle = edgeGradient;
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.moveTo(this.centerX, this.centerY);
    ctx.lineTo(edge.x, edge.y);
    ctx.stroke();

    for (const point of this.points) {
      const distance = point.radius * this.reach;
      const x = this.centerX + Math.cos(point.angle) * distance;
      const y = this.centerY + Math.sin(point.angle) * distance;

      // Cuánto hace que el haz pasó por encima de este punto, en radianes hacia atrás. El encendido
      // es inmediato y el apagado, largo: así el rastro se lee como memoria del barrido y no como
      // un parpadeo.
      const behind = wrap(beam - point.angle);
      const lit = Math.max(0, 1 - behind / AFTERGLOW);
      const alpha = point.base * (0.25 + lit * 0.95);

      ctx.fillStyle = `rgba(255, 255, 255, ${alpha.toFixed(3)})`;
      ctx.beginPath();
      ctx.arc(x, y, point.size * (0.85 + lit * 0.9), 0, Math.PI * 2);
      ctx.fill();
    }

    ctx.globalCompositeOperation = 'source-over';
  }
}

/** Ancho del abanico y cuánto tarda un punto en apagarse detrás del haz, ambos en radianes. */
const BEAM_WIDTH = 0.55;
const AFTERGLOW = 2.2;

function wrap(angle: number): number {
  const full = Math.PI * 2;
  return ((angle % full) + full) % full;
}

function buildPoints(count: number) {
  const random = seededRandom(20260919);
  return Array.from({ length: Math.max(8, count) }, () => ({
    angle: random() * Math.PI * 2,
    // Raíz cuadrada: reparte los puntos por ÁREA. Sin ella se apelotonan todos junto al origen.
    radius: Math.sqrt(random()) * 1.05,
    size: 0.9 + random() * 1.6,
    base: 0.45 + random() * 0.5,
  }));
}

/** Generador con semilla: el mismo campo en cada medida, y el mismo en cada visita. */
function seededRandom(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state * 1664525 + 1013904223) >>> 0;
    return state / 0x100000000;
  };
}
