import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { TranslatePipe } from '@ngx-translate/core';
import { AppShellComponent } from '../../shared/components/app-shell/app-shell.component';
import { WsCardComponent, WsButtonComponent, WsEmptyStateComponent } from '../../shared/ui';
import { ManualApiService } from './services/manual.api.service';
import { AssistantStore } from '../assistant/state/assistant.store';

type ManualState = 'loading' | 'ready' | 'unavailable' | 'error';

/**
 * The user manual, rendered inside the app from an authenticated fetch.
 *
 * ★ THE BROWSER'S OWN PDF VIEWER, ON PURPOSE. A custom renderer (PDF.js drawing to canvases) was built
 * and then removed: it produced blank and clipped pages, and each fix bought a new failure mode. The
 * browser's viewer displays the whole document correctly, at every zoom, with selectable text and working
 * Ctrl+F — none of which the canvas version had. Fewer moving parts beat a viewer we have to keep
 * repairing. If it is ever revisited, the bar is: all 44 pages correct on the first try.
 *
 * ★ WHAT THIS SCREEN PROMISES. The document is behind the login: the bytes come from `/api/manual/pdf`,
 * which requires the session token, and no public URL to the file exists. What it does NOT promise —
 * anywhere, in code or on screen — is that the PDF cannot be saved. The browser's viewer renders it, and
 * a rendered PDF is a savable PDF.
 *
 * ★ THE FETCH IS A BLOB, AND THAT IS LOAD-BEARING. The session is a JWT that an interceptor attaches as a
 * request HEADER; the browser does not run interceptors for iframe/embed loads, so pointing the frame
 * straight at the endpoint would arrive unauthenticated and be refused. Fetching the bytes here and
 * handing the frame an object URL is what lets an authenticated document be displayed at all.
 */
@Component({
  selector: 'app-manual',
  standalone: true,
  imports: [
    AppShellComponent,
    TranslatePipe,
    WsCardComponent,
    WsButtonComponent,
    WsEmptyStateComponent,
  ],
  templateUrl: './manual.component.html',
  styleUrl: './manual.component.scss',
})
export class ManualComponent implements OnInit {
  private readonly api = inject(ManualApiService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Root-provided, and the same instance the topbar trigger uses — which is what lets a button on this
   * screen open the panel that lives in the shell.
   */
  readonly assistant = inject(AssistantStore);

  readonly state = signal<ManualState>('loading');
  readonly documentUrl = signal<SafeResourceUrl | null>(null);

  /**
   * The object URL as a plain string, kept so it can be revoked. A SafeResourceUrl cannot be read back,
   * and an un-revoked blob holds the whole PDF in memory for the life of the tab.
   */
  private objectUrl: string | null = null;

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => this.release());
    this.load();
  }

  load(): void {
    this.release();
    this.state.set('loading');

    this.api.getPdf().subscribe({
      next: blob => {
        this.objectUrl = URL.createObjectURL(blob);

        // SAFETY NOTE (ARCHITECTURE.md Rule 4.6.1): bypassSecurityTrustResourceUrl is safe here because
        // the value is a blob: URL this component just minted from a response body of our own API. It is
        // never user input, never interpolated, and never comes from the page.
        //
        // ★ `view=FitH` — FIT TO WIDTH, AND HERE THAT IS RIGHT RATHER THAN WRONG. It was refused before
        // for a good reason: across a full-width column it makes the type enormous on a large monitor.
        // The document now lives in a COLUMN OF BOUNDED WIDTH beside the panel, so filling that width
        // gives a page far larger than the previous fit-to-page (which sized the paper from the frame's
        // HEIGHT and left it small), while the cap keeps it from ever becoming huge. It also leaves the
        // viewer no room to paint grey down the sides.
        this.documentUrl.set(
          this.sanitizer.bypassSecurityTrustResourceUrl(
            `${this.objectUrl}#toolbar=0&navpanes=0&view=FitH`,
          ),
        );
        this.state.set('ready');
      },
      error: (err: HttpErrorResponse) => {
        // 404 is not a failure, it is "this installation has no manual yet" — a different message and a
        // different tone from a network or permission error, which is why they are separate states.
        this.state.set(err.status === 404 ? 'unavailable' : 'error');
      },
    });
  }

  /**
   * Opens the document in its own tab.
   *
   * ★ THIS IS THE ANSWER TO "IT READS TOO SMALL", and it costs nothing: it hands the browser the SAME
   * blob already in memory, so no request is made, the manual never gains a public URL, and the reader
   * gets the whole window plus the browser's own zoom controls.
   */
  openFullScreen(): void {
    if (this.objectUrl) {
      window.open(this.objectUrl, '_blank', 'noopener');
    }
  }

  /** Opens the assistant panel that lives in the shell. The manual stays on screen behind it. */
  askAssistant(): void {
    void this.assistant.open();
  }

  private release(): void {
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
    this.documentUrl.set(null);
  }
}
