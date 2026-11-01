import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import { fragment, correctFragment } from '../src/html.js';
import { Parser } from './helpers.js';
const core = path.resolve('build/core-source');
let available = true;
try { await access(path.join(core, 'src/index.js')); } catch { available = false; }
// Unit-test runs may precede the concurrent core build. The explicit integration
// command is a release gate and MUST NOT pass by silently skipping.
const requireCore = process.env.npm_lifecycle_event === 'test:integration';
if (!available && requireCore) throw new Error('Build with the real browser core before integration tests.');
test('REAL shared core: EN/DE contract and HTML correction', { skip: !available }, async () => {
  const { createCorrector } = await import(pathToFileURL(path.join(core, 'src/index.js')));
  const [en, de] = await Promise.all(['en', 'de'].map(l => readFile(path.join(core, 'dictionaries', `${l}.txt`), 'utf8')));
  const corrector = createCorrector({ en, de });
  for (const language of ['en', 'de']) for (const mode of ['conservative', 'balanced']) {
    const text = language === 'en' ? 'This is teh message. https://example.invalid a@example.invalid 12345 REF-123' : 'Das ist ein Nachrciht. https://example.invalid a@example.invalid 12345 REF-123';
    const result = corrector.correct(text, { language, mode });
    assert.equal(typeof result.text, 'string'); assert.ok(Array.isArray(result.edits));
    let end = 0;
    for (const e of result.edits) { assert.ok(e.offset >= end); assert.equal(text.slice(e.offset, e.offset + e.length), e.original); end = e.offset + e.length; }
    assert.ok(result.text.includes('https://example.invalid a@example.invalid 12345 REF-123'));
    const patched = correctFragment(fragment(`<b>${language === 'en' ? 'teh message' : 'Nachrciht'}</b>`, Parser), corrector, { language, mode });
    assert.ok(patched.html.startsWith('<b>') && patched.html.endsWith('</b>'));
  }
  const typo = corrector.correct('recieve', { language: 'en', mode: 'balanced' });
  assert.equal(typo.text, 'receive', 'real core must perform a real correction');
  const german = corrector.correct('Nachrciht', { language: 'de', mode: 'balanced' });
  assert.equal(german.text, 'Nachricht', 'German real core correction');
  const unicode = corrector.correct('😀 recieve', { language: 'en', mode: 'balanced' });
  assert.equal(unicode.edits[0].offset, 3, 'offsets use original UTF-16 units');
  const protectedSplit = correctFragment(fragment('<b>https://exa</b>mpel.invalid a@exa<b>mpel.invalid</b> REF-123 12345', Parser), corrector, { language: 'en', mode: 'balanced' });
  assert.equal(protectedSplit.count, 0, 'whole-selection core protects URLs/addresses across markup');
  assert.equal(protectedSplit.text, 'https://exampel.invalid a@exampel.invalid REF-123 12345');
});
