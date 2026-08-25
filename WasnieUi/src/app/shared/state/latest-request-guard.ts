/**
 * Makes the LAST-REQUESTED response win, instead of the last-ARRIVED one.
 *
 * A list store fires a fetch from an `effect()` on its filter signal. Change the filter while a fetch
 * is still in flight and there are two requests racing; without a guard, whichever the network happens
 * to return last overwrites the other. When the slow one is the older, wider query, the user ends up
 * staring at UNFILTERED data under a filtered UI — and it stays that way until a manual reload, which
 * is precisely the "I have to press F5" symptom.
 *
 * Reproduced deterministically on 2026-08-18 (WI-1 Paso 0.2): on `PayRunsStore`, an in-flight
 * unfiltered request arriving after a `status=Draft` request left the list showing every pay run.
 * `TransactionsStore` already carried this counter inline and survived the same scenario; this class
 * is that guard extracted so every store can share it.
 *
 * Note on what this does NOT protect against: the comment previously sitting on the inline counter
 * blamed `refreshOnEnter` firing with a stale filter while `ngOnInit` applied the URL one. That does
 * not happen — a component's `ngOnInit` runs before its template's directives, so `refresh()` already
 * sees the new filter (verified in the same repro: both re-entry requests carried the correct filter).
 * Re-entry does issue two identical requests; the guard makes the duplicate harmless.
 *
 * ```ts
 * private readonly _latest = new LatestRequestGuard();
 *
 * const token = this._latest.begin();
 * const data = await firstValueFrom(this.api.list(params));
 * if (this._latest.isStale(token)) return;   // a newer load was requested — drop this response
 * this.pagedResult.set(data);
 * ```
 *
 * Guard all three exits, not just the happy one: a stale FAILURE must not overwrite a fresh result
 * with an error banner, and a stale request finishing must not clear the spinner while the current
 * load is still running.
 */
export class LatestRequestGuard {
  private seq = 0;

  /** Call before starting a request. Returns the token identifying it. */
  begin(): number {
    return ++this.seq;
  }

  /** True when a newer request has been started since `token` — its response must be discarded. */
  isStale(token: number): boolean {
    return token !== this.seq;
  }
}
