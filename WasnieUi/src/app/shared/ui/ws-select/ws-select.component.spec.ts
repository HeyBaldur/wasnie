import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { NG_VALUE_ACCESSOR } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';
import { of, delay } from 'rxjs';
import { WsSelectComponent, SelectOption } from './ws-select.component';

const OPTIONS: SelectOption[] = [
  { value: 'a', label: 'Alpha' },
  { value: 'b', label: 'Beta' },
  { value: 'c', label: 'Gamma', disabled: true },
];

describe('WsSelectComponent', () => {
  let fixture: ComponentFixture<WsSelectComponent>;
  let comp: WsSelectComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WsSelectComponent, TranslateModule.forRoot()],
    }).compileComponents();

    fixture = TestBed.createComponent(WsSelectComponent);
    comp = fixture.componentInstance;
  });

  // --- Client-side mode ---

  it('renders placeholder when no value is selected', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.componentRef.setInput('placeholder', 'Pick one');
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.ws-select__value')?.textContent?.trim()).toBe('Pick one');
  });

  it('selectedOption returns the matching option', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();
    comp.writeValue('b');
    expect(comp.selectedOption()?.label).toBe('Beta');
  });

  it('filteredOptions filters by search query (client-side)', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();
    comp.searchQuery.set('al');
    expect(comp.filteredOptions().length).toBe(1);
    expect(comp.filteredOptions()[0].value).toBe('a');
  });

  it('select() updates value and calls onChange', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();
    const changes: (string | number)[] = [];
    comp.registerOnChange(v => changes.push(v));
    comp.select(OPTIONS[1]);
    expect(comp.value()).toBe('b');
    expect(changes).toEqual(['b']);
  });

  it('select() does nothing for a disabled option', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();
    comp.select(OPTIONS[2]);
    expect(comp.value()).toBe('');
  });

  it('setDisabledState() reflects on isDisabled signal', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();
    comp.setDisabledState(true);
    expect(comp.isDisabled()).toBeTrue();
  });

  it('writeValue() sets value; empty string on null', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();
    comp.writeValue('a');
    expect(comp.value()).toBe('a');
    comp.writeValue(null as unknown as string);
    expect(comp.value()).toBe('');
  });

  it('onKeydown ArrowDown / ArrowUp moves activeIndex', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();
    comp.openDropdown();
    comp.activeIndex.set(0);
    comp.onKeydown(new KeyboardEvent('keydown', { key: 'ArrowDown' }));
    expect(comp.activeIndex()).toBe(1);
    comp.onKeydown(new KeyboardEvent('keydown', { key: 'ArrowUp' }));
    expect(comp.activeIndex()).toBe(0);
  });

  it('Escape closes dropdown', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();
    comp.openDropdown();
    comp.onKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(comp.isOpen()).toBeFalse();
  });

  // --- Async mode ---

  it('in async mode filteredOptions returns asyncOptions directly', () => {
    const asyncOpts: SelectOption[] = [{ value: 'x', label: 'Xavier' }];
    fixture.componentRef.setInput('searchFn', () => of(asyncOpts));
    fixture.detectChanges();
    comp.asyncOptions.set(asyncOpts);
    expect(comp.filteredOptions()).toBe(asyncOpts);
  });

  it('selectedOption falls back to initialOption in async mode when value not in asyncOptions', () => {
    const initial: SelectOption = { value: 'pre', label: 'Preloaded' };
    fixture.componentRef.setInput('searchFn', () => of([]));
    fixture.componentRef.setInput('initialOption', initial);
    fixture.detectChanges();
    comp.writeValue('pre');
    expect(comp.selectedOption()).toEqual(initial);
  });

  it('selectedOption prefers asyncOptions over initialOption when the value is present', () => {
    const asyncOpt: SelectOption = { value: 'pre', label: 'From Server' };
    const initial: SelectOption = { value: 'pre', label: 'Preloaded' };
    fixture.componentRef.setInput('searchFn', () => of([asyncOpt]));
    fixture.componentRef.setInput('initialOption', initial);
    fixture.detectChanges();
    comp.asyncOptions.set([asyncOpt]);
    comp.writeValue('pre');
    expect(comp.selectedOption()?.label).toBe('From Server');
  });

  it('opening dropdown in async mode triggers an initial search', fakeAsync(() => {
    const queries: string[] = [];
    fixture.componentRef.setInput('searchFn', (q: string) => {
      queries.push(q);
      return of([]);
    });
    fixture.detectChanges();
    comp.openDropdown();
    tick(300); // debounce
    expect(queries).toContain('');
  }));

  it('onSearch() in async mode pushes query through debounce and updates asyncOptions', fakeAsync(() => {
    const serverOpts: SelectOption[] = [{ value: 'y', label: 'Yellow' }];
    fixture.componentRef.setInput('searchFn', (_q: string) => of(serverOpts));
    fixture.detectChanges();
    comp.isOpen.set(true);

    const event = { target: { value: 'yel' } } as unknown as Event;
    comp.onSearch(event);
    // asyncLoading is set inside the pipe after debounce — still false before tick
    expect(comp.asyncLoading()).toBeFalse();
    tick(300);
    // debounce fired, of() emitted synchronously, subscribe ran → loading cleared
    expect(comp.asyncOptions()).toEqual(serverOpts);
    expect(comp.asyncLoading()).toBeFalse();
  }));

  it('asyncLoading is set to true immediately and cleared after response', fakeAsync(() => {
    fixture.componentRef.setInput('searchFn', () => of([]).pipe(delay(100)));
    fixture.detectChanges();
    comp.openDropdown();
    tick(300); // debounce fires → loading = true, request in flight
    expect(comp.asyncLoading()).toBeTrue();
    tick(100); // response arrives
    expect(comp.asyncLoading()).toBeFalse();
  }));

  it('async mode shows empty state only after response with no results (not while loading)', fakeAsync(() => {
    fixture.componentRef.setInput('searchFn', () => of([]).pipe(delay(100)));
    fixture.detectChanges();
    comp.openDropdown();
    fixture.detectChanges();
    tick(300);
    fixture.detectChanges();
    // While loading: asyncLoading=true, asyncOptions still empty
    expect(fixture.nativeElement.querySelector('.ws-select__loading')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.ws-select__empty')).toBeNull();
    tick(100);
    fixture.detectChanges();
    // After response: asyncLoading=false, asyncOptions=[]
    expect(fixture.nativeElement.querySelector('.ws-select__loading')).toBeNull();
    expect(fixture.nativeElement.querySelector('.ws-select__empty')).toBeTruthy();
  }));

  // --- Multi-select mode (value stays a comma-separated string) ---

  it('multiple: select() toggles membership in the CSV value and keeps the dropdown open', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.componentRef.setInput('multiple', true);
    fixture.detectChanges();
    const changes: (string | number)[] = [];
    comp.registerOnChange(v => changes.push(v));
    comp.openDropdown();

    comp.select(OPTIONS[0]); // a
    comp.select(OPTIONS[1]); // b
    expect(comp.value()).toBe('a, b');
    expect(comp.selectedValues()).toEqual(['a', 'b']);
    expect(comp.isOpen()).toBeTrue();          // stays open for further picks

    comp.select(OPTIONS[0]); // toggle a off
    expect(comp.value()).toBe('b');
    expect(changes.at(-1)).toBe('b');
  });

  it('multiple: isOptionSelected reflects CSV membership (case-insensitive)', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.componentRef.setInput('multiple', true);
    fixture.detectChanges();
    comp.writeValue('A, b');
    expect(comp.isOptionSelected(OPTIONS[0])).toBeTrue();  // 'a' matches 'A'
    expect(comp.isOptionSelected(OPTIONS[1])).toBeTrue();
    expect(comp.isOptionSelected(OPTIONS[2])).toBeFalse();
  });

  it('multiple: multiLabel joins the selected option labels', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.componentRef.setInput('multiple', true);
    fixture.detectChanges();
    comp.writeValue('a, b');
    expect(comp.multiLabel()).toBe('Alpha, Beta');
  });

  it('single mode is unaffected: selectedValues is empty and select still closes', () => {
    fixture.componentRef.setInput('options', OPTIONS);
    fixture.detectChanges();
    comp.openDropdown();
    expect(comp.selectedValues()).toEqual([]);
    comp.select(OPTIONS[1]);
    expect(comp.value()).toBe('b');
    expect(comp.isOpen()).toBeFalse();
  });
});
