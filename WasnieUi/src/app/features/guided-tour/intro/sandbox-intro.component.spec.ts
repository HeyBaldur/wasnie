import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { SandboxIntroComponent } from './sandbox-intro.component';
import { AssistantStore } from '../../assistant/state/assistant.store';

describe('SandboxIntroComponent', () => {
  const KEY = 'wasnie:sandbox-intro-dismissed';
  let entitled: WritableSignal<boolean | null>;
  let open: jasmine.Spy;

  const render = () => {
    const fixture = TestBed.createComponent(SandboxIntroComponent);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(() => {
    localStorage.removeItem(KEY);
    entitled = signal<boolean | null>(true);
    open = jasmine.createSpy('open').and.resolveTo();
    TestBed.configureTestingModule({
      imports: [SandboxIntroComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: AssistantStore, useValue: { entitled, open, loadEntitlement: jasmine.createSpy().and.resolveTo() } },
      ],
    });
  });

  afterEach(() => localStorage.removeItem(KEY));

  it('★ aparece la primera vez; cerrada, no vuelve nunca', () => {
    const first = render();
    expect(first.nativeElement.querySelector('.intro')).not.toBeNull();

    (first.nativeElement.querySelector('.intro__close') as HTMLButtonElement).click();
    first.detectChanges();

    expect(first.nativeElement.querySelector('.intro')).toBeNull();
    expect(localStorage.getItem(KEY)).toBe('1');
    expect(render().nativeElement.querySelector('.intro')).toBeNull();
  });

  it('★ Zeke sólo aparece para quien tiene el asistente — escondido, no deshabilitado', () => {
    const withZeke = render();
    expect(withZeke.nativeElement.querySelector('.intro__zeke')).not.toBeNull();
    (withZeke.nativeElement.querySelector('.intro__zeke button') as HTMLButtonElement).click();
    expect(open).toHaveBeenCalled();

    entitled.set(false);
    withZeke.detectChanges();
    expect(withZeke.nativeElement.querySelector('.intro__zeke')).toBeNull();

    entitled.set(null); // todavía no se sabe: tampoco se ofrece
    withZeke.detectChanges();
    expect(withZeke.nativeElement.querySelector('.intro__zeke')).toBeNull();
  });
});
