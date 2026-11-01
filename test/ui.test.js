import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFile, access } from 'node:fs/promises';
import { parseHTML } from 'linkedom';
let available = true;
try { await access('build/site/taskpane.js'); } catch { available = false; }
test('compiled pane synthetic DOM: no automatic Copy, explicit Copy, reset, no input mutation', { skip: !available }, async () => {
  const { document, Event } = parseHTML(await readFile('build/site/demo.html', 'utf8'));
  const $ = id => document.getElementById(id);
  $('language').querySelector('option').setAttribute('selected', '');
  $('mode').querySelector('option').setAttribute('selected', '');
  $('draft').value = 'This is a message.';
  const copied = [], handlers = {};
  const sandbox = {
    document, navigator: { clipboard: { async writeText(text) { copied.push(text); } } },
    window: { addEventListener(name, fn) { handlers[name] = fn; } },
    setInterval() {}, setTimeout, clearTimeout,
    async fetch(url) { return { ok: true, text: () => readFile(`build/site/${url}`, 'utf8') }; }
  };
  vm.runInNewContext(await readFile('build/site/taskpane.js', 'utf8'), sandbox);
  const until = async predicate => { for (let i = 0; i < 500; i++) { if (predicate()) return; await new Promise(r => setTimeout(r, 10)); } throw new Error('Pane fixture timed out'); };
  await until(() => !$('correct').disabled);
  $('correct').dispatchEvent(new Event('click'));
  await until(() => !$('copy').disabled);
  assert.equal($('draft').value, 'This is a message.'); assert.equal($('output').value, 'This is a message.'); assert.deepEqual(copied, []);
  $('copy').dispatchEvent(new Event('click'));
  await until(() => copied.length === 1); assert.deepEqual(copied, ['This is a message.']);
  $('clear').dispatchEvent(new Event('click')); assert.equal($('output').value, ''); assert.equal($('copy').disabled, true); assert.equal(copied.length, 1);
  $('correct').dispatchEvent(new Event('click')); await until(() => !$('copy').disabled);
  handlers.pagehide(); assert.equal($('output').value, ''); assert.equal(copied.length, 1);
});
