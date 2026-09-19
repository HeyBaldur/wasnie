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
 * IDENTIFICADOR DE ORGANIZACIÓN OLVIDADO — datos corriendo por sus carriles, y uno que se resuelve.
 *
 * Lo que dice la pantalla: su correo puede existir en varios espacios de trabajo y hay que averiguar
 * cuál es el suyo. Aquí eso son muchos carriles de datos moviéndose a la vez y una columna —el
 * resolutor— por la que todo pasa: cuando un registro la cruza, se enciende y sigue. No se para
 * nada, no falla nada; se identifica y continúa, que es exactamente lo que hace esta pantalla.
 *
 * ★ CARRILES, NO CONSTELACIONES. La primera versión dibujaba islas de estrellas, y la idea (espacios
 * de trabajo separados, uno reconocido cada vez) era correcta pero se LEÍA como astronomía. Un
 * sistema que mueve registros se dibuja con registros moviéndose.
 *
 * ★ LOS CARRILES NO SE TOCAN, y eso sigue siendo deliberado: un dato que saltara de un carril a otro
 * diría lo contrario de lo que el producto garantiza — los datos de un tenant no se cruzan con los
 * de otro.
 *
 * ★ TODO SE CALCULA DESDE `time`, SIN ESTADO. La posición de cada registro es una función del
 * cuadro, así que pintar el cuadro 900 sin haber pintado el 899 da exactamente lo mismo — que es lo
 * que ocurre de verdad cada vez que la pestaña vuelve del fondo.
 */
@Component({
  selector: 'app-data-stream-field',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<canvas #canvas aria-hidden="true"></canvas>',
  styles: [FIELD_STYLES],
})
export class DataStreamFieldComponent implements OnDestroy, FieldPainter {
  /** Dónde cae la columna que resuelve, en fracción del ancho. */
  readonly resolverRatio = input(0.66);
  readonly laneCount = input(16);

  private readonly canvasRef = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly host = inject(ElementRef<HTMLElement>);
  private field?: CanvasField;

  private readonly lanes = buildLanes(this.laneCount());

  private width = 0;
  private height = 0;
  private resolver = 0;

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
    this.width = width;
    this.height = height;
    this.resolver = width * this.resolverRatio();
  }

  paint(frame: FieldFrame): void {
    const { ctx, time } = frame;

    paintGlow(frame, this.resolver, this.height * 0.45, Math.max(this.width, this.height) * 0.7, 0.18);

    ctx.globalCompositeOperation = 'lighter';

    // La columna que resuelve. Una línea y su halo: no es una barrera, es el punto por donde todo
    // pasa y se identifica.
    const column = ctx.createLinearGradient(this.resolver - 26, 0, this.resolver + 26, 0);
    column.addColorStop(0, 'rgba(255, 255, 255, 0)');
    column.addColorStop(0.5, 'rgba(255, 255, 255, 0.055)');
    column.addColorStop(1, 'rgba(255, 255, 255, 0)');
    ctx.fillStyle = column;
    ctx.fillRect(this.resolver - 26, 0, 52, this.height);

    ctx.fillStyle = 'rgba(255, 255, 255, 0.14)';
    ctx.fillRect(this.resolver, 0, 1, this.height);

    for (const lane of this.lanes) {
      const y = this.height * lane.y;
      // Los carriles de los bordes se apagan: sin ese desvanecido, el conjunto se lee como una
      // tabla con márgenes, y lo que tiene que parecer es un caudal sin principio ni final.
      const presence = 0.35 + 0.65 * Math.sin(Math.PI * lane.y);

      ctx.fillStyle = `rgba(255, 255, 255, ${(0.045 * presence).toFixed(3)})`;
      ctx.fillRect(0, y, this.width, 1);

      for (const packet of lane.packets) {
        // Cada registro recorre un carril y medio y vuelve a entrar: el desbordamiento por los dos
        // lados es lo que hace que nunca se vea el hueco por donde aparecen.
        const cursor = wrap(packet.offset + time * lane.speed);
        const x = cursor * (this.width + PACKET_MARGIN * 2) - PACKET_MARGIN;
        const length = packet.length * this.width * 0.1;

        // Cuánto le falta o le sobra para cruzar el resolutor: encendido al pasar, apagado enseguida.
        const distance = Math.abs(x - this.resolver);
        const resolved = Math.max(0, 1 - distance / (this.width * 0.06));
        const alpha = (packet.base + resolved * 0.55) * presence;

        const trail = ctx.createLinearGradient(x - length, y, x, y);
        trail.addColorStop(0, 'rgba(255, 255, 255, 0)');
        trail.addColorStop(1, `rgba(255, 255, 255, ${Math.min(1, alpha).toFixed(3)})`);
        ctx.fillStyle = trail;
        ctx.fillRect(x - length, y - 0.5, length, packet.weight);

        if (resolved > 0.02) {
          // El acuse: un pulso que se abre desde el registro justo cuando lo identifican.
          const ring = 2 + (1 - resolved) * 9;
          ctx.strokeStyle = `rgba(255, 255, 255, ${(0.3 * resolved * presence).toFixed(3)})`;
          ctx.lineWidth = 1;
          ctx.beginPath();
          ctx.arc(x, y, ring, 0, Math.PI * 2);
          ctx.stroke();

          if (frame.brand) {
            const halo = ctx.createRadialGradient(x, y, 0, x, y, 17);
            halo.addColorStop(0, `rgba(${frame.brand}, ${(0.34 * resolved).toFixed(3)})`);
            halo.addColorStop(1, `rgba(${frame.brand}, 0)`);
            ctx.fillStyle = halo;
            ctx.fillRect(x - 17, y - 17, 34, 34);
          }
        }
      }
    }

    ctx.globalCompositeOperation = 'source-over';
  }
}

/** Cuánto sobresale el caudal por cada lado del panel, en píxeles. */
const PACKET_MARGIN = 120;

function wrap(value: number): number {
  return value - Math.floor(value);
}

function buildLanes(count: number) {
  const random = seededRandom(20260919);
  const lanes = Math.max(4, count);
  return Array.from({ length: lanes }, (_, i) => ({
    // Repartidos con una desviación pequeña: una rejilla perfecta se lee como una hoja de cálculo.
    y: (i + 0.5) / lanes + (random() - 0.5) * 0.012,
    // Velocidades distintas y ninguna múltiplo de otra: así los carriles no se sincronizan nunca y
    // el caudal no forma patrones.
    speed: 0.00042 + random() * 0.00085,
    packets: Array.from({ length: 4 + Math.floor(random() * 4) }, () => ({
      offset: random(),
      length: 0.5 + random() * 1.6,
      weight: random() > 0.8 ? 2 : 1,
      base: 0.12 + random() * 0.3,
    })),
  }));
}

/** Generador con semilla: el mismo caudal en cada medida, y el mismo en cada visita. */
function seededRandom(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state * 1664525 + 1013904223) >>> 0;
    return state / 0x100000000;
  };
}
