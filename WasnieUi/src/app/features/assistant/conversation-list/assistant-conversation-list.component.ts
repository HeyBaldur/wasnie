import { Component, HostListener, computed, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TranslateModule } from '@ngx-translate/core';

import { AssistantStore } from '../state/assistant.store';
import { isUntitled } from '../models/assistant.model';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { WsConfirmationModalComponent, WsInputComponent, WsPopoverComponent } from '../../../shared/ui';
import { ConversationGroup, filterConversations, groupConversations } from './conversation-groups';

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
   * Live filter on the title. The host owns the box, this owns the filtering.
   *
   * ★ CLIENT-SIDE ON PURPOSE, and it is not a shortcut. The store already holds the whole list — it is
   * what the rail renders — so a server round trip per keystroke would return the set the browser is
   * already sitting on, only later. If the list ever outgrows one page this becomes a real query; today
   * that would be a slower answer to a question already answered.
   */
  readonly query = input('');

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
    const matching = filterConversations(this.store.conversations(), this.query());

    if (!this.grouped() || this.query().trim().length > 0) {
      return [{ key: 'today', labelKey: '', items: matching }];
    }

    return groupConversations(matching, new Date());
  });

  /** How many rows will actually render — drives the empty state, which differs when searching. */
  readonly visibleCount = computed(
    () => this.groups().reduce((n, g) => n + g.items.length, 0));

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
