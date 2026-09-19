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
import {
  CanvasField,
  FIELD_STYLES,
  paintGlow,
  readChannels,
  type FieldFrame,
  type FieldPainter,
} from '../auth-field/canvas-field';

/**
 * ACCESO — el borde de un planeta de nodos enlazados, girando despacio.
 *
 * Lo que dice: una red viva a la que se entra. El centro de la esfera vive FUERA del lienzo, a la
 * derecha, así que lo que se ve es la silueta de algo mucho mayor que el panel — escala, no adorno.
 *
 * ★ SIN LIBRERÍA Y SIN IFRAME. El original de esta idea es un componente React que mete un
 * `<iframe srcDoc>` cargando three.js, Tailwind e Iconify desde CDN. Aquí eso no entra por dos
 * motivos independientes: el CSP de la SPA es `script-src 'self'` (KAN-21) y lo bloquearía entero, y
 * una pantalla de acceso de software financiero no va a pedirle scripts a tres dominios ajenos. Un
 * canvas y sesenta líneas de proyección dan la misma imagen y no añaden un kilobyte de dependencia.
 *
 * La fontanería (medida, tema, movimiento reducido, pestaña de fondo) vive en `CanvasField`.
 */
@Component({
  selector: 'app-topology-field',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<canvas #canvas aria-hidden="true"></canvas>',
  styles: [FIELD_STYLES],
})
export class TopologyFieldComponent implements OnDestroy, FieldPainter {
  /** Cuántos nodos tiene la esfera. El número de enlaces crece solo con la distancia entre ellos. */
  readonly nodeCount = input(170);

  /**
   * Radio de la esfera como fracción del lado LARGO del panel.
   *
   * ★ DEL LADO LARGO, NO DEL CORTO. Atado al lado corto, el recorte dependería de la forma de la
   * ventana y en pantallas anchas se quedaría en una bola pequeña.
   */
  readonly radiusRatio = input(0.74);

  /**
   * Dónde cae el centro de la esfera, en fracciones del ancho y del alto del panel.
   *
   * ★ EL CENTRO VIVE FUERA DEL LIENZO, A LA DERECHA (x > 1). Lo que se ve entonces no es una bola
   * dentro de un recuadro sino el BORDE de un planeta: la silueta entra por arriba, se curva y sale
   * por abajo, y el resto del panel queda oscuro.
   */
  readonly centerXRatio = input(1.06);
  readonly centerYRatio = input(0.46);

  private readonly canvasRef = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly host = inject(ElementRef<HTMLElement>);
  private field?: CanvasField;

  private points: { x: number; y: number; z: number; size: number; speed: number; phase: number }[] = [];
  private edges: { a: number; b: number; strength: number }[] = [];
  private radius = 0;
  /**
   * El violeta del logo, para el resplandor del planeta.
   *
   * ★ EL MISMO QUE EL AMANECER DEL ALTA, y a propósito: las dos pantallas que dan entrada al
   * producto comparten el color de la marca. El azul se queda en las de recuperación, que son
   * utilitarias.
   *
   * ★ SE LEE AL MEDIR Y NO HACE FALTA RELEERLO AL CAMBIAR DE TEMA: los acentos valen lo mismo en
   * los tres temas, a diferencia de `--color-brand`.
   */
  private violet = '';

  constructor() {
    afterNextRender(() => {
      this.build();
      this.field = new CanvasField(this.host.nativeElement, this.canvasRef().nativeElement, this);
      this.field.start();
    });
  }

  ngOnDestroy(): void {
    this.field?.stop();
  }

  layout(width: number, height: number): void {
    this.radius = Math.max(width, height) * this.radiusRatio();
    this.violet = readChannels(
      getComputedStyle(this.host.nativeElement).getPropertyValue('--color-accent-violet'),
    );
  }

