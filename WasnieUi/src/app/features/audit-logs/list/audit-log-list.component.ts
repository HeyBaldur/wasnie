import { Component, computed, DestroyRef, ElementRef, inject, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';
import { TranslateModule } from '@ngx-translate/core';
import { AppShellComponent } from '../../../shared/components/app-shell/app-shell.component';
import { IconComponent } from '../../../shared/components/icon/icon.component';
import { DateFormatPipe } from '../../../shared/pipes/date-format.pipe';
import { RefreshOnEnterDirective } from '../../../shared/directives/refresh-on-enter.directive';
import { findScrollContainer } from '../../../shared/utils/find-scroll-container';
import { AuditLogStore } from '../state/audit-log.store';
import { AuditLogRow, SYSTEM_ACTOR } from '../models/audit-log.model';
import { actionKey, resourceLink } from '../models/audit-action';
import {
  WsButtonComponent,
  WsBadgeComponent,
  WsCardComponent,
  WsSelectComponent,
  WsDatePickerComponent,
  WsPageLayoutComponent,
  WsTableComponent,
  WsTableEmptyComponent,
  WsEmptyStateComponent,
  WsPaginationComponent,
  WsModalComponent,
  type SelectOption,
} from '../../../shared/ui';

/**
 * The tenant's audit trail (KAN-19, Parte 2).
 *
 * ★★ IT SHOWS WHAT THE LOG CONTAINS, AND NOTHING ELSE. It does not infer, backfill or hide: an
 * action nobody recorded does not appear here, and this screen must never be read as proof that it
 * did not happen. KAN-19's Paso 0 measured the gap — quotas, assignments and rule add/edit/remove
 * write no audit row at all, and approving a payout only writes one when it overlapped — and that
 * gap is a separate ticket, not something a display layer may paper over.
 *
 * ★ READ-ONLY, STRUCTURALLY. There is no action on this page and there must never be one. An audit
 * trail the application can edit is not evidence.
 */
@Component({
  selector: 'app-audit-log-list',
  standalone: true,
  imports: [
    AppShellComponent, RefreshOnEnterDirective, RouterLink, ReactiveFormsModule, TranslateModule,
    IconComponent, DateFormatPipe,
    WsButtonComponent, WsBadgeComponent, WsCardComponent,
    WsSelectComponent, WsDatePickerComponent,
    WsPageLayoutComponent, WsTableComponent, WsTableEmptyComponent,
    WsEmptyStateComponent, WsPaginationComponent, WsModalComponent,
  ],
  templateUrl: './audit-log-list.component.html',
  styleUrl: './audit-log-list.component.scss',
})
export class AuditLogListComponent implements OnInit {
  readonly store = inject(AuditLogStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);

  readonly filterOpen = signal(false);

  readonly form = new FormGroup({
    action: new FormControl<string | null>(null),
    actor: new FormControl<string | null>(null),
    from: new FormControl<string | null>(null),
    to: new FormControl<string | null>(null),
  });

  /**
   * The action dropdown.
   *
   * ★ THE VALUES COME FROM THE SERVER, THE WORDS FROM THE WHITELIST. Those are two different
   * questions: the log may hold a code this build cannot name, and it must still be filterable —
   * it simply reads as the generic label until somebody writes its translations.
   */
  readonly actionOptions = computed<SelectOption[]>(() => [
    { value: '', label: 'AUDIT.FILTER.ALL_ACTIONS' },
    ...this.store.actions().map((code) => ({ value: code, label: actionKey(code) })),
  ]);

  /**
   * The actor dropdown.
   *
   * ★★ "SYSTEM" IS AN OPTION, NOT AN ABSENCE. Rows written by a job carry no actor, and "show me
   * what ran without a person" is one of the questions this page exists to answer. It carries the
   * sentinel rather than an empty string — see SYSTEM_ACTOR for why the obvious encoding silently
   * means "no filter" instead.
   *
   * ★ EMAILS ARE NOT TRANSLATION KEYS, and passing one through `translate` is safe: ngx-translate
   * returns the input unchanged when it is not a key, so an address renders as itself.
   */
  readonly actorOptions = computed<SelectOption[]>(() => [
    { value: '', label: 'AUDIT.FILTER.ALL_ACTORS' },
    { value: SYSTEM_ACTOR, label: 'AUDIT.ACTOR_SYSTEM' },
    ...this.store.actors().map((email) => ({ value: email, label: email })),
  ]);

  /** Translation key for an action code. Whitelisted — never the raw code (§C2). */
  readonly actionKeyFor = actionKey;

  ngOnInit(): void {
    void this.store.loadOptions();
    void this.store.load({ page: 1 });

    this.form.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe((value) => {
        void this.store.load({
          action: value.action || null,
          // ★ NOT `value.actor || null`. The system sentinel is a non-empty word, but writing the
          // falsy shortcut here would be the exact bug the sentinel exists to prevent the day
          // somebody "simplifies" it back to ''. Compared explicitly against "no selection".
          actor: value.actor === '' || value.actor === undefined ? null : value.actor,
          from: value.from || null,
          to: value.to || null,
          page: 1,
        });
      });
  }

  /** The row's entity, or null when its resource type has no screen to land on. */
  linkFor(row: AuditLogRow): readonly string[] | null {
    return resourceLink(row.resourceType, row.resourceId);
  }

  /**
   * What the resource cell reads when there is no display name.
   *
   * ★ THE ID IS THE FALLBACK, NOT AN EMPTY CELL. `ResourceDisplayName` is optional and plenty of
   * historical rows have none; showing nothing would make the row look like it changed nothing.
   */
  resourceLabel(row: AuditLogRow): string {
    return row.resourceDisplayName?.trim() || row.resourceId || '—';
  }

  isSystemActor(email: string | null | undefined): boolean {
    return !email || email.trim().length === 0;
  }

  async clearFilters(): Promise<void> {
    this.form.reset({ action: null, actor: null, from: null, to: null }, { emitEvent: false });
    await this.store.clearFilters();
  }

  async goToPage(page: number): Promise<void> {
    await this.store.goToPage(page);
    this.scrollToTop();
  }

  async goToPageSize(size: number): Promise<void> {
    await this.store.setPageSize(size);
    this.scrollToTop();
  }

  /**
   * ★★ THE PAGE SCROLLS INSIDE app-shell's COLUMN, NOT THE WINDOW, so `window.scrollTo(0, 0)` does
   * nothing here. The scroller is found by walking up from the table and taking the first ancestor
   * that actually overflows, which keeps working if the shell's markup changes.
   *
   * ★★ INSTANT, NOT SMOOTH — copied deliberately from the Reconciliation Centre, where the smooth
   * version was a verified bug. A smooth scroll is an animation, and Angular repaints the new rows
   * into this very scroller a frame later; Chrome cancels an in-flight smooth scroll when its
   * content changes, so the reader stays exactly where they were. Measured there in the browser:
   * with 'smooth' the scroller stayed pinned at its maximum, without it, it lands on 0.
   */
  private scrollToTop(): void {
    const table = this.host.nativeElement.querySelector<HTMLElement>('.ws-table-wrap');
    findScrollContainer(table?.parentElement ?? null)?.scrollTo({ top: 0 });
  }

  openDetail(row: AuditLogRow): void {
    void this.store.openDetail(row.id);
  }

  closeDetail(): void {
    this.store.closeDetail();
  }
}
