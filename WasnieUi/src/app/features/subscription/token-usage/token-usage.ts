import { AccountAccess } from '../services/subscription.service';

/**
 * How the assistant's token usage reads for one account (KAN-80). Pure, so both screens and the specs share one rule.
 *
 * ★ TWO SHAPES, AND THEY ARE NOT THE SAME FACT. A trial has an ALLOWANCE: "used of limit", counted since the account began.
 * A paying account has no limit: "used this billing period", counted from the period's start. A locked account shows
 * nothing — there is no usage it can act on.
 */
export type TokenUsageView =
  | { kind: 'trial'; used: number; limit: number; fraction: number; exhausted: boolean; nearLimit: boolean }
  | { kind: 'period'; used: number; since: string | null }
  | { kind: 'none' };

/** From this share of the allowance the meter warns: the trial's assistant is about to stop. */
export const TOKEN_USAGE_WARNING_FRACTION = 0.8;

export function tokenUsageView(access: AccountAccess | null): TokenUsageView {
  if (!access || access.assistantTokensUsed == null) {
    return { kind: 'none' };
  }

  const used = access.assistantTokensUsed;

  if (access.state === 'Trial' && access.assistantTokenLimit != null && access.assistantTokenLimit > 0) {
    const limit = access.assistantTokenLimit;
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
    return { kind: 'period', used, since: access.assistantTokensSince };
  }

  return { kind: 'none' };
}