  /**
   * La esfera de Fibonacci: nodos repartidos por igual sobre la superficie, sin los polos apretados
   * que deja un reparto por latitud y longitud.
   */
  private build(): void {
    const count = Math.max(8, this.nodeCount());
    this.points = [];
    for (let i = 0; i < count; i++) {
      const phi = Math.acos(-1 + (2 * i) / count);
      const theta = Math.sqrt(count * Math.PI) * phi;
      this.points.push({
        x: Math.cos(theta) * Math.sin(phi),
        y: Math.sin(theta) * Math.sin(phi),
        z: Math.cos(phi),
        // Uno de cada ocho es un «faro»: la malla es menuda con un puñado de nodos claramente
        // mayores, que es lo que le da textura en vez de ruido uniforme.
        size: (Math.random() * 1.1 + 0.7) * (i % 8 === 0 ? 2.4 : 1),
        speed: Math.random() * 0.02 + 0.015,
        phase: Math.random() * Math.PI * 2,
      });
    }

    // Los enlaces se calculan UNA vez, sobre las posiciones sin girar: girar la esfera no cambia qué
    // nodos son vecinos, y recalcular 14 000 distancias por cuadro sería tirar el presupuesto de
    // pintado en una decoración.
    const threshold = 0.45;
    this.edges = [];
    for (let i = 0; i < this.points.length; i++) {
      for (let j = i + 1; j < this.points.length; j++) {
        const a = this.points[i];
        const b = this.points[j];
        const distance = Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
        if (distance < threshold) {
          this.edges.push({ a: i, b: j, strength: 1 - distance / threshold });
        }
      }
    }
  }

  paint(frame: FieldFrame): void {
    const { ctx, time } = frame;
    const centerX = frame.width * this.centerXRatio();
    const centerY = frame.height * this.centerYRatio();
    paintGlow(frame, centerX, centerY, this.radius * 1.6, 0.22, this.violet);

    // Giro: lento en el eje vertical, con la inclinación fija que enseña el polo, más una deriva
    // mínima en el plano para que dos vueltas nunca se vean iguales.
    const yaw = time * 0.0018;
    const roll = time * 0.0006;
    const pitch = 0.2;

    const projected = this.points.map((point) => {
      const x1 = point.x * Math.cos(yaw) + point.z * Math.sin(yaw);
      const z1 = point.z * Math.cos(yaw) - point.x * Math.sin(yaw);
      const y2 = point.y * Math.cos(pitch) - z1 * Math.sin(pitch);
      const z2 = z1 * Math.cos(pitch) + point.y * Math.sin(pitch);
      const x3 = x1 * Math.cos(roll) - y2 * Math.sin(roll);
      const y3 = y2 * Math.cos(roll) + x1 * Math.sin(roll);

      // Perspectiva: el hemisferio de delante se agranda y el de detrás se encoge. Sin esto la
      // esfera se lee como un disco.
      const distance = this.radius * 2.2;
      const scale = distance / (distance - z2 * this.radius);

      return {
        x: centerX + x3 * this.radius * scale,
        y: centerY + y3 * this.radius * scale,
        depth: (z2 + 1) / 2,
        scale,
        node: point,
      };
    });

    // Aditivo: donde se cruzan varios enlaces la luz se suma, que es lo que da el brillo del núcleo.
    ctx.globalCompositeOperation = 'lighter';

    ctx.lineWidth = 1;
    for (const edge of this.edges) {
      const a = projected[edge.a];
      const b = projected[edge.b];
      const depth = (a.depth + b.depth) / 2;
      const alpha = edge.strength * 0.4 * (0.35 + depth * 0.65);
      ctx.strokeStyle = `rgba(255, 255, 255, ${alpha.toFixed(3)})`;
      ctx.beginPath();
      ctx.moveTo(a.x, a.y);
      ctx.lineTo(b.x, b.y);
      ctx.stroke();
    }

    for (const point of projected) {
      const pulse = (Math.sin(time * point.node.speed + point.node.phase) + 1) / 2;
      const size = (point.node.size + pulse * 1.6) * point.scale;
      const alpha = (0.3 + pulse * 0.6) * (0.35 + point.depth * 0.65);

      ctx.fillStyle = `rgba(255, 255, 255, ${alpha.toFixed(3)})`;
      ctx.beginPath();
      ctx.arc(point.x, point.y, size, 0, Math.PI * 2);
      ctx.fill();
    }

    ctx.globalCompositeOperation = 'source-over';
  }
}
