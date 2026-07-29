#!/usr/bin/env node
/**
 * Fails the build when a template asks for an icon the registry does not have.
 *
 * An unknown icon name is silent: `<app-icon name="chevron-up">` renders an empty <svg>, the page
 * lays out exactly as before, and nobody finds out until someone looks at that corner of that
 * screen. Eight of them shipped that way before this check existed.
 *
 * The names are READ FROM THE TEMPLATES on every run — there is deliberately no list of icon names
 * in this file. A hard-coded list would be correct on the day it was written and wrong from the
 * next commit on, which is the failure mode it is supposed to prevent.
 *
 * Runs in Node rather than as a Karma spec because it needs the file system: a browser test cannot
 * enumerate .html files, so it could only ever check a list someone typed by hand.
 *
 * Usage:  node scripts/verify-icons.mjs [--json]
 * Exit:   0 clean · 1 unknown icon used · 2 registry entry with no path
 */
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(fileURLToPath(new URL('.', import.meta.url)), '..');
const SRC = join(ROOT, 'src', 'app');
const REGISTRY = join(SRC, 'shared', 'components', 'icon', 'icon.component.ts');

// ── The registry: names → svg body ──────────────────────────────────────────
const registrySource = readFileSync(REGISTRY, 'utf8');
const registryBlock = registrySource.slice(
  registrySource.indexOf('const ICONS'),
  registrySource.indexOf('\n};', registrySource.indexOf('const ICONS')),
);
const defined = new Map();
for (const m of registryBlock.matchAll(/'([a-z0-9-]+)':\s*\n?\s*'([^']*)'/g)) {
  defined.set(m[1], m[2]);
}

// ── Every icon name a template or component asks for ────────────────────────
// Four shapes appear in this codebase, so all four are parsed:
//   <app-icon name="x">            static attribute
//   [name]="'x'"                   bound literal
//   [name]="cond ? 'x' : 'y'"      bound ternary — both branches count
//   icon: 'x'                      nav/toast config objects in .ts
const PATTERNS = [
  /<app-icon[^>]*?\bname="([a-z0-9-]+)"/g,
  /\[name\]="'([a-z0-9-]+)'"/g,
  /\[name\]="[^"]*?'([a-z0-9-]+)'\s*:\s*'([a-z0-9-]+)'/g,
  /\bicon:\s*'([a-z0-9-]+)'/g,
];

function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) yield* walk(full);
    else if (/\.(html|ts)$/.test(entry) && !entry.endsWith('.spec.ts')) yield full;
  }
}

const used = new Map(); // name -> Set<file>
for (const file of walk(SRC)) {
  const text = readFileSync(file, 'utf8');
  const where = relative(ROOT, file).split(sep).join('/');
  for (const pattern of PATTERNS) {
    for (const match of text.matchAll(pattern)) {
      for (const name of match.slice(1).filter(Boolean)) {
        if (!used.has(name)) used.set(name, new Set());
        used.get(name).add(where);
      }
    }
  }
}

// ── Verdict ─────────────────────────────────────────────────────────────────
const unknown = [...used.entries()].filter(([name]) => !defined.has(name));
const empty = [...defined.entries()].filter(([, body]) => body.trim() === '');
const unused = [...defined.keys()].filter((name) => !used.has(name));

if (process.argv.includes('--json')) {
  console.log(JSON.stringify({
    defined: defined.size,
    used: used.size,
    unknown: unknown.map(([n, f]) => ({ icon: n, files: [...f] })),
    empty: empty.map(([n]) => n),
    unused,
  }, null, 2));
} else {
  console.log(`icons: ${defined.size} defined, ${used.size} referenced by templates`);
  if (unknown.length) {
    console.error(`\n✘ ${unknown.length} icon name(s) used but NOT in the registry — these render as an empty <svg>:`);
    for (const [name, files] of unknown.sort()) {
      console.error(`   ${name.padEnd(20)} ${[...files].sort().join(', ')}`);
    }
  }
  if (empty.length) {
    console.error(`\n✘ ${empty.length} registry entr(y|ies) with an empty path: ${empty.map(([n]) => n).join(', ')}`);
  }
  if (unused.length) {
    // Not a failure: an icon can legitimately wait for the screen that will use it.
    console.log(`\nnote: ${unused.length} registered icon(s) currently unused — ${unused.sort().join(', ')}`);
  }
  if (!unknown.length && !empty.length) console.log('\n✓ every icon a template asks for exists');
}

process.exit(unknown.length ? 1 : empty.length ? 2 : 0);
