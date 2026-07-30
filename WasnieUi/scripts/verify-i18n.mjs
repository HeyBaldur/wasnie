#!/usr/bin/env node
/**
 * Fails the build when a translation is missing — including when it is missing from ALL THREE locales.
 *
 * Comparing the locale files against each other only proves they agree; a key nobody added is absent
 * everywhere and passes that comparison happily. That is exactly how LEDGER.TYPE_DATA_CORRECTION_CREDIT
 * shipped and printed its own key on screen. So this checks completeness against the CODE:
 *
 *   1. Every LedgerTransactionType member in the C# enum has a LEDGER.TYPE_<SCREAMING_SNAKE> key in
 *      all three locales — the screen derives that key from the type name at runtime.
 *   2. The frontend's LEDGER_TRANSACTION_TYPES array matches that enum, so the two cannot drift.
 *   3. Every literal key passed to `| translate` exists in en.json.
 *   4. The locales agree with each other (the original check, kept — it catches a one-file addition).
 *
 * Nothing here lists icon or key names by hand: both sides are read from source on every run.
 *
 * Usage:  node scripts/verify-i18n.mjs
 * Exit:   0 clean · 1 something missing
 */
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const UI = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const REPO = join(UI, '..');
const I18N = join(UI, 'src', 'assets', 'i18n');
const SRC = join(UI, 'src', 'app');
const ENUMS = join(REPO, 'WasnieApi/src/Wasnie.Domain/Compensation/Enums/LedgerEnums.cs');
const MODEL = join(SRC, 'features/ledger/models/ledger.model.ts');

const flat = (o, p = '') =>
  Object.entries(o).reduce((acc, [k, v]) => {
    const key = p ? `${p}.${k}` : k;
    return typeof v === 'object' && v !== null
      ? { ...acc, ...flat(v, key) }
      : { ...acc, [key]: v };
  }, {});

const locales = Object.fromEntries(
  ['en.json', 'es.json', 'pl.json'].map((f) => [f, flat(JSON.parse(readFileSync(join(I18N, f), 'utf8')))]),
);

const screaming = (name) => name.replace(/(?<!^)(?=[A-Z])/g, '_').toUpperCase();
const problems = [];

// ── 1. Enum coverage ────────────────────────────────────────────────────────
const enumSrc = readFileSync(ENUMS, 'utf8');
const enumBlock = enumSrc.slice(
  enumSrc.indexOf('public enum LedgerTransactionType'),
  enumSrc.indexOf('\n}', enumSrc.indexOf('public enum LedgerTransactionType')),
);
const members = [...enumBlock.matchAll(/^ {4}(\w+) = \d+,/gm)].map((m) => m[1]);

console.log(`LedgerTransactionType: ${members.length} members`);
for (const member of members) {
  const key = `LEDGER.TYPE_${screaming(member)}`;
  const missing = Object.entries(locales).filter(([, d]) => !(key in d)).map(([f]) => f);
  console.log(`  ${member.padEnd(26)} -> ${key.padEnd(44)} ${missing.length ? 'MISSING in ' + missing.join(',') : 'OK'}`);
  if (missing.length) problems.push(`${key} missing in ${missing.join(', ')}`);
}

// ── 2. The frontend's copy of the enum must match it ────────────────────────
const modelSrc = readFileSync(MODEL, 'utf8');
const arrayBlock = modelSrc.slice(
  modelSrc.indexOf('LEDGER_TRANSACTION_TYPES = ['),
  modelSrc.indexOf('] as const', modelSrc.indexOf('LEDGER_TRANSACTION_TYPES = [')),
);
const frontTypes = [...arrayBlock.matchAll(/'(\w+)'/g)].map((m) => m[1]);
const onlyBackend = members.filter((m) => !frontTypes.includes(m));
const onlyFrontend = frontTypes.filter((t) => !members.includes(t));
if (onlyBackend.length || onlyFrontend.length) {
  problems.push(`LEDGER_TRANSACTION_TYPES drifted — backend only: [${onlyBackend}] frontend only: [${onlyFrontend}]`);
}
console.log(`\nfrontend type list: ${frontTypes.length} entries, ${onlyBackend.length + onlyFrontend.length === 0 ? 'in sync with the enum' : 'DRIFTED'}`);

// ── 3. Literal translate keys ───────────────────────────────────────────────
function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) yield* walk(full);
    else if (/\.(html|ts)$/.test(entry) && !entry.endsWith('.spec.ts')) yield full;
  }
}
const PATTERNS = [
  /'([A-Z][A-Z0-9_]*(?:\.[A-Z0-9_]+)+)'\s*\|\s*translate/g,
  /\.instant\(\s*'([A-Z][A-Z0-9_]*(?:\.[A-Z0-9_]+)+)'/g,
];
const used = new Set();
for (const file of walk(SRC)) {
  const text = readFileSync(file, 'utf8');
  for (const p of PATTERNS) for (const m of text.matchAll(p)) used.add(m[1]);
}
const undefinedKeys = [...used].filter((k) => !(k in locales['en.json'])).sort();
console.log(`literal translate keys: ${used.size} used, ${undefinedKeys.length} undefined`);
if (undefinedKeys.length) problems.push(`used but undefined: ${undefinedKeys.join(', ')}`);

// ── 4. Parity between locales ───────────────────────────────────────────────
const en = new Set(Object.keys(locales['en.json']));
for (const other of ['es.json', 'pl.json']) {
  const diff = [...new Set([...en, ...Object.keys(locales[other])])].filter(
    (k) => en.has(k) !== (k in locales[other]),
  );
  console.log(`parity en/${other}: ${diff.length ? diff.join(', ') : 'identical'}`);
  if (diff.length) problems.push(`parity en/${other}: ${diff.join(', ')}`);
}

if (problems.length) {
  console.error(`\n✘ ${problems.length} problem(s):`);
  for (const p of problems) console.error(`   ${p}`);
  process.exit(1);
}
console.log('\n✓ every translation the code needs exists, in all three locales');
