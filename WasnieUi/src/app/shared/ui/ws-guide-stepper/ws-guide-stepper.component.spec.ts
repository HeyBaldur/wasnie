import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WsGuideStep, WsGuideStepperComponent } from './ws-guide-stepper.component';

/**
 * El panel de guía del onboarding (KAN-68).
 *
 * ★ SE MIRA EL DOM, NO LAS SEÑALES (§A3). Lo que este componente promete es que el usuario VE en qué
 * paso está, cuál ya hizo y por qué uno está bloqueado. Una prueba sobre el estado interno pasaría
 * igual de contenta con una plantilla que no pintara nada de eso.
 */
describe('WsGuideStepperComponent', () => {
  let fixture: ComponentFixture<WsGuideStepperComponent>;

  const steps: WsGuideStep[] = [
    { id: 'plan', title: 'Create a plan', description: 'The container for the rules.', state: 'done' },
    { id: 'rule', title: 'Add a rule', description: 'The rate table.', state: 'current' },
    { id: 'payrun', title: 'Run a pay run', state: 'blocked', blockedReason: 'You need a calculated transaction first.' },
  ];

  function render(input: Partial<{ steps: WsGuideStep[]; navigable: boolean }> = {}): void {
    fixture = TestBed.createComponent(WsGuideStepperComponent);
    fixture.componentRef.setInput('steps', input.steps ?? steps);
    if (input.navigable !== undefined) fixture.componentRef.setInput('navigable', input.navigable);
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [WsGuideStepperComponent] }).compileComponents();
  });

  it('numbers the steps by position and marks the current one', () => {
    render();

    const current = el().querySelector('.ws-gs__step--current');
    expect(current?.querySelector('.ws-gs__title')?.textContent?.trim()).toBe('Add a rule');
    expect(current?.querySelector('.ws-gs__circle')?.textContent?.trim()).toBe('2');
    expect(current?.querySelector('.ws-gs__body')?.getAttribute('aria-current')).toBe('step');
  });

  /** ★ Un paso hecho no muestra su número: muestra que está hecho. */
  it('replaces the number with a mark on a completed step', () => {
    render();

    const done = el().querySelector('.ws-gs__step--done');
    expect(done?.querySelector('.ws-gs__circle')?.textContent?.trim()).toBe('');
    expect(done?.querySelector('app-icon')).not.toBeNull();
  });

  /**
   * ★★ EL MOTIVO ES LA MITAD ÚTIL DEL BLOQUEO. Sin él, el paso deshabilitado deja al usuario buscando
   * qué hizo mal en vez de decirle adónde ir.
   */
  it('states why a blocked step is blocked, and only on the blocked one', () => {
    render();

    const reasons = el().querySelectorAll('.ws-gs__blocked');
    expect(reasons.length).toBe(1);
    expect(reasons[0].textContent).toContain('You need a calculated transaction first.');
    expect(el().querySelector('.ws-gs__step--blocked .ws-gs__title')?.textContent?.trim())
      .toBe('Run a pay run');
  });

  /** El último paso no conecta con nada: su línea se marca para que el CSS la esconda. */
  it('marks the last step so its connector can be hidden', () => {
    render();

    const all = el().querySelectorAll('.ws-gs__step');
    expect(all.length).toBe(3);
    expect(all[all.length - 1].classList).toContain('ws-gs__step--last');
    expect(all[0].classList).not.toContain('ws-gs__step--last');
  });

  /** Leer adelante es la forma de entender qué falta: hasta un paso bloqueado se puede abrir. */
  it('emits the step that was opened, blocked ones included', () => {
    render();
    const opened: string[] = [];
    fixture.componentInstance.stepSelect.subscribe(id => opened.push(id));

    el().querySelectorAll<HTMLButtonElement>('.ws-gs__body').forEach(b => b.click());

    expect(opened).toEqual(['plan', 'rule', 'payrun']);
  });

  it('is inert when it is not navigable', () => {
    render({ navigable: false });
    const opened: string[] = [];
    fixture.componentInstance.stepSelect.subscribe(id => opened.push(id));

    const buttons = el().querySelectorAll<HTMLButtonElement>('.ws-gs__body');
    buttons.forEach(b => b.click());

    expect(buttons[0].disabled).toBeTrue();
    expect(opened).toEqual([]);
  });
});
