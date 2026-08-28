import { clearAllDrafts, draftKeyFor, readDrafts, writeDrafts } from './draft-storage';

describe('draft storage — a backup that is never allowed to break anything', () => {
  const key = draftKeyFor('t1', 'u1');

  beforeEach(() => sessionStorage.clear());
  afterEach(() => sessionStorage.clear());

  describe('the key', () => {
    // ★ Without the tenant and the user in the key this feature is a data leak, not a convenience: on a
    // shared machine the next person opens the assistant and reads the previous one's half-written
    // question about somebody's pay.
    it('★ separates two users in the same tenant', () => {
      expect(draftKeyFor('t1', 'u1')).not.toBe(draftKeyFor('t1', 'u2'));
    });

    it('★ separates the same user id across two tenants', () => {
      expect(draftKeyFor('t1', 'u1')).not.toBe(draftKeyFor('t2', 'u1'));
    });

    it('gives nobody-signed-in its own bucket rather than a shared one', () => {
      expect(draftKeyFor(null, null)).not.toBe(draftKeyFor('t1', 'u1'));
    });
  });

  describe('round trip', () => {
    it('writes and reads a map back', () => {
      writeDrafts(key, { a: 'hola', b: 'chau' });

      expect(readDrafts(key)).toEqual({ a: 'hola', b: 'chau' });
    });

    it('an empty map leaves nothing behind', () => {
      writeDrafts(key, { a: 'hola' });
      writeDrafts(key, {});

      expect(sessionStorage.getItem(key)).toBeNull();
      expect(readDrafts(key)).toEqual({});
    });

    it('drops empty drafts rather than storing blanks', () => {
      writeDrafts(key, { a: 'hola', b: '' });

      expect(readDrafts(key)).toEqual({ a: 'hola' });
    });
  });

  describe('reading defensively', () => {
    it('returns an empty map when there is nothing stored', () => {
      expect(readDrafts(key)).toEqual({});
    });

    // ★ Storage holds text some other version of this code wrote. Trusting its shape turns a bad string
    // into a crash on the assistant's first render.
    it('★ discards unparseable JSON instead of throwing', () => {
      sessionStorage.setItem(key, '{not json');

      expect(() => readDrafts(key)).not.toThrow();
      expect(readDrafts(key)).toEqual({});
    });

    it('★ discards a value of the wrong shape', () => {
      sessionStorage.setItem(key, JSON.stringify(['a', 'b']));

      expect(readDrafts(key)).toEqual({});
    });

    // One bad entry must not cost the user the drafts that are fine.
    it('★ keeps the good entries and drops the ones that are not strings', () => {
      sessionStorage.setItem(key, JSON.stringify({ a: 'hola', b: 42, c: null }));

      expect(readDrafts(key)).toEqual({ a: 'hola' });
    });
  });

  describe('when storage itself fails', () => {
    /** Private mode, a full quota and a browser policy all look like this. */
    function breakStorage(): void {
      spyOn(Storage.prototype, 'getItem').and.throwError('denied');
      spyOn(Storage.prototype, 'setItem').and.throwError('denied');
      spyOn(Storage.prototype, 'removeItem').and.throwError('denied');
    }

    // ★ Memory is the source of truth; a backup that cannot be written is not a reason the user cannot
    // type or send. None of these may propagate.
    it('★ reading degrades to an empty map', () => {
      breakStorage();

      expect(() => readDrafts(key)).not.toThrow();
      expect(readDrafts(key)).toEqual({});
    });

    it('★ writing swallows the failure', () => {
      breakStorage();

      expect(() => writeDrafts(key, { a: 'hola' })).not.toThrow();
    });

    it('★ clearing swallows the failure', () => {
      breakStorage();

      expect(() => clearAllDrafts()).not.toThrow();
    });
  });

  // ★ sessionStorage has a quota, and one pasted document must not take the other conversations' drafts
  // down with it. Over the cap the draft still works — it just is not backed up.
  it('★ does not persist a draft past the size cap, and keeps the others', () => {
    const huge = 'x'.repeat(20_001);
    writeDrafts(key, { small: 'hola', huge });

    const stored = readDrafts(key);
    expect(stored['small']).toBe('hola');
    expect(stored['huge']).toBeUndefined();
  });

  it('clearAllDrafts forgets every user, and nothing else', () => {
    writeDrafts(draftKeyFor('t1', 'u1'), { a: 'uno' });
    writeDrafts(draftKeyFor('t2', 'u9'), { b: 'dos' });
    sessionStorage.setItem('wasnie:something-else', 'keep me');

    clearAllDrafts();

    expect(readDrafts(draftKeyFor('t1', 'u1'))).toEqual({});
    expect(readDrafts(draftKeyFor('t2', 'u9'))).toEqual({});
    expect(sessionStorage.getItem('wasnie:something-else')).toBe('keep me');
  });
});
