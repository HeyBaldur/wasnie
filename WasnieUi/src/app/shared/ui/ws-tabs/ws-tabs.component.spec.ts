import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';
import { WsTabsComponent } from './ws-tabs.component';

describe('WsTabsComponent', () => {
  let fixture: ComponentFixture<WsTabsComponent>;

  const buttons = (): HTMLButtonElement[] =>
    [...fixture.nativeElement.querySelectorAll('button[role="tab"]')];

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [WsTabsComponent, TranslateModule.forRoot()] });
    fixture = TestBed.createComponent(WsTabsComponent);
    fixture.componentRef.setInput('tabs', [
      { value: 'steps', label: 'Steps', icon: 'list' },
      { value: 'experiments', label: 'Experiments', icon: 'archive' },
    ]);
    fixture.componentRef.setInput('translateLabels', false);
    fixture.componentRef.setInput('value', 'steps');
    fixture.detectChanges();
  });

  it('renders one tab per option, marking only the open one as selected', () => {
    expect(buttons().map(b => b.textContent?.trim())).toEqual(['Steps', 'Experiments']);
    expect(buttons().map(b => b.getAttribute('aria-selected'))).toEqual(['true', 'false']);
    expect(buttons()[0].classList).toContain('ws-tabs__tab--active');
  });

  it('selects a tab on click', () => {
    buttons()[1].click();
    fixture.detectChanges();

    expect(fixture.componentInstance.value()).toBe('experiments');
    expect(buttons()[1].getAttribute('aria-selected')).toBe('true');
  });

  it('is a standalone group by default and a card header when asked', () => {
    const group = (): HTMLElement => fixture.nativeElement.querySelector('.ws-tabs');
    expect(group().classList).not.toContain('ws-tabs--header');

    fixture.componentRef.setInput('variant', 'header');
    fixture.detectChanges();

    expect(group().classList).toContain('ws-tabs--header');
  });

  it('moves with the arrow keys and wraps at the ends', () => {
    buttons()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(fixture.componentInstance.value()).toBe('experiments');

    buttons()[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(fixture.componentInstance.value()).toBe('steps');

    buttons()[0].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    expect(fixture.componentInstance.value()).toBe('experiments');
  });
});
