import { TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { WsToastContainerComponent } from './ws-toast-container.component';
import { WsToastService, WsToastType } from './ws-toast.service';

/**
 * The toast primitive, which ~30 features render through.
 *
 * ★★ THE ICON IS WHAT IS PINNED, because it is the part that fails SILENTLY. `IconComponent` renders
 * nothing for a name it does not recognise — no error, no warning, just a gap where the meaning was.
 * A name built from the type (`type + '-circle'`) would look perfectly reasonable in review and would
 * leave the error toast blank for ever.
 *
 * ★★ AND THE ASSERTION IS ABOUT THE SHAPES INSIDE THE SVG, NOT ABOUT THE SVG. `IconComponent` always
 * renders the `<svg>` element and fills only its `innerHTML` from the dictionary, so `svg !== null`
 * passes for an icon name that does not exist — the first version of this test did exactly that and
 * stayed green when the map was deliberately broken. What proves an icon is really there is a path.
 */
describe('WsToastContainerComponent', () => {
  const ALL_TYPES: readonly WsToastType[] = ['success', 'error', 'warning', 'info'];

  function render() {
    TestBed.configureTestingModule({
      // ★ No animation provider: the entrance is a CSS keyframe on the element, not an Angular
      // animation, so this component needs nothing from @angular/animations (which this app does
      // not install).
      imports: [WsToastContainerComponent, TranslateModule.forRoot()],
    });
    const fixture = TestBed.createComponent(WsToastContainerComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('draws an icon for every toast type', () => {
    const fixture = render();
    const service = TestBed.inject(WsToastService);

    for (const type of ALL_TYPES) {
      service.show('COMMON.DISMISS', type);
    }
    fixture.detectChanges();

    const toasts = fixture.nativeElement.querySelectorAll('.ws-toast');
    expect(toasts.length).toBe(ALL_TYPES.length);

    ALL_TYPES.forEach((type, i) => {
      const toast = toasts[i] as HTMLElement;
      expect(toast.classList).toContain(`ws-toast--${type}`);
      const svg = toast.querySelector('.ws-toast__icon svg');
      expect(svg).not.toBeNull();
      expect(svg!.querySelector('path, circle, line, polyline, rect, polygon'))
        .withContext(`the ${type} toast renders an EMPTY svg — its icon name is not in IconComponent`)
        .not.toBeNull();
    });
  });

  /**
   * ★ The accent bar is gone on purpose: the colour lives in the icon now. Asserted so a future
   * revert of the stylesheet cannot quietly leave both.
   */
  it('carries no accent bar', () => {
    const fixture = render();
    TestBed.inject(WsToastService).show('COMMON.DISMISS', 'success');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ws-toast__accent')).toBeNull();
  });

  it('dismisses the toast the close button belongs to, and only that one', () => {
    const fixture = render();
    const service = TestBed.inject(WsToastService);

    service.show('COMMON.DISMISS', 'success');
    service.show('COMMON.DISMISS', 'error');
    fixture.detectChanges();

    const closeButtons = fixture.nativeElement.querySelectorAll('.ws-toast__close');
    expect(closeButtons.length).toBe(2);

    (closeButtons[0] as HTMLButtonElement).click();
    fixture.detectChanges();

    const remaining = service.toasts();
    expect(remaining.length).toBe(1);
    expect(remaining[0].type).toBe('error');
  });

  /** ★ The dismiss control is labelled for screen readers — it has no text of its own. */
  it('labels the close button', () => {
    const fixture = render();
    TestBed.inject(WsToastService).show('COMMON.DISMISS', 'info');
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('.ws-toast__close') as HTMLElement;
    expect(button.getAttribute('aria-label')).toBeTruthy();
  });

  /**
   * ★★ THE WIRING, NOT THE TIMER. `WsToastService` owns the countdown and its own spec proves the
   * arithmetic; what can break HERE is the template — a handler bound to `mouseover` instead of
   * `mouseenter`, or the pair missing on one side so the toast never resumes. So this asserts the
   * events actually reach the service.
   */
  describe('holding the countdown while it is being read', () => {
    it('pauses on hover and resumes when the pointer leaves', () => {
      const fixture = render();
      const service = TestBed.inject(WsToastService);
      spyOn(service, 'pause').and.callThrough();
      spyOn(service, 'resume').and.callThrough();

      service.show('COMMON.DISMISS', 'success');
      fixture.detectChanges();

      const toast = fixture.nativeElement.querySelector('.ws-toast') as HTMLElement;
      const id = service.toasts()[0].id;

      toast.dispatchEvent(new MouseEvent('mouseenter'));
      expect(service.pause).toHaveBeenCalledWith(id);

      toast.dispatchEvent(new MouseEvent('mouseleave'));
      expect(service.resume).toHaveBeenCalledWith(id);
    });

    /** ★ A reader who tabbed to the dismiss button is reading too. */
    it('pauses on keyboard focus and resumes when focus leaves', () => {
      const fixture = render();
      const service = TestBed.inject(WsToastService);
      spyOn(service, 'pause').and.callThrough();
      spyOn(service, 'resume').and.callThrough();

      service.show('COMMON.DISMISS', 'error');
      fixture.detectChanges();

      const toast = fixture.nativeElement.querySelector('.ws-toast') as HTMLElement;
      const id = service.toasts()[0].id;

      toast.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
      expect(service.pause).toHaveBeenCalledWith(id);

      toast.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
      expect(service.resume).toHaveBeenCalledWith(id);
    });
  });
});
