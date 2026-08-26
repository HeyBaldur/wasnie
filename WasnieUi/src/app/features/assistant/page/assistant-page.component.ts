import { Component, DestroyRef, HostListener, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TranslateModule } from '@ngx-translate/core';

import { AssistantStore } from '../state/assistant.store';
import { isUntitled } from '../models/assistant.model';
import { AssistantConversationComponent } from '../conversation/assistant-conversation.component';
import { AssistantConversationListComponent } from '../conversation-list/assistant-conversation-list.component';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import {
  WsButtonComponent,
  WsConfirmationModalComponent,
  WsEmptyStateComponent,
  WsInputComponent,
  WsPopoverComponent,
} from '../../../shared/ui';
import { IconComponent } from '../../../shared/components/icon/icon.component';

/**
 * The assistant with room to read: conversations on the left, the active thread on the right, inside
 * the ordinary app layout.
 *
 * ★ IT IS A SECOND VIEW, NOT A SECOND ASSISTANT. The drawer stays exactly as it was for quick
 * questions; this is the same conversation shown wide, for the answers that carry a balance, a
 * calculation or a table — the ones a 420px slide-over makes hard to read. Both mount
 * `app-assistant-conversation` and both read `AssistantStore` (`providedIn: 'root'`), so a message
 * typed in one is already in the other. Nothing is copied between them because there is only one
 * conversation.
 *
 * ★ WHY THE ID IS IN THE URL. Inside the app the store alone would be enough — expanding from the
 * drawer finds the thread already loaded. It is a reload that needs the id: the store is recreated
 * empty on a hard refresh, so without `/assistant/:conversationId` an F5 would drop the user into a
 * blank chat with their conversation apparently gone. The id makes the URL survivable, bookmarkable
 * and pasteable.
 */
@Component({
  selector: 'app-assistant-page',
  standalone: true,
  imports: [
    FormsModule,
    TranslateModule,
    AppShellComponent,
    IconComponent,
    WsButtonComponent,
    WsConfirmationModalComponent,
    WsEmptyStateComponent,
    WsInputComponent,
    WsPopoverComponent,
    AssistantConversationComponent,
    AssistantConversationListComponent,
  ],
  templateUrl: './assistant-page.component.html',
  styleUrl: './assistant-page.component.scss',
})
export class AssistantPageComponent implements OnInit {
  readonly store = inject(AssistantStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** The requested conversation could not be loaded — gone, or never the caller's. See the template. */
  readonly notFound = signal(false);



  // ── The active conversation's own actions, from the chat header ───────────
  readonly renaming = signal(false);
  readonly renameDraft = signal('');
  readonly confirmingDelete = signal(false);

  /** True while the thread has no name yet, so the header renders the translated label instead. */
  isUntitled(title: string | null | undefined): boolean {
    return isUntitled(title);
  }

  /**
   * A click anywhere outside the rename box abandons the edit.
   *
   * ★ THE BELT TO focusout'S BRACES, AND BOTH ARE NEEDED. `focusout` covers tabbing away and every
   * ordinary click, but it can only fire on a box that HELD focus — which is why this bug survived one
   * fix already. This path does not care about focus at all, so a browser that declines to blur on some
   * particular click cannot strand the editor on screen again.
   *
   * ★ THE OPENING CLICK CANNOT REACH THIS. `startRename` stops the click that opened the box from
   * propagating, so the very event that turned renaming on does not immediately turn it off — the
   * classic self-closing bug for document listeners paired with open-on-click.
   */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.renaming()) {
      return;
    }

    // `closest?.` because a click is not always delivered at an Element — a synthetic one aimed at the
    // document has no such method, and a handler that throws here would leave the box open again.
    const target = event.target as HTMLElement | null;
    if (target?.closest?.('[data-rename-box]')) {
      return;
    }

