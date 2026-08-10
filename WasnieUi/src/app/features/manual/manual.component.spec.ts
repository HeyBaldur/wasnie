import { TestBed } from '@angular/core/testing';
import { ComponentFixture } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { ManualComponent } from './manual.component';
import { ManualApiService } from './services/manual.api.service';
import { AssistantStore } from '../assistant/state/assistant.store';

/**
 * The manual screen.
 *
 * ★ WHAT IS WORTH PINNING HERE. Not "a PDF renders" — that is the browser's job, and the reason this
 * screen went back to it. What the tests hold down is what would regress silently: the bytes come through
 * the API service (so the session token travels with them), 404 is a DIFFERENT state from a failure, and
 * the object URL is revoked. That last one leaks the whole document into memory for the life of the tab
 * if it is dropped.
 */
describe('ManualComponent', () => {
  let api: jasmine.SpyObj<ManualApiService>;
  let fixture: ComponentFixture<ManualComponent>;

  const pdfBlob = () => new Blob(['%PDF-1.7 fake'], { type: 'application/pdf' });

  beforeEach(async () => {
    api = jasmine.createSpyObj<ManualApiService>('ManualApiService', ['getPdf', 'getStatus']);

    await TestBed.configureTestingModule({
      imports: [ManualComponent, TranslateModule.forRoot()],
      providers: [
        // The app shell pulls in InactivityService -> AuthService -> HttpClient. The manual's own calls
        // go through the mocked ManualApiService; this is only so the shell can be constructed.
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ManualApiService, useValue: api },
      ],
    }).compileComponents();
  });

  function create(): ManualComponent {
    fixture = TestBed.createComponent(ManualComponent);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('fetches the PDF through the API service and shows the viewer', () => {
    api.getPdf.and.returnValue(of(pdfBlob()));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');

    const component = create();

    // ★ Through the service, which means through the interceptor that attaches the token. An
    // <iframe src="/api/manual/pdf"> would have skipped it and arrived unauthenticated.
    expect(api.getPdf).toHaveBeenCalled();
    expect(component.state()).toBe('ready');
    expect(component.documentUrl()).not.toBeNull();
    expect(fixture.nativeElement.querySelector('iframe.manual__frame')).toBeTruthy();
  });

  it('opens the assistant panel from the manual, and HIDES the button without an entitlement', () => {
    // ★ Hidden, not disabled (Spec 5b.6): a user without a seat must never see a button that would 403.
    api.getPdf.and.returnValue(of(pdfBlob()));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    const store = TestBed.inject(AssistantStore);
    const open = spyOn(store, 'open').and.resolveTo(undefined);

    store.entitled.set(false);
    const component = create();
    expect(fixture.nativeElement.querySelector('ws-button[variant="primary"]')).toBeNull();

    store.entitled.set(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('ws-button[variant="primary"]')).toBeTruthy();

    component.askAssistant();
    expect(open).toHaveBeenCalled();
  });

  it('opens the SAME blob in a new tab rather than fetching anything', () => {
    // Full-window reading without a second request, and without the manual ever gaining a public URL.
    api.getPdf.and.returnValue(of(pdfBlob()));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    const open = spyOn(window, 'open');

    create().openFullScreen();

    expect(open).toHaveBeenCalledWith('blob:fake', '_blank', 'noopener');
    expect(api.getPdf).toHaveBeenCalledTimes(1);
  });

  it('treats 404 as "not published yet", NOT as an error', () => {
    // An installation without the manual is an expected state and gets a calm empty state; a network
    // failure gets a retry. Collapsing them would tell every user the product is broken.
    api.getPdf.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404, statusText: 'Not Found' })),
    );

    const component = create();

    expect(component.state()).toBe('unavailable');
    expect(fixture.nativeElement.querySelector('ws-empty-state')).toBeTruthy();
  });

  it('shows a retryable error for a real failure', () => {
    api.getPdf.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' })),
    );

    const component = create();
    expect(component.state()).toBe('error');

    api.getPdf.and.returnValue(of(pdfBlob()));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    component.load();

    expect(component.state()).toBe('ready');
  });

  it('revokes the object URL when the component is destroyed', () => {
    api.getPdf.and.returnValue(of(pdfBlob()));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    const revoke = spyOn(URL, 'revokeObjectURL');

    create();
    fixture.destroy();

    expect(revoke).toHaveBeenCalledWith('blob:fake');
  });

  it('revokes the previous object URL before loading a new one', () => {
    api.getPdf.and.returnValue(of(pdfBlob()));
    spyOn(URL, 'createObjectURL').and.returnValues('blob:first', 'blob:second');
    const revoke = spyOn(URL, 'revokeObjectURL');

    const component = create();
    component.load();

    expect(revoke).toHaveBeenCalledWith('blob:first');
    expect(component.state()).toBe('ready');
  });
});
