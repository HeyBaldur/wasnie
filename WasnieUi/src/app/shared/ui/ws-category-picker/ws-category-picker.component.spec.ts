import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Component } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import { WsCategoryPickerComponent } from './ws-category-picker.component';

// Host binds the picker through a real FormControl, exactly as the transaction/rule forms do.
@Component({
  standalone: true,
  imports: [ReactiveFormsModule, TranslateModule, WsCategoryPickerComponent],
  template: `<ws-category-picker [formControl]="ctrl" [options]="options" />`,
})
class HostComponent {
  ctrl = new FormControl<string | null>('');
  options: string[] = [];
}

describe('WsCategoryPickerComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  function picker(): WsCategoryPickerComponent {
    return fixture.debugElement.children[0].componentInstance;
  }
  function toggleText(): string | null {
    return fixture.nativeElement.querySelector('.ws-category-picker__toggle')?.textContent?.trim() ?? null;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HostComponent, TranslateModule.forRoot()],
    }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
  });

  it('starts in list mode when categories exist', () => {
    host.options = ['Laptops', 'Servers'];
    fixture.detectChanges();
    expect(picker().useCustom()).toBeFalse();
    expect(fixture.nativeElement.querySelector('ws-select')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('ws-input')).toBeFalsy();
  });

  it('toggles to custom (free text) and back', () => {
    host.options = ['Laptops'];
    fixture.detectChanges();

    picker().toggleCustom();
    fixture.detectChanges();
    expect(picker().useCustom()).toBeTrue();
    expect(fixture.nativeElement.querySelector('ws-input')).toBeTruthy();

    picker().toggleCustom();
    fixture.detectChanges();
    expect(picker().useCustom()).toBeFalse();
  });

  it('propagates the chosen value to the bound control', () => {
    host.options = ['Laptops', 'Servers'];
    fixture.detectChanges();
    picker().inner.setValue('Servers');
    expect(host.ctrl.value).toBe('Servers');
  });

  it('emits null when cleared to blank', () => {
    host.options = ['Laptops'];
    fixture.detectChanges();
    picker().inner.setValue('   ');
    expect(host.ctrl.value).toBeNull();
  });

  it('falls back to free text with a hint when there are no categories', () => {
    host.options = [];
    fixture.detectChanges();
    expect(picker().listEmpty()).toBeTrue();
    expect(picker().useCustom()).toBeTrue();
    expect(fixture.nativeElement.querySelector('ws-input')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.ws-category-picker__hint')).toBeTruthy();
    expect(toggleText()).toBeNull();
  });

  it('starts in custom mode for a stored value not in the list (stays visible)', () => {
    host.options = ['Laptops'];
    host.ctrl.setValue('Servers'); // an unknown value (e.g. from the CRM or an older rule)
    fixture.detectChanges();
    expect(picker().useCustom()).toBeTrue();
  });

  it('hides the toggle when disabled', () => {
    host.options = ['Laptops'];
    host.ctrl.disable();
    fixture.detectChanges();
    expect(picker().isDisabled()).toBeTrue();
    expect(toggleText()).toBeNull();
  });
});
