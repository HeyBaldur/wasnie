export const SINGLE_NAME_PATTERNS = [
  'full name', 'fullname', 'full_name', 'name', 'nombre completo',
  'nome completo', 'nom complet', 'vollständiger name',
  'imię i nazwisko', 'imie i nazwisko', 'nome e cognome',
];

export const FIRST_NAME_PATTERNS = [
  'first name', 'firstname', 'first_name', 'given name', 'givenname', 'first',
  'nombre', 'primer nombre', 'prénom', 'prenom', 'vorname',
  'nome', 'primeiro nome', 'imię', 'imie',
];

export const LAST_NAME_PATTERNS = [
  'last name', 'lastname', 'last_name', 'surname', 'family name', 'familyname', 'last',
  'apellido', 'apellido 1', 'apellido paterno', 'primer apellido', 'apellidos',
  'nom', 'nachname', 'familienname', 'cognome', 'apelido', 'sobrenome', 'nazwisko',
];

// Checked before LAST_NAME_PATTERNS so 'apellido 2' / 'last name 2' don't fall into 'last'
export const LAST_NAME2_PATTERNS = [
  'last name 2', 'last_name_2', 'lastname2',
  'apellido 2', 'segundo apellido', 'apellido materno', 'segundo apelido',
  'middle name', 'middlename', 'middle_name', 'segundo nome',
  'deuxième nom', 'deuxieme nom', 'zweiter nachname',
];

export const OTHER_FIELD_PATTERNS: Record<string, string[]> = {
  employeeCodeColumn: ['employee code', 'employeecode', 'employee_code', 'emp code', 'code', 'codigo', 'id', 'employee id', 'kod pracownika'],
  emailColumn: ['email', 'e-mail', 'correo', 'correo electrónico', 'correo electronico', 'e mail', 'poczta', 'adres email'],
  hireDateColumn: ['hire date', 'hiredate', 'hire_date', 'fecha ingreso', 'fecha de contratación', 'start date', 'data zatrudnienia'],
  roleColumn: ['role', 'position', 'cargo', 'puesto', 'stanowisko', 'rola'],
  managerColumn: ['manager employee code', 'manager code', 'manager id', 'manager', 'supervisor', 'jefe', 'przełożony', 'kod menedżera'],
  employmentTypeColumn: [
    'employment type', 'employmenttype', 'employment_type', 'contract type', 'contracttype',
    'tipo de contrato', 'tipo contrato', 'typ umowy', 'rodzaj zatrudnienia',
    'tipo di contratto', 'type de contrat', 'beschäftigungsart', 'tipo de empleo',
  ],
  locationColumn: [
    'location', 'office', 'site', 'workplace', 'work location', 'office location',
    'ubicación', 'ubicacion', 'lugar de trabajo', 'lokalizacja', 'miejsce pracy',
    'sede', 'luogo di lavoro', 'lieu de travail', 'arbeitsort', 'standort',
  ],
};

// For first/last/last2 patterns: single-word patterns use whole-word matching (prevents 'nom'
// matching inside 'prénom'). Multi-word patterns use substring matching.
function matchesPhrase(normalized: string, pattern: string): boolean {
  if (normalized === pattern) return true;
  if (pattern.includes(' ')) {
    return normalized.includes(pattern);
  }
  return normalized.split(/[\s_\-]+/).includes(pattern);
}

// For single-name patterns: single-word entries need exact match (prevents 'name' matching inside
// 'First Name' or 'Last Name'). Multi-word entries use substring match.
function matchesSingleNamePattern(normalized: string, pattern: string): boolean {
  if (normalized === pattern) return true;
  return pattern.includes(' ') && normalized.includes(pattern);
}

type ColumnType = 'single' | 'first' | 'last2' | 'last';

function classifyColumn(normalized: string): ColumnType | null {
  if (SINGLE_NAME_PATTERNS.some(p => matchesSingleNamePattern(normalized, p))) return 'single';
  if (FIRST_NAME_PATTERNS.some(p => matchesPhrase(normalized, p))) return 'first';
  if (LAST_NAME2_PATTERNS.some(p => matchesPhrase(normalized, p))) return 'last2';
  if (LAST_NAME_PATTERNS.some(p => matchesPhrase(normalized, p))) return 'last';
  return null;
}

export function detectFullNameColumns(headers: string[]): string[] {
  const classified: { header: string; type: ColumnType }[] = [];

  for (const header of headers) {
    const type = classifyColumn(header.toLowerCase().trim());
    if (type !== null) classified.push({ header, type });
  }

  const single = classified.find(c => c.type === 'single');
  if (single) return [single.header];

  const hasFirst = classified.some(c => c.type === 'first');
  const hasLastLike = classified.some(c => c.type === 'last' || c.type === 'last2');
  if (!hasFirst || !hasLastLike) return [];

  // Return columns in their original header order
  return classified.map(c => c.header);
}

// Two-pass: exact match wins over partial match, regardless of column order.
export function detectOtherField(headers: string[], patterns: string[]): string {
  for (const h of headers) {
    const normalized = h.toLowerCase().trim();
    if (patterns.some(p => normalized === p)) return h;
  }
  for (const h of headers) {
    const normalized = h.toLowerCase().trim();
    if (patterns.some(p => matchesPhrase(normalized, p))) return h;
  }
  return '';
}
