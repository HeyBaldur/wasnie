import { Injectable, signal } from '@angular/core';

/**
 * Which collapsible sidebar groups are open.
 *
 * ★★ IT LIVES IN A SERVICE BECAUSE THE SIDEBAR DOES NOT SURVIVE A NAVIGATION. Every one of the 41
 * feature templates renders its own `<app-shell>`, and the shell contains the sidebar — so changing
 * page destroys and rebuilds the whole aside. While this state was a signal on the component, each
 * rebuild started with every group CLOSED and the auto-expand effect immediately reopened the one
 * holding the active route: the group visibly collapsed and sprang back on every click. That is the
 * blink, and a root-provided signal removes it because the set is simply still there.
 *
 * ★ IT ALSO STOPS LOSING THE USER'S OWN CHOICE. A group somebody opened by hand was forgotten at the
 * next navigation, which read as the sidebar undoing them.
 *
 * ★ NOT PERSISTED TO localStorage, unlike the collapsed/expanded rail. Which group is open is a
 * within-session convenience, and the auto-expand effect already opens the right one on a fresh load.
 */
@Injectable({ providedIn: 'root' })
export class SidebarGroupsService {
  private readonly _expanded = signal<ReadonlySet<string>>(new Set<string>());

  readonly expanded = this._expanded.asReadonly();

  isExpanded(key: string): boolean {
    return this._expanded().has(key);
  }

  toggle(key: string): void {
    this._expanded.update(current => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  /** Open a group, leaving the set alone when it is already open — the auto-expand path. */
  open(key: string): void {
    this._expanded.update(current => {
      if (current.has(key)) return current;
      const next = new Set(current);
      next.add(key);
      return next;
    });
  }
}
