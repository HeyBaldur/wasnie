import { SubscriptionStateService } from '../services/subscription-state.service';

/** The query parameter Stripe Checkout returns with after a successful token purchase. */
export const TOKEN_PURCHASE_PARAM = 'tokens';
export const TOKEN_PURCHASE_SUCCESS = 'success';

/** How long to wait before looking again for a balance the webhook may not have credited yet. */
export const TOKEN_PURCHASE_RETRY_MS = 3000;

/**
 * Picks up the account's new balance after a token purchase (KAN-83 UX pass).
 *
 * ★★ THE PAYMENT AND THE CREDIT ARE NOT THE SAME EVENT. Stripe sends the customer back the instant the card clears,
 * but the tokens are credited by the WEBHOOK, which is a separate delivery arriving a beat later. Reading the balance
 * once, immediately, is how someone who just paid lands back on a chat that still says it is out of tokens. So this
 * reads, and — only if the account still looks exhausted — reads once more a few seconds later.
 *
 * ★ ONE RETRY, NOT A POLL. A loop hammering the endpoint would still not make the webhook arrive sooner, and a webhook
 * that never arrives is a real failure the customer should see rather than a spinner that spins forever.
 *
 * ★ THE QUERY PARAMETER IS READ FROM THE SNAPSHOT ON PURPOSE. This is a full navigation back from Stripe, not an
 * in-app filter change, so the value cannot change underneath the component that read it.
 */
export function refreshAfterTokenPurchase(
  params: { [key: string]: unknown },
  state: SubscriptionStateService,
  schedule: (fn: () => void, ms: number) => void = (fn, ms) => setTimeout(fn, ms),
): boolean {
  if (params[TOKEN_PURCHASE_PARAM] !== TOKEN_PURCHASE_SUCCESS) return false;

  state.refresh();

  schedule(() => {
    const access = state.access();
    const stillOut =
      access?.assistantTokensUsed != null &&
      access.assistantTokenLimit != null &&
      access.assistantTokensUsed >= access.assistantTokenLimit &&
      (access.assistantBoostRemaining ?? 0) <= 0;

    if (stillOut) state.refresh();
  }, TOKEN_PURCHASE_RETRY_MS);

  return true;
}
