import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WsModalComponent } from './ws-modal.component';

/**
 * The zoom in / zoom out animation, and the three defects its first version shipped with.
 *
 * 1. ★★ The entrance kept `transform: scale(1)` on the dialog (fill-mode `both`). Any transform turns the
 *    dialog into the containing block of its `position: fixed` children, so the ws-select panel inside the
 *    Reassign modal landed off-place and clipped — no payee could be picked. Asserted with REAL geometry
 *    (Karma runs Chrome), because a CSS-declaration assertion would miss the next property that does it.
 * 2. Screens that remove the modal from OUTSIDE (`@if (target) { <ws-modal [isOpen]="true"> }`) closed it
 *    with no exit animation at all.
 * 3. On that same path the page stayed scroll-locked: isOpen never became false, so nothing released it.
 */
@Component({
  standalone: true,
  imports: [WsModalComponent],
  template: `
    @if (mounted()) {
      <ws-modal [isOpen]="open()" title="A modal">
        <div class="fixed-probe" style="position: fixed; top: 0; left: 0; width: 10px; height: 10px"></div>
        <textarea class="note"></textarea>
      </ws-modal>
    }
  `,
})
class HostComponent {
  readonly mounted = signal(true);
  readonly open = signal(true);
}

describe('WsModalComponent — zoom in / zoom out', () => {
  let fixture: ComponentFixture<HostComponent>;

  const ghosts = () => document.body.querySelectorAll<HTMLElement>(':scope > .ws-modal--leaving');
  const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    ghosts().forEach(g => g.remove());
    document.body.style.overflow = '';
  });

  it('★★ once the entrance finishes, a position:fixed child is placed against the VIEWPORT again', () => {
    const dialog = fixture.nativeElement.querySelector('.ws-modal__dialog') as HTMLElement;
    dialog.getAnimations().forEach(a => a.finish());

    expect(getComputedStyle(dialog).transform).toBe('none');
    const probe = (fixture.nativeElement.querySelector('.fixed-probe') as HTMLElement).getBoundingClientRect();
    expect(probe.top).toBe(0);
    expect(probe.left).toBe(0);
  });

  it('closing through isOpen leaves an inert copy that zooms out, then removes it', async () => {
    fixture.componentInstance.open.set(false);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ws-modal')).toBeNull();
    expect(ghosts().length).toBe(1);
    const ghost = ghosts()[0];
    expect(ghost.hasAttribute('inert')).toBeTrue();
    expect(ghost.getAttribute('aria-hidden')).toBe('true');
    expect(ghost.getAttribute('role')).toBeNull();
    expect(getComputedStyle(ghost.querySelector('.ws-modal__dialog')!).animationName).toContain('ws-modal-zoom-out');

    await wait(WsModalComponent.EXIT_MS + 50);
    expect(ghosts().length).toBe(0);
  });

  it('★ a modal destroyed from outside while open still zooms out, and releases the page scroll', async () => {
    expect(document.body.style.overflow).toBe('hidden');

    fixture.componentInstance.mounted.set(false);
    fixture.detectChanges();

    expect(ghosts().length).toBe(1);
    expect(document.body.style.overflow).toBe('');

    await wait(WsModalComponent.EXIT_MS + 50);
    expect(ghosts().length).toBe(0);
  });

  it('the copy keeps what was typed, so the fields do not flash empty while fading', () => {
    (fixture.nativeElement.querySelector('.note') as HTMLTextAreaElement).value = 'Reason written';

    fixture.componentInstance.open.set(false);
    fixture.detectChanges();

    expect((ghosts()[0].querySelector('.note') as HTMLTextAreaElement).value).toBe('Reason written');
  });

  it('a modal that was never open leaves no copy behind', () => {
    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    ghosts().forEach(g => g.remove());

    fixture.componentInstance.mounted.set(false);
    fixture.detectChanges();

    expect(ghosts().length).toBe(0);
  });
});
