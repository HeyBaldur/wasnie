import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { REMOVE_STYLES_ON_COMPONENT_DESTROY } from '@angular/platform-browser';
import { TranslateModule } from '@ngx-translate/core';
import { WsModalComponent } from './ws-modal.component';
import { WsSelectComponent } from '../ws-select/ws-select.component';

/**
 * The exit animation must not outlive the styles it needs.
 *
 * ★★ THE DEFECT, MEASURED RATHER THAN REASONED ABOUT. Closing the "link to a payee" dialog flashed its
 * dropdown unstyled — "for a millisecond it loses everything and you see only letters". `ws-modal`
 * animates an inert CLONE of the closed dialog out over 220ms, and Angular 17+ removes a component's
 * `<style>` element when its LAST instance is destroyed. The modal's only `ws-select` died with the
 * dialog, its stylesheet left the document, and the clone was left carrying the right `_ngcontent`
 * attribute with nothing in the document to match it.
 *
 * ★★ THE FIRST TWO THEORIES WERE BOTH WRONG, which is why this file measures. It was not the clone
 * dropping bound classes (they survive `cloneNode`) and not the encapsulation attribute going missing
 * (it is identical on both nodes). Only `getComputedStyle` showed what was actually happening:
 * display flex → block, height 32px → 35px, border 1px → 0, background transparent.
 *
 * ★ ASSERTED ON COMPUTED STYLE, NOT ON CLASS NAMES. A test on `class` would have passed throughout the
 * entire bug — the classes were never the thing that was lost.
 */
@Component({
  standalone: true,
  imports: [WsModalComponent, WsSelectComponent, TranslateModule],
  template: `
    @if (open()) {
      <ws-modal [isOpen]="true" title="T">
        <ws-select [options]="options" placeholder="Pick" />
      </ws-modal>
    }
  `,
})
class HostComponent {
  readonly open = signal(true);
  options = [{ value: 'a', label: 'Ana' }];
}

describe('WsModal — the exit ghost keeps its styling', () => {
  /** The properties the bug visibly destroyed. */
  function snapshot(el: HTMLElement): Record<string, string> {
    const s = getComputedStyle(el);
    return {
      display: s.display,
      height: s.height,
      borderTopWidth: s.borderTopWidth,
      backgroundColor: s.backgroundColor,
    };
  }

  afterEach(() => {
    document.body.querySelectorAll('.ws-modal--leaving').forEach((n) => n.remove());
  });

  it('renders the closing copy exactly as the live dialog looked', () => {
    TestBed.configureTestingModule({
      imports: [HostComponent, TranslateModule.forRoot()],
      // ★★ THE FIX ITSELF, and the reason this spec needs it explicitly: TestBed does not read
      // app.config.ts, so without this line the suite would test the broken configuration and the
      // green would mean nothing about the product.
      providers: [{ provide: REMOVE_STYLES_ON_COMPONENT_DESTROY, useValue: false }],
    });

    const fixture = TestBed.createComponent(HostComponent);
    // Attached to the document on purpose: getComputedStyle on a detached tree reports defaults, and
    // the whole assertion below would compare two sets of nothing.
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();

    const live = fixture.nativeElement.querySelector('.ws-select__trigger') as HTMLElement;
    expect(live).withContext('no ws-select rendered inside the modal').toBeTruthy();
    const before = snapshot(live);

    // Sanity: the live trigger really is styled, or the comparison proves nothing.
    expect(before['display']).toBe('flex');

    fixture.componentInstance.open.set(false);
    fixture.detectChanges();

    const ghost = document.body.querySelector('.ws-modal--leaving');
    expect(ghost).withContext('no exit copy was appended').toBeTruthy();

    const ghostTrigger = ghost!.querySelector('.ws-select__trigger') as HTMLElement;
    expect(ghostTrigger).withContext('the copy lost the dropdown entirely').toBeTruthy();

    expect(snapshot(ghostTrigger)).toEqual(before);
  });
});
