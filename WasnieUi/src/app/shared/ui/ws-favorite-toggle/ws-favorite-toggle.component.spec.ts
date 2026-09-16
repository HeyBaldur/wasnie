import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslateModule } from '@ngx-translate/core';

import { WsFavoriteToggleComponent } from './ws-favorite-toggle.component';

/**
 * The star primitive. Its home is a `routerLink` row, so the host wraps it in a clickable element to prove the press
 * never reaches the row — the same proof ws-copy-button carries.
 */
@Component({
  standalone: true,
  imports: [WsFavoriteToggleComponent],
  template: `
    <div (click)="rowClicks.set(rowClicks() + 1)">
      <ws-favorite-toggle [active]="active()" [busy]="busy()" (toggled)="presses.set(presses() + 1)" />
    </div>
  `,
})
class HostComponent {
  readonly active = signal(false);
  readonly busy = signal(false);
  readonly presses = signal(0);
  readonly rowClicks = signal(0);
}

describe('WsFavoriteToggleComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  const button = () => fixture.nativeElement.querySelector('button') as HTMLButtonElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent, TranslateModule.forRoot()] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('emits on press and never lets the click reach the row underneath', () => {
    button().click();

    expect(host.presses()).toBe(1);
    expect(host.rowClicks()).toBe(0);
  });

  it('announces its state with aria-pressed and says what pressing will do', () => {
    expect(button().getAttribute('aria-pressed')).toBe('false');
    expect(button().getAttribute('aria-label')).toBe('FAVORITES.ADD');

    host.active.set(true);
    fixture.detectChanges();

    expect(button().getAttribute('aria-pressed')).toBe('true');
    expect(button().getAttribute('aria-label')).toBe('FAVORITES.REMOVE');
    expect(button().classList).toContain('ws-favorite-toggle--active');
  });

  it('draws the filled star when active and the outline when not', () => {
    const svgPath = () => fixture.nativeElement.querySelector('svg path') as SVGPathElement | null;

    expect(svgPath()?.getAttribute('fill')).toBeNull();

    host.active.set(true);
    fixture.detectChanges();

    expect(svgPath()?.getAttribute('fill')).toBe('currentColor');
  });

  it('ignores presses while busy', () => {
    host.busy.set(true);
    fixture.detectChanges();

    button().click();

    expect(host.presses()).toBe(0);
    expect(host.rowClicks()).toBe(0);
  });
});
