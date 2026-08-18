import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Component, signal } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { WsTooltipDirective } from '../ws-tooltip/ws-tooltip.directive';
import { CopiedFeedbackMs, WsCopyButtonComponent } from './ws-copy-button.component';

// Host wraps the button in a click handler, exactly as a clickable table row does. `rowClicks`
// is what proves the row was NOT navigated when the user copied.
@Component({
  standalone: true,
  imports: [TranslateModule, WsCopyButtonComponent],
  template: `
    <div (click)="rowClicks.set(rowClicks() + 1)">
      <ws-copy-button [value]="value()" [label]="label()" [tooltip]="tooltip()" />
    </div>
  `,
})
class HostComponent {
  readonly value = signal('PAYEE-42');
  readonly label = signal('COMMON.COPY');
  readonly tooltip = signal<string | null>(null);
  readonly rowClicks = signal(0);
}

describe('WsCopyButtonComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let written: string[];
  let writeText: jasmine.Spy;

  function button(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('.ws-copy-button');
  }
  // Read straight off the directive instance: the tooltip element itself only exists after a
  // 300ms hover, and what this asserts is the text the button OFFERS, not the popup's lifecycle.
  function tooltipText(): string {
    return fixture.debugElement
      .query(By.directive(WsTooltipDirective))
      .injector.get(WsTooltipDirective)
      .wsTooltip();
  }
  function iconPaths(): string {
    return fixture.nativeElement.querySelector('svg')?.innerHTML ?? '';
  }

  beforeEach(async () => {
    written = [];
    writeText = jasmine.createSpy('writeText').and.callFake((text: string) => {
      written.push(text);
      return Promise.resolve();
    });
    // navigator.clipboard is undefined in headless Chrome without a secure context.
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      configurable: true,
    });

    await TestBed.configureTestingModule({
      imports: [HostComponent, TranslateModule.forRoot()],
    }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('writes the value to the clipboard', fakeAsync(() => {
    button().click();
    tick();

    expect(written).toEqual(['PAYEE-42']);
  }));

  it('shows the tick for exactly the feedback window, then goes back to the copy icon', fakeAsync(() => {
    // The circle is the tick's; the 2.667 radius is the copy glyph's. Asserting on the rendered
    // path keeps this honest about what the user actually sees.
    expect(iconPaths()).toContain('2.667');

    button().click();
    tick();
    fixture.detectChanges();
    expect(iconPaths()).toContain('circle');

    tick(CopiedFeedbackMs);
    fixture.detectChanges();
    expect(iconPaths()).toContain('2.667');
  }));

  // ★ THE REGRESSION THIS PRIMITIVE EXISTS TO PREVENT: payee and plan rows are routerLinks, and a
  // copy that also navigates loses the list the user was copying from.
  it('does not let the click reach the row underneath', fakeAsync(() => {
    button().click();
    tick();

    expect(host.rowClicks()).toBe(0);
  }));

  it('stays un-ticked when the clipboard refuses, rather than claiming success', fakeAsync(() => {
    writeText.and.returnValue(Promise.reject(new Error('denied')));

    button().click();
    tick();
    fixture.detectChanges();

    expect(iconPaths()).toContain('2.667');
  }));

  it('is disabled and copies nothing when there is no value', fakeAsync(() => {
    host.value.set('   ');
    fixture.detectChanges();

    expect(button().disabled).toBeTrue();

    button().click();
    tick();
    expect(written).toEqual([]);
  }));

  it('names itself from the label key and prefers a raw tooltip when one is given', () => {
    host.tooltip.set('28df4900-8c39-4fc9-9e22-6b2bf318a9b1');
    host.label.set('COMMON.COPY_ID');
    fixture.detectChanges();

    expect(button().getAttribute('aria-label')).toBe('COMMON.COPY_ID');
    expect(tooltipText()).toBe('28df4900-8c39-4fc9-9e22-6b2bf318a9b1');
  });

  it('falls back to the translated label when no raw tooltip is given', () => {
    fixture.detectChanges();

    expect(tooltipText()).toBe('COMMON.COPY');
  });

  it('says it copied, in the tooltip too', fakeAsync(() => {
    button().click();
    tick();
    fixture.detectChanges();

    expect(tooltipText()).toBe('COMMON.COPIED');
    tick(CopiedFeedbackMs);
  }));
});
