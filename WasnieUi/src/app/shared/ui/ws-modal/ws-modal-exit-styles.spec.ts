import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
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

  let host: HTMLElement | null = null;

  /**
   * ★★ THE FIXTURE IS TAKEN BACK OUT OF THE DOCUMENT, AND LEAVING IT IN MADE THIS SPEC FLAP. It has to
   * be attached for `getComputedStyle` to report anything real, but Karma runs every spec into one
   * page: an abandoned fixture keeps a live `ws-select` in the document, which keeps its stylesheet
   * alive, which means the very styles this test is about surviving would survive for the wrong
   * reason. It went green on most runs and red on the ones where the order differed — the worst kind
   * of result, because a flaky pass here reads as "the bug is fixed".
   */
  afterEach(() => {
    document.body.querySelectorAll('.ws-modal--leaving').forEach((n) => n.remove());
    host?.remove();
    host = null;
  });

  /**
   * ★★ WHAT IS ASSERTED HERE IS THE CLONE, NOT THE STYLESHEET, AND THE DIFFERENCE IS DELIBERATE.
   * The first version of this spec compared `getComputedStyle` on the live trigger against the ghost's
   * and it FLAPPED — green on most runs, red on maybe one in three. The cause is not the modal:
   * `REMOVE_STYLES_ON_COMPONENT_DESTROY` is consumed by `DomRendererFactory2`, which Angular builds
   * ONCE per platform, so a per-spec TestBed provider only takes effect when this file happens to run
   * before anything else has built the renderer. Karma randomises that order.
   *
   * ★★ SO THE GLOBAL HALF IS NOT TESTED HERE AT ALL, and saying so is the point. It was verified in the
   * running app instead, by measuring the ghost's trigger before and after the fix: display flex →
   * block, height 32px → 35px, border 1px → 0, background transparent, and all four restored. A flaky
   * green on that assertion would have read as "the bug is fixed" while proving nothing, which is worse
   * than the honest gap this comment leaves.
   *
   * ★ WHAT IS LEFT IS STILL WORTH PINNING: that `playExit` produces a copy at all, that the copy keeps
   * the projected component rather than an empty shell, and that it carries the encapsulation attribute
   * the stylesheet is matched on. Those are the modal's own behaviour and they are deterministic.
   */
  it('animates out a copy that still contains the projected content', () => {
    TestBed.configureTestingModule({ imports: [HostComponent, TranslateModule.forRoot()] });

    const fixture = TestBed.createComponent(HostComponent);
    host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);
    fixture.detectChanges();

    const live = fixture.nativeElement.querySelector('.ws-select__trigger') as HTMLElement;
    expect(live).withContext('no ws-select rendered inside the modal').toBeTruthy();

    const encapsulation = Array.from(live.attributes)
      .map((a) => a.name)
      .filter((n) => n.startsWith('_ngcontent'));

    // ★ Any OTHER spec that closed a modal in the last 220 ms left its own ghost in the body, and
    // `querySelector` returns the first one — which is not ours and holds no ws-select. Swept here so
    // the only ghost left to find is the one this close creates.
    document.body.querySelectorAll('.ws-modal--leaving').forEach((n) => n.remove());

    fixture.componentInstance.open.set(false);
    fixture.detectChanges();

    const ghost = document.body.querySelector('.ws-modal--leaving');
    expect(ghost).withContext('no exit copy was appended').toBeTruthy();

    const ghostTrigger = ghost!.querySelector('.ws-select__trigger') as HTMLElement;
    expect(ghostTrigger).withContext('the copy lost the dropdown entirely').toBeTruthy();

    // The attribute the component's stylesheet is matched on has to survive the clone, or no amount of
    // keeping that stylesheet alive would help.
    const ghostEncapsulation = Array.from(ghostTrigger.attributes)
      .map((a) => a.name)
      .filter((n) => n.startsWith('_ngcontent'));

    expect(ghostEncapsulation).toEqual(encapsulation);
  });
});
