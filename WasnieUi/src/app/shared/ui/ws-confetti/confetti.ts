/**
 * Confeti a pantalla completa, para un momento contado: la bienvenida después de registrarse.
 *
 * ★ SIN LIBRERÍA. Un canvas, unas decenas de líneas de física y `requestAnimationFrame`: una
 * dependencia entera por tres segundos de celebración no se justifica.
 *
 * ★ LOS COLORES SON LOS DE LA MARCA, leídos de los tokens (`--color-accent-*`, `--color-brand`) en el
 * momento de lanzarlo — así sigue al tema activo y no hay un solo hex aquí.
 *
 * ★ QUIEN PIDIÓ MENOS MOVIMIENTO NO LO VE (`prefers-reduced-motion`). El modal que lo acompaña dice lo
 * mismo sin él.
 *
 * ★ NUNCA ESTORBA: `pointer-events: none` y `aria-hidden`. Se pinta por encima del modal (z-index 1100
 * sobre los 1000 de `ws-modal`) pero los clics lo atraviesan, y se retira solo al terminar.
 *
 * @returns una función que lo detiene y retira el canvas en el acto (idempotente).
 */
export interface ConfettiOptions {
  /** Tokens de color (custom properties). Por defecto, el par violeta/azul y el azul de marca. */
  colorTokens?: readonly string[];
  durationMs?: number;
  particleCount?: number;
}

const DEFAULT_TOKENS = [
  '--color-accent-violet',
  '--color-accent-blue',
  '--color-accent-violet-soft',
  '--color-accent-blue-soft',
  '--color-brand',
] as const;

interface Particle {
  x: number;
  y: number;
  vx: number;
  vy: number;
  rotation: number;
  spin: number;
  wobble: number;
  wobbleSpeed: number;
  width: number;
  height: number;
  color: string;
  round: boolean;
}

const NOOP = (): void => undefined;

