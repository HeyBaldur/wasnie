import { signal, WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { SandboxIntroComponent } from './sandbox-intro.component';
import { AssistantStore } from '../../assistant/state/assistant.store';
import { UI_PREFERENCE_KEYS, UiPreferencesService } from '../../../core/services/ui-preferences.service';

describe('SandboxIntroComponent', () => {
  let entitled: WritableSignal<boolean | null>;
  let open: jasmine.Spy;
  let values: Record<string, string>;
  let loads: boolean;

  const render = async () => {
    const fixture = TestBed.createComponent(SandboxIntroComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(() => {
    values = {};
    loads = true;
    entitled = signal<boolean | null>(true);
    open = jasmine.createSpy('open').and.resolveTo();
    TestBed.configureTestingModule({
      imports: [SandboxIntroComponent, TranslateModule.forRoot()],
      providers: [
        provideRouter([]),
        { provide: AssistantStore, useValue: { entitled, open, loadEntitlement: jasmine.createSpy().and.resolveTo() } },
        {
          provide: UiPreferencesService,
          useValue: {
            ensureLoaded: () => Promise.resolve(loads),
            isFlagSet: (key: string) => values[key] === 'true',
            setFlag: (key: string) => {
              values[key] = 'true';
              return Promise.resolve();
            },
          },
        },
      ],
    });
  });

  it('★ aparece la primera vez; cerrada, no vuelve nunca — para este usuario, en cualquier equipo', async () => {
    const first = await render();
    expect(first.nativeElement.querySelector('.intro')).not.toBeNull();

    (first.nativeElement.querySelector('.intro__close') as HTMLButtonElement).click();
    first.detectChanges();

    expect(first.nativeElement.querySelector('.intro')).toBeNull();
    expect(values[UI_PREFERENCE_KEYS.sandboxIntroDismissed]).toBe('true');
    expect((await render()).nativeElement.querySelector('.intro')).toBeNull();
  });

  it('★ sin poder leer las preferencias no se muestra (nunca por adivinar)', async () => {
    loads = false;
    expect((await render()).nativeElement.querySelector('.intro')).toBeNull();
  });

  it('★ Zeke sólo aparece para quien tiene el asistente — escondido, no deshabilitado', async () => {
    const withZeke = await render();
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
