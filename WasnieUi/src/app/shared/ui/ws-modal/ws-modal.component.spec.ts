import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { WsModalComponent } from './ws-modal.component';

/**
 * The bug these tests hold shut: a modal whose content was taller than the screen pushed its footer
 * past the dialog's `overflow: hidden` edge, so **Save and Cancel were clipped away entirely** and
 * nothing scrolled to reach them. The user could neither confirm nor cancel — a dead end.
 *
 * These are geometry assertions on purpose. Asserting the CSS declarations would keep passing the
 * day someone removes `min-height: 0` from the body, because the declaration that breaks it is the
 * absence of one. What matters is: is the footer on screen, and does the body scroll.
 */
@Component({
  standalone: true,
  imports: [WsModalComponent],
  template: `
    <ws-modal [isOpen]="open()" title="A modal">
      <div class="test-content" [style.height.px]="contentHeight()">content</div>
      <div slot="footer">
        <button class="test-cancel" type="button">Cancel</button>
        <button class="test-save" type="button">Save</button>
      </div>
    </ws-modal>
  `,
})
class HostComponent {
  readonly open = signal(true);
  /** 5000px is taller than any real viewport, so the overflow case is forced, not hoped for. */
  readonly contentHeight = signal(5000);
}

describe('WsModalComponent — the three zones', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
  });

  afterEach(() => {
    // The component locks page scroll while open; leaving it locked would leak into other specs.
    document.body.style.overflow = '';
  });

  function render(contentHeight: number) {
    fixture.componentInstance.contentHeight.set(contentHeight);
    fixture.detectChanges();

    const el = (selector: string) =>
      fixture.debugElement.query(By.css(selector)).nativeElement as HTMLElement;

    return {
      dialog: el('.ws-modal__dialog'),
      body: el('.ws-modal__body'),
      footer: el('.ws-modal__footer'),
      save: el('.test-save'),
    };
  }

  it('keeps the dialog within the viewport when the content is enormous', () => {
    const { dialog } = render(5000);

    expect(dialog.getBoundingClientRect().height)
      .withContext('the dialog must never grow past the screen')
      .toBeLessThanOrEqual(window.innerHeight);
  });

  it('keeps Save and Cancel on screen when the content overflows', () => {
    const { dialog, footer, save } = render(5000);

    const dialogRect = dialog.getBoundingClientRect();
    const footerRect = footer.getBoundingClientRect();

    // The exact failure that trapped the user: the footer sat below the dialog's clipped edge.
    expect(footerRect.bottom)
      .withContext('the footer must sit inside the dialog, not past its clipped edge')
      .toBeLessThanOrEqual(dialogRect.bottom + 1);
    expect(footerRect.top).toBeGreaterThanOrEqual(dialogRect.top);
    expect(footerRect.height).withContext('the footer must not be collapsed to nothing').toBeGreaterThan(0);

    const saveRect = save.getBoundingClientRect();
    expect(saveRect.height).toBeGreaterThan(0);
    expect(saveRect.bottom).toBeLessThanOrEqual(window.innerHeight + 1);
  });

  it('scrolls the BODY, not the whole dialog', () => {
    const { body } = render(5000);

    expect(body.scrollHeight)
      .withContext('the overflow has to live in the body')
      .toBeGreaterThan(body.clientHeight);
    expect(getComputedStyle(body).overflowY).toBe('auto');

    // And it must actually be reachable, not just declared scrollable.
    body.scrollTop = body.scrollHeight;
    expect(body.scrollTop).toBeGreaterThan(0);
  });

  it('leaves the header pinned above the scrolling body', () => {
    const { dialog, body } = render(5000);
    const header = dialog.querySelector('.ws-modal__header') as HTMLElement;

    const headerRect = header.getBoundingClientRect();
    expect(headerRect.top).toBeGreaterThanOrEqual(dialog.getBoundingClientRect().top - 1);
    expect(headerRect.bottom).toBeLessThanOrEqual(body.getBoundingClientRect().top + 1);
  });

  it('does not add a scrollbar to a short modal', () => {
    const { dialog, body, footer } = render(40);

    expect(body.scrollHeight)
      .withContext('a short modal must look exactly as it did before')
      .toBeLessThanOrEqual(body.clientHeight);
    // The dialog shrink-wraps its content instead of stretching to the max height.
    expect(dialog.getBoundingClientRect().height).toBeLessThan(window.innerHeight);
    // The footer stays attached to the content, not pushed to the bottom of an oversized panel.
    expect(footer.getBoundingClientRect().top - body.getBoundingClientRect().bottom)
      .toBeLessThanOrEqual(1);
  });

  it('survives a short viewport — the case that trapped the user', () => {
    // Simulates a laptop with the browser chrome eating the screen by shrinking the modal's own
    // ceiling: whatever the available height, the footer has to remain inside it.
    const { dialog, body, footer } = render(5000);
    dialog.style.maxHeight = '320px';
    fixture.detectChanges();

    const dialogRect = dialog.getBoundingClientRect();
    expect(dialogRect.height).toBeLessThanOrEqual(321);
    expect(footer.getBoundingClientRect().bottom).toBeLessThanOrEqual(dialogRect.bottom + 1);
    expect(body.scrollHeight).toBeGreaterThan(body.clientHeight);
    expect(body.clientHeight).withContext('the body must not be squeezed to zero').toBeGreaterThan(0);
  });
});