export function launchConfetti(options: ConfettiOptions = {}): () => void {
  if (typeof window === 'undefined' || typeof document === 'undefined') return NOOP;
  if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return NOOP;

  const styles = getComputedStyle(document.documentElement);
  const colors = (options.colorTokens ?? DEFAULT_TOKENS)
    .map(token => styles.getPropertyValue(token).trim())
    .filter(Boolean);
  if (colors.length === 0) return NOOP;

  const canvas = document.createElement('canvas');
  canvas.className = 'ws-confetti';
  canvas.setAttribute('aria-hidden', 'true');
  Object.assign(canvas.style, {
    position: 'fixed',
    inset: '0',
    width: '100%',
    height: '100%',
    pointerEvents: 'none',
    zIndex: '1100',
  });
  document.body.appendChild(canvas);

  const ctx = canvas.getContext('2d');
  if (!ctx) {
    canvas.remove();
    return NOOP;
  }

  const dpr = Math.min(window.devicePixelRatio || 1, 2);
  const resize = (): void => {
    canvas.width = window.innerWidth * dpr;
    canvas.height = window.innerHeight * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  };
  resize();
  window.addEventListener('resize', resize);

  const width = window.innerWidth;
  const height = window.innerHeight;
  // La velocidad escala con la pantalla: en un portátil y en un monitor grande llega a la misma altura.
  const scale = Math.max(0.6, height / 900);
  const count = options.particleCount ?? 220;
  const duration = options.durationMs ?? 3400;
  const random = (min: number, max: number): number => min + Math.random() * (max - min);
  const pick = <T>(list: readonly T[]): T => list[Math.floor(Math.random() * list.length)];

  const particle = (x: number, y: number, angleDeg: number, speed: number): Particle => {
    const angle = (angleDeg * Math.PI) / 180;
    return {
      x,
      y,
      vx: Math.cos(angle) * speed * scale,
      vy: Math.sin(angle) * speed * scale,
      rotation: random(0, Math.PI * 2),
      spin: random(-0.25, 0.25),
      wobble: random(0, Math.PI * 2),
      wobbleSpeed: random(0.05, 0.12),
      width: random(6, 11),
      height: random(8, 16),
      color: pick(colors),
      round: Math.random() < 0.25,
    };
  };

  // ★ SÓLO DESDE LOS LADOS: dos cañones en las esquinas de abajo, disparando hacia el centro. Nada cae
  // desde arriba.
  const particles: Particle[] = [];
  const cannons = (amount: number): void => {
    for (let i = 0; i < amount; i++) {
      particles.push(particle(-10, height * 0.92, random(-80, -45), random(14, 24)));
      particles.push(particle(width + 10, height * 0.92, random(-135, -100), random(14, 24)));
    }
  };
  const perCannon = Math.round(count * 0.4);
  cannons(perCannon);

  // ★ UN CONFETI LARGO NECESITA MÁS DE UN DISPARO. Lo lanzado cae fuera de la pantalla en unos tres
  // segundos: alargar sólo la duración dejaría la pantalla vacía. Si dura más, hay una segunda salva y,
  // hasta poco antes del final, los mismos cañones siguen soltando ráfagas cortas; después se desvanece.
  const secondVolleyAt = duration > 4000 ? 1400 : Infinity;
  const streamUntil = duration - 2200;
  const streamPerSide = Math.max(1, Math.round(count / 220));
  let secondVolleyDone = false;
  let frameCount = 0;

  const start = performance.now();
  let frame = 0;
  let stopped = false;

  const stop = (): void => {
    if (stopped) return;
    stopped = true;
    cancelAnimationFrame(frame);
    window.removeEventListener('resize', resize);
    canvas.remove();
  };

  const tick = (now: number): void => {
    const elapsed = now - start;
    const progress = elapsed / duration;
    if (progress >= 1) {
      stop();
      return;
    }

    if (!secondVolleyDone && elapsed >= secondVolleyAt) {
      secondVolleyDone = true;
      cannons(Math.round(perCannon * 0.7));
    }
    // Una ráfaga corta cada tres cuadros desde cada lado: un chorro continuo, no una nube.
    if (elapsed < streamUntil && frameCount++ % 3 === 0) cannons(streamPerSide);
    // Lo que ya salió por abajo no se sigue calculando: una lluvia larga no puede crecer sin fin.
    for (let i = particles.length - 1; i >= 0; i--) {
      if (particles[i].y > height + 40) particles.splice(i, 1);
    }

    // El último tramo se desvanece: termina en calma, no de golpe.
    const fadeFrom = Math.max(0.7, 1 - 1500 / duration);
    const alpha = progress > fadeFrom ? 1 - (progress - fadeFrom) / (1 - fadeFrom) : 1;

    ctx.clearRect(0, 0, width, height);
    ctx.globalAlpha = alpha;
    for (const p of particles) {
      p.vy += 0.32 * scale; // gravedad
      p.vx *= 0.985; // resistencia del aire
      p.vy *= 0.985;
      p.wobble += p.wobbleSpeed;
      p.x += p.vx + Math.sin(p.wobble) * 0.8;
      p.y += p.vy;
      p.rotation += p.spin;

      ctx.save();
      ctx.translate(p.x, p.y);
      ctx.rotate(p.rotation);
      // El papel gira sobre sí mismo: se aplana y vuelve, como un confeti real.
      ctx.scale(1, Math.cos(p.wobble));
      ctx.fillStyle = p.color;
      if (p.round) {
        ctx.beginPath();
        ctx.arc(0, 0, p.width / 2, 0, Math.PI * 2);
        ctx.fill();
      } else {
        ctx.fillRect(-p.width / 2, -p.height / 2, p.width, p.height);
      }
      ctx.restore();
    }
    frame = requestAnimationFrame(tick);
  };

  frame = requestAnimationFrame(tick);
  return stop;
}
