import { AssistantMessage } from './assistant.model';
import {
  clarifyEntityOf,
  clarifyOf,
  clarifyOptionKey,
  clarifyPromptKey,
  clarifyStatusKey,
  openClarify,
} from './clarify';

/**
 * KAN-58 — reading the clarify form off a stored turn.
 *
 * The rules pinned here are the ones that decide whether a panel the user already dealt with comes
 * back, and whether an internal tool name can reach the screen. Both are silent when they break.
 */
describe('clarify', () => {
  const message = (over: Partial<AssistantMessage> = {}): AssistantMessage => ({
    id: 'm1',
    role: 'Assistant',
    content: 'Which would you like?',
    payload: null,
    status: 'Complete',
    sequence: 1,
    createdAt: '2026-09-07T10:00:00Z',
    ...over,
  } as AssistantMessage);

  const payload = (state: string, functions = ['get_payee_balance', 'get_payee_plans']) =>
    JSON.stringify({
      clarify: { options: functions.map((f) => ({ function: f })), state },
    });

  describe('clarifyOf', () => {
    it('reads the options and the state', () => {
      const form = clarifyOf(message({ payload: payload('open') }));

      expect(form).not.toBeNull();
      expect(form!.options.map((o) => o.function)).toEqual(['get_payee_balance', 'get_payee_plans']);
      expect(form!.state).toBe('open');
    });

    /**
     * ★★ THE PAYLOAD IS SHARED GROUND. `resolvedEntities` lives in the same column and carries the ids
     * that let the next turn skip re-asking for a name. Reading the object as if it were the form's
     * own — or writing over it — is the silent failure this coexistence is designed around.
     */
    it('ignores the other keys sharing the payload', () => {
      const shared = JSON.stringify({
        resolvedEntities: { payees: [{ id: 'x', name: 'Ana' }] },
        clarify: { options: [{ function: 'get_payee_balance' }], state: 'open' },
      });

      expect(clarifyOf(message({ payload: shared }))!.options).toHaveSize(1);
    });

    it('is null when the turn carries no form', () => {
      expect(clarifyOf(message({ payload: null }))).toBeNull();
      expect(clarifyOf(message({ payload: '{"resolvedEntities":{}}' }))).toBeNull();
    });

    /** ★ Server-controlled text: unreadable means no panel, never a thrown error over the thread. */
    it('survives a payload it cannot parse', () => {
      expect(clarifyOf(message({ payload: 'not json' }))).toBeNull();
      expect(clarifyOf(message({ payload: '[1,2,3]' }))).toBeNull();
    });

    /**
     * ★★ AN OPTION THIS BUILD CANNOT NAME IS DROPPED, NOT RENDERED. The alternative is a button
     * labelled `get_payouts` — an internal identifier on screen, which is exactly what rule 10a
     * forbids the assistant itself from doing.
     */
    it('drops an option whose function it cannot label', () => {
      const form = clarifyOf(message({ payload: payload('open', ['get_payouts', 'get_payee_balance']) }));

      expect(form!.options.map((o) => o.function)).toEqual(['get_payee_balance']);
    });

    it('is null when every option was dropped', () => {
      expect(clarifyOf(message({ payload: payload('open', ['get_payouts']) }))).toBeNull();
    });
  });

  describe('openClarify', () => {
    /**
     * ★★ THE TICKET'S THIRD COMMENT, AS A PREDICATE. A form the user answered or closed must NOT
     * reappear fresh after a refresh — and since the state lives on the message, that survives a
     * reload with the browser remembering nothing at all.
     */
    it('offers an open form and never an answered or dismissed one', () => {
      expect(openClarify([message({ payload: payload('open') })])).not.toBeNull();
      expect(openClarify([message({ payload: payload('answered') })])).toBeNull();
      expect(openClarify([message({ payload: payload('dismissed') })])).toBeNull();
    });

    it('takes the most recent open form when a thread has several', () => {
      const found = openClarify([
        message({ id: 'old', payload: payload('open') }),
        message({ id: 'new', payload: payload('open', ['get_transaction']) }),
      ]);

      expect(found!.message.id).toBe('new');
    });

    /** ★ A user turn cannot carry a form: only the assistant offers one. */
    it('ignores a payload on a user turn', () => {
      expect(openClarify([message({ role: 'User', payload: payload('open') })])).toBeNull();
    });

    it('is null for an empty conversation', () => {
      expect(openClarify([])).toBeNull();
    });
  });

  describe('entity forms', () => {
    /** The reported tenant: three people a user can reasonably call "Camille". */
    const camilles = JSON.stringify({
      clarify: {
        kind: 'entity',
        state: 'open',
        options: [
          { function: 'get_payee_balance', argument: 'EPO9009',
            entity: { name: 'Camille Laurent', code: 'EPO9009', status: 'Terminated' } },
          { function: 'get_payee_balance', argument: 'EMP409',
            entity: { name: 'Camille Laurent', code: 'EMP409', status: 'Active' } },
          { function: 'get_payee_balance', argument: 'FR-301',
            entity: { name: 'Camille Martin', code: 'FR-301', status: 'Active' } },
        ],
      },
    });

    /**
     * ★★ THREE OPTIONS NAMING THE SAME FUNCTION MUST ALL SURVIVE. The reader used to be written for
     * forms where every option was a different function; nothing here may collapse them, or the panel
     * asking "which of these three people?" shows one person.
     */
    it('keeps every option even though they share one function', () => {
      const form = clarifyOf(message({ payload: camilles }));

      expect(form!.options.length).toBe(3);
      expect(form!.options.map((o) => o.argument)).toEqual(['EPO9009', 'EMP409', 'FR-301']);
      expect(form!.kind).toBe('entity');
    });

    it('carries the record each option stands for', () => {
      const form = clarifyOf(message({ payload: camilles }));

      expect(clarifyEntityOf(form!.options[0])).toEqual({
        name: 'Camille Laurent', code: 'EPO9009', status: 'Terminated',
      });
    });

    /** ★ A function option has no record, and must not pretend to. */
    it('reports no record for a function option', () => {
      const form = clarifyOf(message({ payload: payload('open') }));

      expect(clarifyEntityOf(form!.options[0])).toBeNull();
    });

    /** ★ An entity with no name cannot be rendered, so it falls back to its function label. */
    it('ignores a nameless record', () => {
      const nameless = JSON.stringify({
        clarify: {
          kind: 'entity', state: 'open',
          options: [{ function: 'get_payee_balance', argument: 'X', entity: { name: '' } }],
        },
      });

      expect(clarifyEntityOf(clarifyOf(message({ payload: nameless }))!.options[0])).toBeNull();
    });

    /**
     * ★★ THE COUNT ONLY APPEARS WHEN IT EXCEEDS WHAT IS SHOWN. Rendering "3 of 3" is noise; rendering
     * a number the server never sent would be worse than saying nothing.
     */
    it('keeps a total that exceeds the options shown', () => {
      const truncated = JSON.parse(camilles);
      truncated.clarify.totalMatches = 15;

      expect(clarifyOf(message({ payload: JSON.stringify(truncated) }))!.totalMatches).toBe(15);
    });

    it('drops a total that does not exceed the options shown', () => {
      const exact = JSON.parse(camilles);
      exact.clarify.totalMatches = 3;

      expect(clarifyOf(message({ payload: JSON.stringify(exact) }))!.totalMatches).toBeNull();
    });

    /** ★ Same §C2 rule as the function map: an unknown status renders as nothing, never as a token. */
    it('translates only the status tokens it knows', () => {
      for (const s of ['Active', 'OnLeave', 'Terminated', 'Draft', 'Archived']) {
        expect(clarifyStatusKey(s)).withContext(s).not.toBeNull();
      }

      expect(clarifyStatusKey('Reactivated')).toBeNull();
      expect(clarifyStatusKey('toString')).toBeNull();
      expect(clarifyStatusKey(null)).toBeNull();
    });
  });

  describe('the whitelists', () => {
    it('maps every offerable function to a label and a question', () => {
      for (const fn of [
        'get_transaction', 'get_plan_rules', 'get_payee_balance',
        'get_payee_plans', 'simulate_plan_rules',
      ]) {
        expect(clarifyOptionKey(fn)).withContext(fn).not.toBeNull();
        expect(clarifyPromptKey(fn)).withContext(fn).not.toBeNull();
      }
    });

    it('returns null for a function it has never heard of', () => {
      expect(clarifyOptionKey('get_payouts')).toBeNull();
      expect(clarifyPromptKey('get_payouts')).toBeNull();
    });

    /** ★ Own properties only — a plain object answers `toString` from its prototype. */
    it('does not resolve inherited object properties', () => {
      expect(clarifyOptionKey('toString')).toBeNull();
      expect(clarifyOptionKey('constructor')).toBeNull();
    });

    it('returns null for nothing at all', () => {
      expect(clarifyOptionKey(null)).toBeNull();
      expect(clarifyOptionKey(undefined)).toBeNull();
      expect(clarifyOptionKey('')).toBeNull();
    });
  });
});
