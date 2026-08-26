import {
  Component,
  HostListener,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';

import { AssistantStore } from '../state/assistant.store';
import { isUntitled } from '../models/assistant.model';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import {
  WsButtonComponent,
  WsConfirmationModalComponent,
  WsInputComponent,
  WsPopoverComponent,
} from '../../../shared/ui';
import { ConversationGroup, groupConversations } from './conversation-groups';

/**
 * The list of conversations, with its rename and delete affordances.
 *
 * ★ ONE COPY, TWO LAYOUTS. The drawer shows this as a dropdown that covers the chat; the full page at
 * /assistant shows it as a permanent left column. Those are two different SHAPES of the same list, so
 * the rows, the rename flow and the delete confirmation live here once and each host supplies the box.
 * Nothing in this component sets a width or a position for that reason.
 *
 * ★ IT DOES NOT OPEN THE CONVERSATION ITSELF. It emits `select`, because the two hosts have to react
 * differently: the drawer just loads it into the shared store, while the page must ALSO put the id in
 * the URL so a refresh comes back to the same thread. Calling `openConversation` from in here would
 * have quietly denied the page that.
 */
@Component({
  selector: 'app-assistant-conversation-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TranslateModule,
    IconComponent,
    WsButtonComponent,
    WsInputComponent,
    WsPopoverComponent,
    WsConfirmationModalComponent,
  ],
  templateUrl: './assistant-conversation-list.component.html',
  styleUrl: './assistant-conversation-list.component.scss',
})
export class AssistantConversationListComponent {
  readonly store = inject(AssistantStore);

  /** A row was chosen. The host decides what "open" means for its context — see the class comment. */
  readonly select = output<string>();

  /**
   * The term the last request actually searched for.
   *
   * ★★ IT MOVED TO THE SERVER, AND THE OLD COMMENT HERE EXPLAINED WHY IT DID NOT NEED TO: the store
   * held the whole list, so filtering it in the browser answered a question the browser had already
   * answered. That reasoning was correct and it expired the moment the list became paged — a filter
   * over the loaded batch says "no results" while the match sits forty rows further down, unloaded.
   * Which is the same untruth as telling somebody a record does not exist because the lookup could not
   * reach it.
   *
   * ★ READ FROM THE STORE, NOT AN INPUT. Both hosts have a search box now, and both have to agree
   * about what is being searched — an input per host is two sources for one fact.
   */
  readonly query = computed(() => this.store.searchTerm());

  /**
   * Cut the list into time bands.
   *
   * ★ OFF BY DEFAULT so the drawer is untouched. Its dropdown shows a handful of recent threads in a
   * small box, where four headings would be most of what is on screen. The full page, which is where
   * someone actually goes looking through months of conversations, turns it on.
   */
  readonly grouped = input(false);

  /**
   * Grouping is computed against the moment the list is BUILT, not per row — otherwise a list rendered
   * across midnight could file two rows from the same minute under different days.
   */
  readonly groups = computed<ConversationGroup[]>(() => {
    // ★ NO CLIENT-SIDE FILTER ANY MORE. What the store holds IS the answer to the current search —
    // filtering it again here would narrow a set the server already narrowed, and would quietly hide
    // rows whose match is real but whose folding differs between the two implementations.
    const items = this.store.conversations();

    if (!this.grouped() || this.query().length > 0) {
      return [{ key: 'today', labelKey: '', items }];
    }

    // ★ GROUPING STILL WORKS BATCH BY BATCH, and it needs nothing new to do so: it is computed over
    // the WHOLE accumulated list every time, so a batch that arrives with rows from two bands lands in
    // both without duplicating a heading or reordering anything already on screen.
    return groupConversations(items, new Date());
  });

  /** How many rows will actually render — drives the empty state, which differs when searching. */
  readonly visibleCount = computed(
    () => this.groups().reduce((n, g) => n + g.items.length, 0));

  /**
   * What the box shows, which is NOT the same signal as what was searched for.
   *
   * ★ TWO SIGNALS ON PURPOSE. The box has to echo every keystroke immediately or typing feels broken;
   * `store.searchTerm` only moves when a request actually goes out, 300ms later. Reading the box from
   * the applied term would make characters appear a third of a second after they were typed.
   *
   * ★★ AND IT IS SEEDED FROM THE STORE, WHICH IS THE WHOLE OF A REPORTED BUG. Starting at `''` made the
   * box lie: this component is DESTROYED AND REBUILT every time the drawer's history panel is toggled
   * (`@if (store.historyOpen())`), and the page mounts a second copy of it — while the applied term
   * lives in the root-provided store and survives all of that. So a search, then a toggle, and the user
   * is looking at an EMPTY search box above a list that is still filtered. Reported as "I cleared the
   * search and my chats did not come back", which is exactly what it looks like from the outside.
   *
   * ★ AND IT FOLLOWS THE STORE WHEN THE CHANGE CAME FROM SOMEWHERE ELSE. Seeding once at construction
   * fixes the remount, and there is a sibling case it does not reach: the drawer lives in the app
   * shell and the page owns a rail, so TWO copies of this component can be alive at the same time.
   * Searching in one left the other's box stale — the same lie, arriving by a different door.
   */
  readonly searchBox = signal(this.store.searchTerm());

