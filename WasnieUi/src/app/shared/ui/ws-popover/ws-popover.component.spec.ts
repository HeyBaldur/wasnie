import { Component, viewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WsPopoverComponent } from './ws-popover.component';

/**
 * The rail is the shape that produced the bug: a short scroll container with rows inside it, where a
 * menu opened from the last row runs straight past the bottom edge and becomes unreachable without
 * scrolling first.
 */
@Component({
  standalone: true,
  imports: [WsPopoverComponent],
  template: `
    <div class="rail" style="height: 200px; overflow-y: auto;">
      <div [style.height.px]="spacerHeight">spacer</div>
      <ws-popover placement="bottom-end">
        <button slot="trigger" type="button">menu</button>
        <div style="height: 120px;">panel</div>
      </ws-popover>
      <div style="height: 40px;">tail</div>
    </div>
  `,
})
class RailHostComponent {
  spacerHeight = 0;
  readonly popover = viewChild.required(WsPopoverComponent);
}

describe('WsPopoverComponent — flipping', () => {
  let fixture: ComponentFixture<RailHostComponent>;
  let host: RailHostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [RailHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(RailHostComponent);
    host = fixture.componentInstance;
    // The element must be in the live document for getBoundingClientRect to return real geometry.
    document.body.appendChild(fixture.nativeElement);
  });

  afterEach(() => fixture.nativeElement.remove());

  function panelClass(): string {
    return (fixture.nativeElement.querySelector('.ws-pop__panel') as HTMLElement).className;
  }

  it('keeps the requested side when the panel fits below the trigger', () => {
    host.spacerHeight = 0; // trigger at the top of the rail: plenty of room underneath
    fixture.detectChanges();

    host.popover().open();
    fixture.detectChanges();

    expect(panelClass()).toContain('ws-pop__panel--bottom-end');
    expect(panelClass()).not.toContain('ws-pop__panel--top-end');
  });

  it('flips above the trigger when the panel would run past the bottom of the rail', () => {
    host.spacerHeight = 170; // trigger pushed to the bottom edge of the 200px rail
    fixture.detectChanges();

    host.popover().open();
    fixture.detectChanges();

    expect(panelClass())
      .withContext('a menu on the last row must open upward instead of off the bottom edge')
      .toContain('ws-pop__panel--top-end');
  });

  it('reports the flip through resolvedPlacement while leaving placement untouched', () => {
    host.spacerHeight = 170;
    fixture.detectChanges();

    const popover = host.popover();
    popover.open();
    fixture.detectChanges();

    expect(popover.placement()).toBe('bottom-end');
    expect(popover.resolvedPlacement()).toBe('top-end');
  });

  it('goes back to the requested side once the panel is closed and reopened with room', () => {
    host.spacerHeight = 170;
    fixture.detectChanges();
    host.popover().open();
    fixture.detectChanges();
    expect(host.popover().resolvedPlacement()).toBe('top-end');

    host.popover().close();
    fixture.detectChanges();

    host.spacerHeight = 0;
    fixture.detectChanges();
    host.popover().open();
    fixture.detectChanges();

    expect(host.popover().resolvedPlacement())
      .withContext('the flip is a per-open measurement, not a sticky state')
      .toBe('bottom-end');
  });

  it('stays on the requested side when neither side has room', () => {
    // A rail shorter than the panel: flipping would only trade one clipped edge for the other, and
    // the menu should at least appear where the caller said it would.
    const rail = fixture.nativeElement.querySelector('.rail') as HTMLElement;
    rail.style.height = '60px';
    host.spacerHeight = 20;
    fixture.detectChanges();

    host.popover().open();
    fixture.detectChanges();

    expect(host.popover().resolvedPlacement()).toBe('bottom-end');
  });
});
