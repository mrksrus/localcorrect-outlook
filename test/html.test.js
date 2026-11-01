import test from 'node:test';
import assert from 'node:assert/strict';
import { fragment, assertSelectionSafe, correctFragment } from '../src/html.js';
import { Parser, fakeCorrector } from './helpers.js';
const options = { language: 'en', mode: 'conservative' };
test('formatting retained and HTML escaped', () => { const f = fragment('<b>teh</b> &amp; <i>message</i>', Parser); const result = correctFragment(f, fakeCorrector, options); assert.equal(result.html, '<b>the</b> &amp; <i>message</i>'); });
for (const html of ['<p>Please teh</p><p>tomorrow.</p>', '<div>Please teh</div><div>tomorrow.</div>', 'Please teh<br>tomorrow.']) test(`structural separator ${html}`, () => {
  const f = fragment(html, Parser); assert.equal(f.text, 'Please teh\ntomorrow.');
  assertSelectionSafe(`<div>${html}</div>`, f, Parser);
  const result = correctFragment(f, fakeCorrector, options);
  assert.equal(result.text, 'Please the\ntomorrow.'); assert.equal(result.count, 1);
});
test('explicit repeated BR and inline nodes preserve logical text', () => { assert.equal(fragment('<b>teh</b><br><br>message<br>', Parser).text, 'teh\n\nmessage\n'); });
test('BR inside final block is not trimmed as a synthetic separator', () => { assert.equal(fragment('<p>teh<br></p>', Parser).text, 'teh\n'); });
test('synthetic separator edit cannot be applied', () => {
  const core = { correct: text => ({ text: 'x', edits: [{ offset: 0, length: text.length, original: text, replacement: 'x' }], skipped: 0 }) };
  const result = correctFragment(fragment('<p>teh</p><p>message</p>', Parser), core, options);
  assert.equal(result.count, 0); assert.equal(result.text, 'teh\nmessage');
});
test('split word across inline markup skipped', () => { const f = fragment('<b>teh</b>re', Parser); assert.equal(correctFragment(f, fakeCorrector, options).count, 0); });
for (const html of ['<a href="https://example.invalid">teh</a>', '<script>teh</script>', '<img src="https://example.invalid/pixel">', '<span onclick="x()">teh</span>', '<span style="background:url(https://example.invalid)">teh</span>', '<blockquote>teh</blockquote>']) test(`refuses unsafe fragment ${html.slice(0, 30)}`, () => assert.throws(() => fragment(html, Parser)));
for (const container of ['<blockquote>teh phrase</blockquote>', '<div id="Signature">teh phrase</div>', '<div class="gmail_quote">teh phrase</div>', '<div>-- </div><div>teh phrase</div>', '<div>From: Person</div><div>teh phrase</div>']) test(`protects body ${container.slice(0, 30)}`, () => assert.throws(() => assertSelectionSafe(container, fragment('teh phrase', Parser), Parser)));
test('ambiguous repeated text refused', () => assert.throws(() => assertSelectionSafe('<p>teh phrase</p><p>teh phrase</p>', fragment('teh phrase', Parser), Parser), /uniquely/));
test('own text before signature allowed', () => assertSelectionSafe('<p>teh phrase</p><div id="Signature">Name</div>', fragment('teh phrase', Parser), Parser));
test('bad edit offsets and overlap rejected', () => { const core = { correct: () => ({ text: 'x', skipped: 0, edits: [{ offset: 0, length: 3, original: 'teh', replacement: 'the' }, { offset: 1, length: 1, original: 'e', replacement: 'x' }] }) }; assert.throws(() => correctFragment(fragment('teh', Parser), core, options), /contract/); });
test('replacement markup becomes text, not executable HTML', () => { const core = { correct: () => ({ text: '<img src=x>', skipped: 0, edits: [{ offset: 0, length: 3, original: 'teh', replacement: '<img src=x>' }] }) }; assert.match(correctFragment(fragment('teh', Parser), core, options).html, /&lt;img/); });