  /**
   * The last term THIS box handed to the store, so the sync below can tell "somebody else changed the
   * search" from "the store is echoing back what I just typed".
   *
   * ★ WITHOUT IT THE EFFECT FIGHTS THE USER: every keystroke eventually lands in the store, the effect
   * sees the store change, and it writes that value back into the box — 300ms behind the caret.
   */
  private lastSent: string | null = null;

  constructor() {
    effect(() => {
      const applied = this.store.searchTerm();

      untracked(() => {
        if (applied !== this.lastSent) {
          this.searchBox.set(applied);
        }
      });
    });
  }

  onSearchChange(value: string): void {
    // What the store will settle on for this text — below the minimum it settles on "no search", and
    // the box must not be reset to empty just because the user is still on their first character.
    this.lastSent = value.trim().length >= AssistantStore.MIN_SEARCH_LENGTH ? value.trim() : '';
    this.searchBox.set(value);
    this.store.setSearch(value);
  }

  /**
   * ★ AN EXPLICIT BUTTON, NOT INFINITE SCROLL. Scrolling that fetches is unpredictable (a flick of the
   * wheel spends requests nobody asked for), it fights keyboard users by moving focus out from under
   * them, and it makes "I have reached the end" unobservable. A button says what it will do and does it
   * only when asked.
   */
  async loadMore(): Promise<void> {
    await this.store.loadMoreConversations();
  }

  /**
   * Pin or unpin, whichever this row is not.
   *
   * ★ ONE HANDLER, NOT TWO MENU ITEMS. The row shows exactly one of Pin / Unpin, so a second entry
   * would be an entry that is never visible — and two handlers would be two places to keep the
   * optimistic move and its revert in step.
   */
  async togglePin(conversationId: string, event: Event): Promise<void> {
    // The menu's click must not also reach the row underneath and open the conversation.
    event.stopPropagation();

    if (this.store.isPinned(conversationId)) {
      await this.store.unpinConversation(conversationId);
      return;
    }

    await this.store.pinConversation(conversationId);
  }

  /** Retry after a failed batch. The same call either way — the store knows which batch it was on. */
  async retryLoad(): Promise<void> {
    if (this.store.hasMoreConversations() && this.store.conversations().length > 0) {
      await this.store.loadMoreConversations();
      return;
    }

    await this.store.loadConversations();
  }

  readonly pendingDeleteId = signal<string | null>(null);
  readonly renamingId = signal<string | null>(null);
  readonly renameDraft = signal('');

  /** True while the thread has no name yet, so the template renders the translated label instead. */
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
    if (this.renamingId() === null) {
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

  startRename(id: string, title: string | null, event: Event): void {
    // Stops the row's own click from also opening the conversation being renamed.
    event.stopPropagation();
    // An untitled thread starts EMPTY rather than pre-filled with the placeholder label: the label is
    // a translated stand-in, not a name, and committing it would store the word "Untitled" as a title.
    this.renameDraft.set(isUntitled(title) ? '' : (title ?? ''));
    this.renamingId.set(id);
  }

  /**
   * Enter: keep the new name.
   *
   * ★ CLOSED FIRST, and that ordering is load-bearing. Committing moves focus out of the field, which
   * fires `focusout`, which cancels — so if the row were still in edit mode at that point the cancel
   * would land on a rename already in flight. Closing first makes the cancel a no-op.
   */
  async commitRename(): Promise<void> {
    const id = this.renamingId();
    if (id === null) {
      return;
    }

    this.renamingId.set(null);

    const title = this.renameDraft().trim();
    // An empty box is not a request to erase the name; it is a user who changed their mind.
    if (title.length > 0) {
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
    this.renamingId.set(null);
  }

  askDelete(id: string, event: Event): void {
    // Stops the row's own click from also opening the conversation being deleted.
    event.stopPropagation();
    this.pendingDeleteId.set(id);
  }

  async confirmDelete(): Promise<void> {
    const id = this.pendingDeleteId();
    if (id) {
      await this.store.remove(id);
    }
    this.pendingDeleteId.set(null);
  }

  cancelDelete(): void {
    this.pendingDeleteId.set(null);
  }
}
