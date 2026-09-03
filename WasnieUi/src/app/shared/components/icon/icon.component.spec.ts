import { ComponentFixture, TestBed } from '@angular/core/testing';
import { IconComponent } from './icon.component';

/**
 * Most icons are line drawings stroked in currentColor. Excel and PDF are brand MARKS: their colour
 * carries their meaning, so they ship as artwork and render through an <img> instead.
 *
 * ★ THE TESTS ASSERT THE RENDERED ELEMENT, not the lookup map — a component that picked the right
 * URL and still drew an empty <svg> would pass the second and fail the user.
 */
describe('IconComponent', () => {
  let fixture: ComponentFixture<IconComponent>;

  async function render(name: string, size = 14): Promise<HTMLElement> {
    fixture = TestBed.createComponent(IconComponent);
    fixture.componentRef.setInput('name', name);
    fixture.componentRef.setInput('size', size);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [IconComponent] }).compileComponents();
  });

  it('draws an ordinary icon as an inline svg', async () => {
    const el = await render('download');
    expect(el.querySelector('svg')).toBeTruthy();
    expect(el.querySelector('img')).toBeNull();
  });

  it('draws the Excel mark as artwork, not as a stroked path', async () => {
    const el = await render('file-excel');
    const img = el.querySelector('img');

    expect(img).toBeTruthy();
    expect(img!.getAttribute('src')).toBe('/icons/excel.png');
    expect(el.querySelector('svg')).toBeNull();
  });

  it('draws the PDF mark as artwork', async () => {
    const el = await render('file-pdf');
    expect(el.querySelector('img')!.getAttribute('src')).toBe('/icons/pdf.png');
  });

  /** Artwork obeys the same size input as every other icon, so buttons keep their baseline. */
  it('honours the size input on artwork', async () => {
    const el = await render('file-excel', 20);
    const img = el.querySelector('img')!;
    expect(img.getAttribute('width')).toBe('20');
    expect(img.getAttribute('height')).toBe('20');
  });

  /** Decorative in both lanes: the label beside it is what a screen reader should read. */
  it('stays hidden from assistive technology in both lanes', async () => {
    expect((await render('file-excel')).querySelector('img')!.getAttribute('aria-hidden')).toBe('true');
    expect((await render('download')).querySelector('svg')!.getAttribute('aria-hidden')).toBe('true');
  });
});
