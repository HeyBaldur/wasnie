import { composeFullName } from './fullname-composer';

describe('composeFullName', () => {
  it('returns the value of a single column', () => {
    expect(composeFullName({ Name: 'John' }, ['Name'])).toBe('John');
  });

  it('joins two columns with a single space', () => {
    expect(composeFullName({ First: 'John', Last: 'Doe' }, ['First', 'Last'])).toBe('John Doe');
  });

  it('joins three columns preserving the specified column order', () => {
    expect(composeFullName({ A: 'John', B: 'Michael', C: 'Doe' }, ['A', 'B', 'C'])).toBe('John Michael Doe');
  });

  it('respects caller-specified column order even when it differs from insertion order', () => {
    expect(composeFullName({ First: 'John', Last: 'Doe' }, ['Last', 'First'])).toBe('Doe John');
  });

  it('filters out columns whose value is empty string', () => {
    expect(composeFullName({ First: 'John', Middle: '', Last: 'Doe' }, ['First', 'Middle', 'Last'])).toBe('John Doe');
  });

  it('filters out columns not present in the row', () => {
    expect(composeFullName({ First: 'John' }, ['First', 'Last'])).toBe('John');
  });

  it('returns empty string when all column values are empty', () => {
    expect(composeFullName({ First: '', Last: '' }, ['First', 'Last'])).toBe('');
  });

  it('returns empty string when columns array is empty', () => {
    expect(composeFullName({ Name: 'John' }, [])).toBe('');
  });

  it('trims leading and trailing whitespace from each value', () => {
    expect(composeFullName({ First: '  John  ', Last: '  Doe  ' }, ['First', 'Last'])).toBe('John Doe');
  });

  it('treats whitespace-only values as empty and filters them out', () => {
    expect(composeFullName({ First: 'John', Middle: '   ', Last: 'Doe' }, ['First', 'Middle', 'Last'])).toBe('John Doe');
  });

  it('collapses internal whitespace within a single value', () => {
    expect(composeFullName({ Name: 'John  Michael  Doe' }, ['Name'])).toBe('John Michael Doe');
  });

  it('collapses whitespace created by joining trimmed values', () => {
    expect(composeFullName({ First: 'John ', Last: ' Doe' }, ['First', 'Last'])).toBe('John Doe');
  });

  it('ignores row keys not listed in columns', () => {
    expect(composeFullName({ First: 'John', Last: 'Doe', Email: 'j@d.com' }, ['First', 'Last'])).toBe('John Doe');
  });

  it('handles undefined row values gracefully', () => {
    const row = { First: 'John', Last: undefined as unknown as string };
    expect(composeFullName(row, ['First', 'Last'])).toBe('John');
  });

  it('handles accented and non-ASCII characters without mangling', () => {
    expect(composeFullName({ Prénom: 'Jean-Luc', Nom: 'Légère' }, ['Prénom', 'Nom'])).toBe('Jean-Luc Légère');
  });
});
