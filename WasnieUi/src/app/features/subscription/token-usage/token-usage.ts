import { AccountAccess } from '../services/subscription.service';

/**
 * How the assistant's token usage reads for one account (KAN-80, KAN-83). Pure, so both screens and the specs share
 * one rule.
 *
 * ★ TWO SHAPES, AND THEY ARE NOT THE SAME FACT. A trial has a one-off ALLOWANCE counted since the account began; a
 * paying tenant has the plan's INCLUDED tokens for the current billing period, which reset at renewal. A locked account
 * shows nothing — there is no usage it can act on.
 *
 * ★ WHAT CHANGED IN KAN-83. A paying account used to have no limit at all, so its shape carried only "used this
 * period". It now has a ceiling, and on top of it whatever boost it bought — which does NOT reset, and expires. The
 * boost is reported alongside the allowance rather than folded into it: one resets in days and the other is money the
 * customer spent, and a single merged number would hide which of the two they are about to lose (§B3).
 */
export type TokenUsageView =
  | {
      kind: 'trial';
      used: number;
      limit: number;
      fraction: number;
      exhausted: boolean;
      nearLimit: boolean;
    }
  | {
      kind: 'period';
      used: number;
      limit: number;
      fraction: number;
      since: string | null;
      /** Purchased tokens still available. 0 when none were bought. */
      addonRemaining: number;
      /** When the soonest surviving lot dies. Null when no boost remains. */
      addonExpiresAt: string | null;
      /** Purchased tokens that died unused, so the loss is visible. */
      addonExpired: number;
      /** Included AND boost are both gone: the assistant has stopped. */
      exhausted: boolean;
      /** Close to the end of everything available — not just of the included share. */
      nearLimit: boolean;
    }
  | { kind: 'none' };

/** From this share of the allowance the meter warns: the assistant is about to stop. */
export const TOKEN_USAGE_WARNING_FRACTION = 0.8;

/**
 * The same threshold seen from the other side: warn once this share of the allowance is all that is left.
 *
 * ★★ WRITTEN OUT, NOT DERIVED AS `1 - 0.8`. That subtraction is 0.19999999999999996 in binary floating point, so a
 * workspace sitting exactly on the threshold — 600,000 left of 3,000,000 — compared as 0.2 <= 0.19999999999999996 and
 * got NO warning at all. Found on screen, not in a test: the arithmetic is invisible until the number lands exactly
 * on the line.
 */
export const TOKEN_USAGE_WARNING_REMAINING_FRACTION = 0.2;

export function tokenUsageView(access: AccountAccess | null): TokenUsageView {
  if (!access || access.assistantTokensUsed == null) {
    return { kind: 'none' };
  }

  const used = access.assistantTokensUsed;
  const limit = access.assistantTokenLimit;

  if (access.state === 'Trial' && limit != null && limit > 0) {
    // Capped at 1 for the bar: a turn that started under the limit can end over it, and a bar past its track is noise.
    const fraction = Math.min(Math.max(used / limit, 0), 1);
    return {
      kind: 'trial',
      used,
      limit,
      fraction,
      exhausted: used >= limit,
      nearLimit: fraction >= TOKEN_USAGE_WARNING_FRACTION,
    };
  }

  if (access.state === 'Active') {
    const included = limit ?? 0;
    const addonRemaining = access.assistantBoostRemaining ?? 0;

    // ★ THE BAR MEASURES THE INCLUDED SHARE, and the boost is shown beside it. Spreading the bar across included plus
    // boost would make a tenant with a large boost look barely used on the very day their monthly allowance ran out.
    const fraction = included > 0 ? Math.min(Math.max(used / included, 0), 1) : 0;

    // ★ "OUT OF TOKENS" MEANS THE ASSISTANT STOPPED, which is the server's rule: included AND boost both gone. Saying
    // it while a paid boost still answers would send someone to buy what they already have.
    const includedRemaining = Math.max(0, included - used);
    const totalRemaining = includedRemaining + addonRemaining;

    return {
      kind: 'period',
      used,
      limit: included,
      fraction,
      since: access.assistantTokensSince,
      addonRemaining,
      addonExpiresAt: access.assistantBoostExpiresAt,
      addonExpired: access.assistantBoostExpired ?? 0,
      exhausted: included > 0 && totalRemaining <= 0,
      nearLimit:
        included > 0 && totalRemaining > 0
        && totalRemaining <= included * TOKEN_USAGE_WARNING_REMAINING_FRACTION,
    };
  }

  return { kind: 'none' };
}
