import { detectFullNameColumns, detectOtherField, OTHER_FIELD_PATTERNS } from './column-auto-detect';

describe('detectFullNameColumns', () => {
  // ── Single-column detection ──────────────────────────────────────────────────

  it('detects exact single-name column "Full Name"', () => {
    expect(detectFullNameColumns(['Full Name'])).toEqual(['Full Name']);
  });

  it('detects exact single-name column "Name"', () => {
    expect(detectFullNameColumns(['Name'])).toEqual(['Name']);
  });

  it('detects underscore variant "full_name"', () => {
    expect(detectFullNameColumns(['full_name'])).toEqual(['full_name']);
  });

  it('detects Spanish single-name "nombre completo"', () => {
    expect(detectFullNameColumns(['nombre completo'])).toEqual(['nombre completo']);
  });

  it('prefers single-name column when both single and first/last exist', () => {
    expect(detectFullNameColumns(['Full Name', 'First Name', 'Last Name'])).toEqual(['Full Name']);
  });

  // ── First + Last combination ─────────────────────────────────────────────────

  it('detects English first + last combination', () => {
    expect(detectFullNameColumns(['First Name', 'Last Name'])).toEqual(['First Name', 'Last Name']);
  });

  it('detects Spanish first + last combination', () => {
    expect(detectFullNameColumns(['Nombre', 'Apellido'])).toEqual(['Nombre', 'Apellido']);
  });

  it('detects Polish first + last combination', () => {
    expect(detectFullNameColumns(['Imię', 'Nazwisko'])).toEqual(['Imię', 'Nazwisko']);
  });

  it('is case-insensitive', () => {
    const result = detectFullNameColumns(['FIRST NAME', 'LAST NAME']);
    expect(result).toEqual(['FIRST NAME', 'LAST NAME']);
  });

  // ── Order preservation ────────────────────────────────────────────────────────

  it('preserves original header order even when last name comes first', () => {
    expect(detectFullNameColumns(['Last Name', 'First Name'])).toEqual(['Last Name', 'First Name']);
  });

  it('preserves order: first, middle, last', () => {
    expect(detectFullNameColumns(['First Name', 'Middle Name', 'Last Name'])).toEqual([
      'First Name', 'Middle Name', 'Last Name',
    ]);
  });

  it('preserves order with Spanish double-last-name convention', () => {
    expect(detectFullNameColumns(['Nombre', 'Primer Apellido', 'Segundo Apellido'])).toEqual([
      'Nombre', 'Primer Apellido', 'Segundo Apellido',
    ]);
  });

  // ── Last Name 2 / "last name 2" fix ──────────────────────────────────────────

  it('detects "Last Name 2" as a secondary last-name column', () => {
    expect(detectFullNameColumns(['First Name', 'Last Name 1', 'Last Name 2'])).toEqual([
      'First Name', 'Last Name 1', 'Last Name 2',
    ]);
  });

  it('"Last Name 2" is not confused with a primary last name', () => {
    const result = detectFullNameColumns(['First Name', 'Last Name 2']);
    // last name 2 alone satisfies the "has-last" requirement
    expect(result).toContain('First Name');
    expect(result).toContain('Last Name 2');
  });

  // ── Word-boundary bug fix: "Prénom" must not match "nom" ─────────────────────

  it('does not classify "Prénom" as a last-name column', () => {
    expect(detectFullNameColumns(['Prénom', 'Nom'])).toEqual(['Prénom', 'Nom']);
  });

  it('"Prénom" is classified as first name, not last name', () => {
    const result = detectFullNameColumns(['Prénom', 'Nom']);
    // If prénom were misclassified as last, we'd get [] (no first col) or just ['Nom']
    expect(result.length).toBe(2);
  });

  // ── Incomplete sets return [] ─────────────────────────────────────────────────

  it('returns [] when only first name column is present', () => {
    expect(detectFullNameColumns(['First Name'])).toEqual([]);
  });

  it('returns [] when only last name column is present', () => {
    expect(detectFullNameColumns(['Last Name'])).toEqual([]);
  });

  it('returns [] for empty headers array', () => {
    expect(detectFullNameColumns([])).toEqual([]);
  });

  it('returns [] when no name-related columns are present', () => {
    expect(detectFullNameColumns(['Employee Code', 'Email', 'Department'])).toEqual([]);
  });

  // ── Non-name columns are excluded from result ─────────────────────────────────

  it('ignores non-name columns in a mixed header list', () => {
    expect(detectFullNameColumns(['Employee Code', 'First Name', 'Email', 'Last Name'])).toEqual([
      'First Name', 'Last Name',
    ]);
  });
});

// ─────────────────────────────────────────────────────────────────────────────

describe('detectOtherField', () => {
  it('matches exact email column "Email"', () => {
    expect(detectOtherField(['Email'], OTHER_FIELD_PATTERNS['emailColumn'])).toBe('Email');
  });

  it('matches partial email column "My Email Address"', () => {
    expect(detectOtherField(['My Email Address'], OTHER_FIELD_PATTERNS['emailColumn'])).toBe('My Email Address');
  });

  it('matches hyphenated pattern "E-Mail"', () => {
    expect(detectOtherField(['E-Mail'], OTHER_FIELD_PATTERNS['emailColumn'])).toBe('E-Mail');
  });

  it('matches accented Spanish email "Correo Electrónico"', () => {
    expect(detectOtherField(['Correo Electrónico'], OTHER_FIELD_PATTERNS['emailColumn'])).toBe('Correo Electrónico');
  });

  it('matches employee code column', () => {
    expect(detectOtherField(['Employee Code'], OTHER_FIELD_PATTERNS['employeeCodeColumn'])).toBe('Employee Code');
  });

  it('matches hire date column', () => {
    expect(detectOtherField(['Hire Date'], OTHER_FIELD_PATTERNS['hireDateColumn'])).toBe('Hire Date');
  });

  it('matches "Start Date" as hire date', () => {
    expect(detectOtherField(['Start Date'], OTHER_FIELD_PATTERNS['hireDateColumn'])).toBe('Start Date');
  });

  it('matches role column', () => {
    expect(detectOtherField(['Role'], OTHER_FIELD_PATTERNS['roleColumn'])).toBe('Role');
  });

  it('matches manager column', () => {
    expect(detectOtherField(['Manager'], OTHER_FIELD_PATTERNS['managerColumn'])).toBe('Manager');
  });

  it('returns empty string when no header matches', () => {
    expect(detectOtherField(['Department', 'Region'], OTHER_FIELD_PATTERNS['emailColumn'])).toBe('');
  });

  it('is case-insensitive', () => {
    expect(detectOtherField(['EMAIL'], OTHER_FIELD_PATTERNS['emailColumn'])).toBe('EMAIL');
  });

  it('prefers exact match over partial match regardless of column order', () => {
    // 'Email' is an exact match; 'My Email Address' would also match via partial
    expect(detectOtherField(['My Email Address', 'Email'], OTHER_FIELD_PATTERNS['emailColumn'])).toBe('Email');
  });
});
