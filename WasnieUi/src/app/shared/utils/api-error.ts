import { HttpErrorResponse } from '@angular/common/http';

export function extractApiError(err: unknown, fallback = 'ERRORS.GENERIC'): string {
  if (err instanceof HttpErrorResponse) {
    const msg = err.error?.message;
    if (typeof msg === 'string' && msg.trim()) return msg;
  }
  return fallback;
}

/**
 * A refusal the server expressed as a CODE AND ITS DATA instead of a sentence.
 *
 * ★ THE CODE IS NOT A MESSAGE AND MUST NEVER BE PRINTED. It is an internal identifier
 * ("RateTableTiersOverlap"); showing it to a user is showing them nothing. Every caller is expected
 * to run it through its own explicit whitelist and fall back to a generic sentence for anything it
 * does not recognise — see `rateTableErrorKey` in the rule form for the shape.
 */
export interface ApiErrorCode {
  code: string;
  /** The values the sentence needs, by name. Numbers arrive as numbers. */
  parameters: Record<string, unknown>;
}

/**
 * Reads the coded half of a 422, when the server sent one.
 *
 * ★★ THIS EXISTS BECAUSE `extractApiError` CAN ONLY RETURN AN ENGLISH SENTENCE. The plain error
 * shape carries `message`, which the backend writes in English and the toast paints unchanged — so a
 * reader in Spanish or Polish gets English. A coded 422 carries `code` and `parameters` instead, and
 * the wording lives in the translation files where it can be fixed without a redeploy.
 *
 * ★ IT RETURNS NULL RATHER THAN GUESSING. A response without a usable `code` is not a coded refusal,
 * and the caller must fall back to `extractApiError` — never invent a key from whatever arrived.
 */
export function extractApiErrorCode(err: unknown): ApiErrorCode | null {
  if (!(err instanceof HttpErrorResponse)) return null;

  const code = err.error?.code;
  if (typeof code !== 'string' || !code.trim()) return null;

  const parameters = err.error?.parameters;
  return {
    code,
    parameters:
      parameters && typeof parameters === 'object' && !Array.isArray(parameters)
        ? (parameters as Record<string, unknown>)
        : {},
  };
}