    this.cancelRename();
  }

  startRename(event: Event): void {
    // Keeps the click that opened the box from reaching the document listener above, which would
    // otherwise close it in the same tick it was opened.
    event.stopPropagation();

    const title = this.store.conversation()?.title;
    // An untitled thread starts EMPTY rather than pre-filled with the placeholder label: the label is
    // a translated stand-in, not a name, and committing it would store the word "Untitled" as a title.
    this.renameDraft.set(isUntitled(title) ? '' : (title ?? ''));
    this.renaming.set(true);
  }

  /**
   * Enter: keep the new name.
   *
   * ★ CLOSED FIRST, and that ordering is load-bearing now. Committing moves focus out of the field,
   * which fires `focusout`, which cancels — so if the box were still open at that point the cancel
   * would land on a rename already in flight. Closing first makes the cancel a no-op.
   */
  async commitRename(): Promise<void> {
    const id = this.store.conversation()?.id;
    this.renaming.set(false);

    const title = this.renameDraft().trim();
    // An empty box is not a request to erase the name; it is a user who changed their mind.
    if (id && title.length > 0) {
      await this.store.rename(id, title);
    }
  }

  /**
   * Escape, or clicking away: drop the edit and keep the stored name.
   *
   * ★ CLICKING AWAY ABANDONS, IT DOES NOT SAVE. Half-typed text that leaves on its own and becomes the
   * conversation's name would be a change nobody asked for, on a field with no undo. Enter is the
   * deliberate act; everything else backs out.
   */
  cancelRename(): void {
    this.renaming.set(false);
  }

  askDelete(): void {
    this.confirmingDelete.set(true);
  }

  /**
   * Deletes the conversation the header names, then leaves the URL that pointed at it.
   *
   * ★ THE NAVIGATION IS THE POINT. Staying on /assistant/{id} after deleting it would leave the page
   * asking for a thread that no longer exists, and the next reload would land on "not found" — the user
   * would have deleted something and been handed an error about it.
   */
  async confirmDelete(): Promise<void> {
    const id = this.store.conversation()?.id;
    this.confirmingDelete.set(false);

    if (id) {
      await this.store.remove(id);
      await this.router.navigate(['/assistant']);
    }
  }

  ngOnInit(): void {
    // The trigger normally does this from the topbar; a deep link can land here without ever having
    // rendered it, and the page must know whether the assistant is available before showing a chat.
    void this.store.loadEntitlement();
    void this.store.loadConversations();

    // SUBSCRIBE, don't snapshot: selecting another conversation in the rail changes only the route
    // PARAM, and Angular reuses this component when it does. A snapshot read in ngOnInit would load
    // the first thread and then never react again — the list would highlight a new row while the chat
    // kept showing the old one.
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(params => void this.openFromUrl(params.get('conversationId')));
  }

  /**
   * Loads whatever the URL names.
   *
   * ★ THE RE-ENTRY CHECK IS WHAT MAKES /assistant/{id} HONEST. `openConversation` leaves the previous
   * conversation in place when the request fails, so without comparing ids afterwards a bad link would
   * silently show whichever thread happened to be loaded — the user would read someone else's URL and
   * see their own chat, and believe the link worked. Comparing is also why nothing is asserted about
   * WHY it failed: the backend answers 404 for "not yours" and "never existed" alike, on purpose.
   */
  private async openFromUrl(id: string | null): Promise<void> {
    this.notFound.set(false);

    if (id === null) {
      return;
    }

    // Already the live conversation — expanding from the drawer lands here, and re-fetching would only
    // throw away the streaming turn the user is watching.
    if (this.store.conversation()?.id === id) {
      return;
    }

    await this.store.openConversation(id);
    this.notFound.set(this.store.conversation()?.id !== id);
  }

  /** Opens a thread AND puts it in the URL, so this view stays reloadable. */
  async openConversation(id: string): Promise<void> {
    await this.router.navigate(['/assistant', id]);
  }

  /** Starts a thread and moves the URL onto it, so a refresh comes back to the new one. */
  async startNew(): Promise<void> {
    await this.store.startConversation();

    const id = this.store.conversation()?.id;
    if (id) {
      await this.router.navigate(['/assistant', id]);
    }
  }
}
