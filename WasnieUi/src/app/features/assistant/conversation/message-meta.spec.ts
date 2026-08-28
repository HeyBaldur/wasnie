import { formatMessageTime, plainTextOf } from './message-meta';

describe('plainTextOf — the answer as the reader sees it', () => {
  const hosts: HTMLElement[] = [];

  /** A real rendered block, attached so the browser lays it out and innerText means something. */
  function rendered(html: string): HTMLElement {
    const host = document.createElement('div');
    host.innerHTML = html;
    document.body.appendChild(host);
    hosts.push(host);
    return host;
  }

  // ★ ONLY THE NODES THIS SUITE MADE. An earlier version swept every `body > div`, which took Karma's
  // own reporter root with it and made the run end in an error after all the tests had passed. A test
  // that tidies up the harness it runs in is not tidy.
  afterEach(() => {
    hosts.splice(0).forEach(host => host.remove());
  });

  it('★ returns the text without any Markdown syntax', () => {
    const text = plainTextOf(rendered('<h2>Balance</h2><p>You earned <strong>1.200 EUR</strong>.</p>'));

    expect(text).toContain('Balance');
    expect(text).toContain('You earned 1.200 EUR.');
    expect(text).not.toContain('##');
    expect(text).not.toContain('**');
  });

  it('keeps a list readable, one item per line', () => {
    const text = plainTextOf(rendered('<ul><li>First</li><li>Second</li></ul>'));

    expect(text).toContain('First');
    expect(text).toContain('Second');
    expect(text).not.toContain('<li>');
  });

  // ★ textContent would run every cell together on one line; innerText keeps the shape the reader saw.
  it('★ keeps a table legible rather than collapsing it to one line', () => {
    const text = plainTextOf(rendered(
      '<table><tbody><tr><td>Anna</td><td>500</td></tr><tr><td>Bruno</td><td>300</td></tr></tbody></table>'));

    expect(text).toContain('Anna');
    expect(text).toContain('Bruno');
    expect(text.split('\n').length).toBeGreaterThan(1);
  });

  it('carries a code block through as its own text', () => {
    const text = plainTextOf(rendered('<pre><code>SELECT 1</code></pre>'));

    expect(text).toContain('SELECT 1');
  });

  it('trims the ends', () => {
    const text = plainTextOf(rendered('<p>   Hola   </p>'));

    expect(text).toBe('Hola');
  });

  it('returns an empty string for nothing at all', () => {
    expect(plainTextOf(null)).toBe('');
    expect(plainTextOf(undefined)).toBe('');
  });
});

describe('formatMessageTime — absolute, and in the reader\'s language', () => {
  const iso = '2026-08-25T15:42:00.000Z';

  it('gives a date and a time, not a relative phrase', () => {
    const text = formatMessageTime(iso, 'en');

    expect(text).toContain('2026');
    expect(text).toMatch(/\d{1,2}:\d{2}/);
    expect(text).not.toContain('ago');
  });

  // ★ The reason this uses Intl instead of Angular's DatePipe: the app registers no locale data, so
  // the pipe would print English month names to every reader whatever the UI language says.
  it('★ speaks the language it is given, not always English', () => {
    const spanish = formatMessageTime(iso, 'es');
    const english = formatMessageTime(iso, 'en');

    expect(spanish).not.toBe(english);
    expect(spanish.toLowerCase()).toContain('ago');   // "ago." — agosto
  });

  it('falls back to English rather than throwing on a missing language', () => {
    expect(formatMessageTime(iso, '')).toContain('2026');
  });

  // A row from a backend that ever sends something odd must not take the bar down with it.
  it('★ returns nothing for an unparseable timestamp instead of throwing', () => {
    expect(formatMessageTime('not a date', 'en')).toBe('');
  });

  it('is idempotent', () => {
    expect(formatMessageTime(iso, 'en')).toBe(formatMessageTime(iso, 'en'));
  });
});
